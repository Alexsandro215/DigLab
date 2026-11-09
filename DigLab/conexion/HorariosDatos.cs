using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
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
        YaRegistrado,
        // Los de los modos de acceso (db/11), al final para no mover los de antes.
        /// <summary>Generacion de codigos: el profesor todavia no abre la de ahora.</summary>
        SinAbrir,
        /// <summary>Generacion de codigos: tecleo el codigo fijo del profesor.</summary>
        CodigoFijo,
        /// <summary>Sin codigo: hay varias clases abiertas y el alumno tiene que elegir (Opciones).</summary>
        ElegirClase,
        /// <summary>El laboratorio cambio de modo: hay que rehacer el paso 1.</summary>
        ModoCambiado,
        /// <summary>Este laboratorio esta deshabilitado: aqui no se registra nada.</summary>
        LaboratorioDeshabilitado
    }

    public class ResultadoCodigo
    {
        public MotivoCodigo Motivo { get; set; }
        /// <summary>Texto para el alumno, ya redactado.</summary>
        public string Mensaje { get; set; }
        /// <summary>La sesion de hoy, solo si Motivo es Aceptado o YaRegistrado.</summary>
        public VentanaCodigo Ventana { get; set; }
        /// <summary>El modo de acceso del laboratorio con el que se resolvio.</summary>
        public ModoCodigo Modo { get; set; }
        /// <summary>Con ElegirClase: las clases abiertas entre las que elegir.</summary>
        public List<SesionAgenda> Opciones { get; set; }

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
        /// <summary>"Marco Llinas": primer nombre y apellido paterno (HorariosDatos.NombreCorto).</summary>
        public string ProfesorCorto;
        public string NumeroEmpleado;
        public int GrupoId;
        public string Grupo;
        public int MateriaId;
        public string Materia;
        public string Codigo;
        public DateTime VigenteDesde;
        public DateTime VigenteHasta;
        public bool Activa;

        /// <summary>
        /// Termina antes de empezar: empezo y se dio de baja el mismo dia, con algo ya
        /// registrado (CicloPreliminarDatos.DarDeBajaElHorarioActual). Ningun dia es
        /// suyo; lo de ese dia cuenta como lo de una clase que termino la vispera
        /// (AsistenciaDatos.OtraSeQuedaElDia).
        /// </summary>
        public bool SinDiasPropios
        {
            get { return VigenteHasta.Date < VigenteDesde.Date; }
        }

        public int Sesiones;
        /// <summary>
        /// Todas sus sesiones, con y sin bitacoras (VigenciaFranjas.SesionDeLaClase:
        /// enlazadas a sus franjas o a sus extras, o sin enlace de su mismo
        /// profesor, grupo y materia dentro de su vigencia), y la primera y la
        /// ultima fecha. Con alguna, la clase no se desactiva: se termina
        /// (HorariosDatos.TerminarClase), y su vigencia no puede dejarlas fuera.
        /// </summary>
        public int SesionesTotales;
        public DateTime? PrimeraSesion;
        public DateTime? UltimaSesion;

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
        /// <summary>"Marco Llinas": lo que cabe en un cuadro de la cuadricula.</summary>
        public string ProfesorCorto;
        public int GrupoId;
        public string Grupo;
        public string Materia;
        /// <summary>
        /// Los dias en que se da: la vigencia de su clase recortada a la propia de
        /// la franja (db/20, VigenciaFranjas). Sin la 20, la de su clase.
        /// </summary>
        public DateTime VigenteDesde;
        public DateTime VigenteHasta;
        /// <summary>Su clase esta activa (no dice si es vigente).</summary>
        public bool Activa;
        /// <summary>La vigencia propia de la franja tal como esta guardada: null = la de su clase (siempre null sin db/20).</summary>
        public DateTime? FranjaDesde;
        public DateTime? FranjaHasta;
        /// <summary>La vigencia de su clase.</summary>
        public DateTime ClaseDesde;
        public DateTime ClaseHasta;
    }

    /// <summary>
    /// Lo que ya paso con una franja (HorariosDatos.HistoriaDeFranja): con
    /// historia, cambiarla o quitarla en su sitio reescribiria lo pasado, y con
    /// los cambios desde una fecha encendidos (ConFecha) se parte en su lugar.
    /// </summary>
    public class HistoriaFranja
    {
        /// <summary>Tiene sesiones, alguna cancelacion o semanas pasadas en que se daba (Faltas ya las juzgo).</summary>
        public bool TieneHistoria;
        /// <summary>Sesiones enlazadas a esta franja.</summary>
        public int Sesiones;
        /// <summary>Fechas ya pasadas de su dia de la semana dentro de su vigencia: las que cambiaria editarla en su sitio.</summary>
        public int SemanasPasadas;
        /// <summary>Su sesion de hoy tiene bitacoras o, con db/22, asistencias que dio su profesor (tambien anuladas).</summary>
        public bool SesionHoyConBitacoras;
        /// <summary>Su sesion de hoy tiene asistencias que dio su profesor (db/22): los avisos lo dicen.</summary>
        public bool SesionHoyConDadas;
        /// <summary>Sus cancelaciones de hoy en adelante.</summary>
        public int CancelacionesFuturas;
        /// <summary>Desde cuando puede aplicar un cambio: hoy, o manana si hoy ya hubo sesion con bitacoras.</summary>
        public DateTime PrimerDiaPosible;
        /// <summary>VigenciaFranjas.ConFecha: db/20 aplicada y encendido en Ajustes.</summary>
        public bool ConFecha;
    }

    /// <summary>Un equipo que sus companeros avisaron sin internet en una sesion.</summary>
    public class EquipoSinInternet
    {
        /// <summary>Como lo guarda la bitacora: "12", "PC-12".</summary>
        public string Equipo;
        /// <summary>Cuantos alumnos (bitacoras) lo avisaron.</summary>
        public int Alumnos;
    }

    /// <summary>Lo que necesita ColoresProfesor para repartir los colores (HorariosDatos.DatosDeColores).</summary>
    public class RepartoDeColores
    {
        /// <summary>Los profesores con alguna clase, en el orden de su primera clase.</summary>
        public List<int> Orden = new List<int>();
        /// <summary>'#RRGGBB' por id de profesor, los que tienen uno elegido (db/12).</summary>
        public Dictionary<int, string> Elegidos = new Dictionary<int, string>();
    }

    /// <summary>Un grupo o una materia con su carrera: para no ofrecer las materias de todas las carreras.</summary>
    public class OpcionDeCarrera : OpcionCombo
    {
        public int? CarreraId;
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
        public int ProfesorId;
        public string Profesor;
        /// <summary>"Marco Llinas", como en las franjas: lo que cabe en un cuadro de la cuadricula.</summary>
        public string ProfesorCorto;
        public string Grupo;
        public string Materia;
        /// <summary>Cuando se creo.</summary>
        public DateTime? CreadaEn;
        /// <summary>De una extra: alumnos que ya registraron bitacora en su sesion (con ellos no se recorta ni se quita).</summary>
        public int Bitacoras;
        /// <summary>
        /// De una extra: asistencias que dio su profesor en su sesion (db/22; tambien
        /// las anuladas, son historia). Con alguna tampoco se recorta ni se quita.
        /// </summary>
        public int Dadas;

        public bool EsExtra
        {
            get { return Tipo == "extra"; }
        }
    }

    /// <summary>Un tramo de horas de un dia: "09:00-10:00".</summary>
    public class TramoHorario
    {
        public TimeSpan Inicio;
        public TimeSpan Fin;

        public TramoHorario(TimeSpan inicio, TimeSpan fin)
        {
            Inicio = inicio;
            Fin = fin;
        }

        public override string ToString()
        {
            return HorariosDatos.Hora(Inicio) + "-" + HorariosDatos.Hora(Fin);
        }
    }

    /// <summary>Lo que hizo HorariosDatos.AplicarExcepcion, para el resumen del administrador.</summary>
    public class ResultadoExcepcion
    {
        /// <summary>Una linea por cosa hecha: la extra que entra y cada clase cancelada o recortada.</summary>
        public List<string> Lineas = new List<string>();
        public bool ExtraCreada;
        public int Canceladas;
        public int Recortadas;
    }

    /// <summary>Lo que hizo HorariosDatos.NoTomarEnCuenta.</summary>
    public class ResultadoNoTomar
    {
        /// <summary>Las clases que se cancelaron esos dias: ya no cuentan como faltas.</summary>
        public int Canceladas;
        /// <summary>
        /// Las de la vista previa que al aceptar ya no estaban en rojo (alguien
        /// registro, otra persona las cancelo): no se tocaron.
        /// </summary>
        public int YaNoEnRojo;
    }

    /// <summary>
    /// Una vez que se uso "No tomar en cuenta" (Faltas): sus cancelaciones tienen
    /// el mismo motivo (con HorariosDatos.MarcaNoTomarEnCuenta al final), quien y
    /// el mismo creada_en. Es lo unico que VolverAContar quita.
    /// </summary>
    public class LoteSinContar
    {
        public DateTime CreadaEn;
        public int? CreadaPor;
        /// <summary>Tal como esta guardado, con la marca: parte de la clave.</summary>
        public string MotivoGuardado = "";
        /// <summary>El que se escribio (sin la marca).</summary>
        public string Motivo = "";
        /// <summary>El nombre de quien lo hizo, o "".</summary>
        public string Quien = "";
        /// <summary>Cuantas de sus cancelaciones siguen puestas.</summary>
        public int Clases;
        public DateTime Desde;
        public DateTime Hasta;
        public int Laboratorios;
        /// <summary>El nombre del laboratorio (el de nombre menor si son varios).</summary>
        public string Laboratorio = "";
    }

    /// <summary>Como quedo una clase en el Calendario de bitacoras.</summary>
    public enum EstadoEnCalendario
    {
        /// <summary>Todavia no termina, o es de un dia que no ha llegado.</summary>
        PorVenir,
        /// <summary>Cancelada ese dia.</summary>
        Cancelada,
        /// <summary>
        /// No cuenta: el laboratorio aun no registraba con DigLab, el profesor
        /// estaba dado de baja, la clase no estaba vigente, o es una extra puesta
        /// despues de su hora (nadie pudo usarla).
        /// </summary>
        NoContaba,
        /// <summary>Con una bitacora o mas.</summary>
        ConBitacoras,
        /// <summary>Nadie abrio su sesion: ningun alumno escribio el codigo (o el profesor no la abrio).</summary>
        SinSesion,
        /// <summary>Su sesion se abrio, pero nadie registro bitacora.</summary>
        SesionVacia,
        /// <summary>
        /// Nadie registro bitacora, pero su profesor dio la asistencia a quien no
        /// pudo registrar (db/22): la clase si se dio. En ambar, no en rojo: no es
        /// una falta (Falta), no da alerta y no se marca como no dada.
        /// </summary>
        SinBitacorasConAsistencia
    }

    /// <summary>Una clase que tocaba en una fecha, para el Calendario de bitacoras (HorariosDatos.LeerCalendario).</summary>
    public class ClaseDelCalendario
    {
        public DateTime Fecha;
        public int LaboratorioId;
        public string Laboratorio;
        public bool EsExtra;
        public int? FranjaId;
        public int? ExcepcionId;
        public int ClaseId;
        public string Codigo;
        public int ProfesorId;
        public string Profesor;
        public string ProfesorCorto;
        public string Grupo;
        public string Materia;
        /// <summary>Para reconocer la sesion de la clase de antes de su misma terna (EmparejarSesiones). Interno: no va en los volcados.</summary>
        internal int MateriaId;
        internal int GrupoId;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        /// <summary>El de la cancelacion, o el de la extra.</summary>
        public string Motivo = "";
        public bool Cancelada;
        /// <summary>Se podia dar: profesor sin dar de baja ese dia; en una extra, clase activa y vigente.</summary>
        public bool Utilizable;
        /// <summary>De una extra: cuando se puso.</summary>
        public DateTime? CreadaEn;
        /// <summary>
        /// Extra que es lo que le quedo a una clase recortada (AplicarExcepcion):
        /// cae dentro de su franja cancelada ese dia. Se puso al recortar, a
        /// veces con la clase ya empezada: no es "puesta despues de su hora".
        /// </summary>
        public bool EsResto;
        public int? SesionId;
        public int Bitacoras;
        /// <summary>Su sesion no estaba enlazada: se encontro por laboratorio, dia, profesor y horas.</summary>
        public bool SesionSinEnlace;
        /// <summary>
        /// Alumnos de esta clase que quedaron en la sesion de OtraClase; se le
        /// cuentan solo si la suya salio vacia. O llegaron en los 15 minutos antes
        /// y entraron en la de la clase anterior del mismo profesor, ya empezada
        /// (codigo fijo: gana la que ya empezo), o registraron en la de su franja
        /// antes de que se recortara (lo que le queda es una extra de su clase).
        /// </summary>
        public int BitacorasEnOtra;
        public ClaseDelCalendario OtraClase;
        /// <summary>Las asistencias que dio su profesor en su sesion (db/22, solo las que cuentan); 0 sin sesion o sin la 22.</summary>
        public int AsistenciasDadas;
        public EstadoEnCalendario Estado;

        /// <summary>En rojo: ya paso, contaba y no tiene bitacoras.</summary>
        public bool Falta
        {
            get { return Estado == EstadoEnCalendario.SinSesion || Estado == EstadoEnCalendario.SesionVacia; }
        }

        /// <summary>
        /// Ya paso, contaba y nadie registro bitacora en su sesion: en rojo (Falta)
        /// o, con asistencias que dio su profesor, en ambar. Las dos pueden contar
        /// con las bitacoras que quedaron en otra sesion (un recorte, los que
        /// llegaron antes) y pasar a ConBitacoras.
        /// </summary>
        internal bool SinBitacorasPropias
        {
            get { return Falta || Estado == EstadoEnCalendario.SinBitacorasConAsistencia; }
        }
    }

    /// <summary>Una sesion de esas fechas que no es de ninguna clase que tocara (franja cambiada, sesion del sistema anterior...).</summary>
    public class SesionSuelta
    {
        public int Id;
        public DateTime Fecha;
        public int LaboratorioId;
        public TimeSpan? Entrada;
        public TimeSpan? Salida;
        public int ProfesorId;
        public string Profesor;
        public int Bitacoras;
        /// <summary>Las asistencias que dio su docente en esa sesion (db/22).</summary>
        public int Dadas;
    }

    /// <summary>Lo que lee HorariosDatos.LeerCalendario.</summary>
    public class CalendarioBitacoras
    {
        /// <summary>Reloj del servidor al leer.</summary>
        public DateTime Ahora;
        public DateTime Desde;
        public DateTime Hasta;
        public List<ClaseDelCalendario> Clases = new List<ClaseDelCalendario>();
        public List<SesionSuelta> Otras = new List<SesionSuelta>();
        /// <summary>
        /// Por laboratorio, el dia de su primera sesion de una clase del horario: desde
        /// ahi registra con DigLab. Los que no estan todavia no han registrado nada,
        /// y sus clases no cuentan como faltantes.
        /// </summary>
        public Dictionary<int, DateTime> EmpezoEl = new Dictionary<int, DateTime>();
        /// <summary>Nombre de cada laboratorio por id.</summary>
        public Dictionary<int, string> Laboratorios = new Dictionary<int, string>();
        /// <summary>
        /// Los que hoy no entran con el codigo fijo (db/11): ahi el codigo (o la
        /// lista) dice la clase, y no hay alumnos que caigan en la sesion de la
        /// clase anterior del mismo profesor (HorariosDatos.ParesSeguidos).
        /// </summary>
        public HashSet<int> LaboratoriosSinCodigoFijo = new HashSet<int>();
        /// <summary>
        /// Contar tambien los laboratorios que aun no registran con DigLab (sin
        /// EmpezoEl) y lo de antes de que empezaran: sus clases que ya pasaron sin
        /// bitacoras salen en rojo en vez de "no cuenta". Sirve para ver el horario
        /// de un laboratorio que todavia registra en el sistema anterior, sabiendo
        /// que ese rojo no quiere decir que no se dio la clase.
        /// </summary>
        public bool ContarTodos;
        /// <summary>La base tiene las asistencias que da el profesor (db/22) y esta cuenta las ve.</summary>
        public bool ConDadas;
        /// <summary>Cuando estuvo deshabilitado cada laboratorio: esas clases no se podian dar con DigLab y no cuentan.</summary>
        public List<PeriodoDeshabilitado> PeriodosDeshabilitados = new List<PeriodoDeshabilitado>();
    }

    /// <summary>Una sesion extra que es lo que le quedo a una clase recortada (HorariosDatos.AplicarExcepcion).</summary>
    public class RestoDeClase
    {
        public int Id;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        /// <summary>Alumnos que ya registraron bitacora en ella.</summary>
        public int Bitacoras;
        /// <summary>Asistencias que dio su profesor en ella (db/22; tambien las anuladas): con alguna, como con bitacoras.</summary>
        public int Dadas;

        public override string ToString()
        {
            return HorariosDatos.Hora(Inicio) + "-" + HorariosDatos.Hora(Fin);
        }
    }

    /// <summary>Una entrada de la agenda efectiva de un laboratorio en un dia.</summary>
    public class SesionAgenda
    {
        public bool EsExtra;
        public int? FranjaId;
        public int? ExcepcionId;
        public int ClaseId;
        /// <summary>El codigo fijo de la clase (clases.codigo).</summary>
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
        public int MateriaId;
        public int GrupoId;
        /// <summary>
        /// La clase esta activa y vigente y su profesor no esta dado de baja: el
        /// servidor la deja abrir. La vista del administrador tambien ensena las
        /// sesiones extra que no (con false); la del alumno nunca las trae.
        /// </summary>
        public bool Utilizable = true;

        /// <summary>De una extra: cuando se puso (para saber si se puso despues de su hora).</summary>
        public DateTime? CreadaEn;

        // Solo los rellenan PanoramaDelDia y SituacionDelLaboratorio: la sesion
        // (codigos_accesos) de esta entrada, si ya existe, su codigo (el que se
        // dicta con la generacion de codigos) y cuantos registraron en ella.
        public int? SesionId;
        public string CodigoSesion;
        public int Alumnos;
        public int ConFallas;
        /// <summary>Equipos que los alumnos de esta sesion avisaron sin internet (db/10).</summary>
        public List<EquipoSinInternet> SinInternet = new List<EquipoSinInternet>();
        /// <summary>Solo en el panel del profesor: las fallas que el reporto en esta sesion (db/15).</summary>
        public int ReportesProfesor;
        /// <summary>
        /// Las asistencias que dio su profesor en esta sesion a quien no pudo
        /// registrar (db/22, solo las que cuentan). Solo en el panorama de la
        /// administracion (PanoramaDelDia) y en el del profesor (hoy y Ayer); nunca
        /// en las pantallas del alumno.
        /// </summary>
        public int AsistenciasDadas;
        /// <summary>
        /// Con "Los docentes no ven los codigos" (CodigosSoloResponsables), solo en
        /// el panel en vivo de la administracion y de la persona responsable del
        /// laboratorio (PanoramaDelDia con codigosParaDar): el codigo que se le da
        /// al grupo, el de su sesion o, si aun no tiene, el fijo o el generado.
        /// Null sin la opcion, sin codigo (sin codigos, cancelada o que no se
        /// puede usar) o sin secreto.
        /// </summary>
        public string CodigoParaDar;

        // Solo en el panel del profesor (ayer y hoy), con las correcciones de
        // Faltas (HorariosDatos.CorregirComoFaltas): lo que le queda a una clase
        // recortada (EsResto) cuenta con las bitacoras de su franja, y una clase
        // seguida con codigo fijo con los que llegaron antes y quedaron en la
        // anterior (BitacorasEnOtra, en la sesion de EnOtra); NoContaba, que no se
        // juzga (el laboratorio aun no registraba con DigLab, o una extra puesta
        // despues de su hora).
        public bool EsResto;
        public int BitacorasEnOtra;
        public SesionAgenda EnOtra;
        public bool NoContaba;
        /// <summary>
        /// BitacorasEnOtra y EnOtra son los de la sesion de la clase de antes de su
        /// misma terna, recortada por "Ahora mismo" (HorariosDatos.AnotarSesiones
        /// con deClasesTerminadas). No cambia SesionId: la clase sigue sin sesion
        /// propia, y con la generacion de codigos su docente la abre con la suya.
        /// </summary>
        public bool EnLaDeAntes;

        /// <summary>Cuantas bitacoras tiene esta clase: las de su sesion o, si salio vacia, las que quedaron en otra.</summary>
        public int BitacorasDeLaClase
        {
            get { return Alumnos > 0 ? Alumnos : BitacorasEnOtra; }
        }

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
        /// <summary>Para ColoresProfesor: el mismo color que en Horarios.</summary>
        public List<int> OrdenDeProfesores = new List<int>();
        /// <summary>Para ColoresProfesor: los colores elegidos a mano (db/12), por profesor.</summary>
        public Dictionary<int, string> ColoresElegidos = new Dictionary<int, string>();
        /// <summary>El modo de acceso de cada laboratorio (db/11); los que falten, Fijo.</summary>
        public Dictionary<int, ModoCodigo> Modos = new Dictionary<int, ModoCodigo>();
        /// <summary>False sin db/11: entonces no se puede cambiar el modo.</summary>
        public bool ConModos;
        /// <summary>La forma general guardada de "Codigos de las clases" (db/13), o null.</summary>
        public ModoCodigo? GeneralGuardada;
        /// <summary>"Los docentes no ven los codigos" (CodigosSoloResponsables), leido con el resto del dia.</summary>
        public bool CodigosSoloResponsables;
        /// <summary>
        /// Si la base tiene las asistencias que da el profesor (db/22) y esta cuenta
        /// las ve; null si no se pregunto (ninguna clase del dia tenia sesion).
        /// PanoramaDeProfesor lo reutiliza: sin la 22, cada pregunta va a
        /// information_schema.
        /// </summary>
        internal bool? ConDadas;

        public ModoCodigo ModoDe(int idLaboratorio)
        {
            return ModosDeAcceso.De(Modos, idLaboratorio);
        }
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
        /// <summary>Sus clases de FechaAyer, con cuantos registraron (la primera de las tres columnas del panel).</summary>
        public List<SesionAgenda> Ayer = new List<SesionAgenda>();
        /// <summary>Sus clases de FechaManana (agenda efectiva; aun sin sesiones ni conteos).</summary>
        public List<SesionAgenda> Manana = new List<SesionAgenda>();
        /// <summary>
        /// El dia de la primera columna (Ayer): el ultimo antes de hoy, hasta una
        /// semana atras, en que tuvo algo en su agenda (clases, canceladas o
        /// extras). Si en esa semana no tuvo nada, ayer. Siempre antes de hoy.
        /// </summary>
        public DateTime FechaAyer;
        /// <summary>
        /// El dia de la tercera columna (Manana): el siguiente despues de hoy,
        /// hasta una semana, en que tiene algo en su agenda; si no hay ninguno,
        /// manana. Siempre despues de hoy.
        /// </summary>
        public DateTime FechaManana;
        /// <summary>Nombre de cada laboratorio por id.</summary>
        public Dictionary<int, string> Laboratorios = new Dictionary<int, string>();
        /// <summary>Su horario semanal de ahora en adelante (HorariosDatos.ObtenerFranjasDeProfesor), para decir cual es su proxima clase otro dia.</summary>
        public List<FranjaInfo> Franjas = new List<FranjaInfo>();
        /// <summary>Tiene horario, pero todo en laboratorios deshabilitados: hoy no hay codigo ni registros.</summary>
        public bool SoloDeshabilitados;
        /// <summary>El modo de acceso de cada laboratorio (db/11); los que falten, Fijo.</summary>
        public Dictionary<int, ModoCodigo> Modos = new Dictionary<int, ModoCodigo>();
        /// <summary>Por que no se pudo abrir alguna de sus clases (generacion de codigos), o null.</summary>
        public string AvisoApertura;
        /// <summary>
        /// "Los docentes no ven los codigos" (CodigosSoloResponsables), leido con su
        /// dia: el panel no ensena ningun codigo y dice que lo pida a la persona
        /// responsable del laboratorio.
        /// </summary>
        public bool CodigosOcultos;
        /// <summary>True si la base admite las fallas que reporta el profesor (db/15): solo entonces se ofrece "Reportar una falla".</summary>
        public bool ReportesDisponibles;
        /// <summary>True si el profesor puede dar la asistencia a quien no pudo registrar (db/22, y esta cuenta ve la tabla).</summary>
        public bool ConDadas;
        /// <summary>Lo que eligio para sus clases (db/19): su circulo, su color y su nombre corto. Null sin la 19.</summary>
        internal LecturaPersonal Personal;
        /// <summary>True si Personal se leyo (con o sin la 19); con un fallo, false y el panel conserva lo de antes.</summary>
        public bool PersonalLeido;
        /// <summary>Las imagenes de sus clases que aun no estaban en memoria (las trae el panel en la misma tarea), o null.</summary>
        public Dictionary<string, byte[]> ImagenesPersonal;

        public string Laboratorio(int id)
        {
            string nombre;
            return Laboratorios.TryGetValue(id, out nombre) ? nombre : "";
        }

        public ModoCodigo ModoDe(int idLaboratorio)
        {
            return ModosDeAcceso.De(Modos, idLaboratorio);
        }

        /// <summary>El modo de los laboratorios donde da clase (el de la mayoria), o el de la escuela si no da ninguna.</summary>
        public ModoCodigo ModoComun
        {
            get
            {
                List<int> suyos = Franjas.Select(f => f.LaboratorioId).Concat(Hoy.Select(s => s.LaboratorioId)).Distinct().ToList();
                return ModosDeAcceso.Comun(suyos.Count > 0 ? suyos.Select(ModoDe) : Modos.Values);
            }
        }
    }

    /// <summary>
    /// Acceso a datos de la fase 6: clases con codigo fijo, horario semanal por
    /// laboratorio, excepciones por dia, agenda efectiva y resolucion del codigo
    /// que teclea el alumno (docs/fase6-horarios.md). Todo con el reloj del
    /// servidor MySQL.
    /// </summary>
    internal partial class HorariosDatos
    {
        public const int MargenAntesDelInicioMinutos = 15;
        // Solo para mostrar (cabeceras, avisos, menus): nada lo compara ni lo guarda.
        public static readonly string[] NombresDia = { "", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };

        private readonly Conexion mconexion = new Conexion();

        // "sesiones" cuenta solo las sesiones en las que alguien registro (o, con
        // db/22, en las que su profesor dio alguna asistencia: SinRegistros): con
        // una, DialogoClase no deja cambiar su profesor, grupo ni materia. Las
        // vacias no cuentan (con la generacion de codigos, db/11, el profesor abre
        // la suya aunque no llegue nadie): ActualizarClase les pone los datos
        // nuevos a las de hoy en adelante, para que las bitacoras que lleguen
        // despues no salgan con los viejos.
        private static string SelectClase(bool conDadas)
        {
            return @"
            SELECT c.idclases, c.usuarios_idusuarios, u.numero_identificador,
                   CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                   u.nombre AS profesor_nombre, u.apellido_paterno AS profesor_paterno,
                   c.grupos_idgrupos, g.nombre_grupo, c.materias_id_materia, m.nombre_materia,
                   c.codigo, c.vigente_desde, c.vigente_hasta, c.activa,
                   (SELECT COUNT(*) FROM codigos_accesos ca
                      LEFT JOIN horarios h ON h.idhorarios = ca.horarios_idhorarios
                      LEFT JOIN horario_excepciones x ON x.idhorario_excepciones = ca.horario_excepciones_id
                     WHERE (h.clases_idclases = c.idclases OR x.clases_idclases = c.idclases)
                       AND " + ConRegistros(conDadas, "ca") + @") AS sesiones
            FROM clases c
            INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
            INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
            INNER JOIN materias m ON m.id_materia = c.materias_id_materia";
        }

        /// <summary>
        /// La sesion (alias.idcodigos_accesos) no tiene bitacoras ni, con db/22
        /// (conDadas = Esquema.AsistenciaDada), asistencias que dio su profesor,
        /// tampoco anuladas: son historia. Es lo que decide si una sesion se puede
        /// mover, desenlazar, recortar o quitar. Sin la 22, la condicion de siempre.
        /// </summary>
        private static string SinRegistros(bool conDadas, string alias)
        {
            return "NOT EXISTS (SELECT 1 FROM registros_bitacoras rb WHERE rb.fk_codigo_accesos = " + alias + ".idcodigos_accesos)"
                + (conDadas ? " AND NOT EXISTS (SELECT 1 FROM asistencias_dadas ad WHERE ad.fk_codigo_accesos = " + alias + ".idcodigos_accesos)" : "");
        }

        /// <summary>Lo contrario de SinRegistros: con bitacoras o (db/22) con asistencias que dio su profesor.</summary>
        private static string ConRegistros(bool conDadas, string alias)
        {
            string bitacoras = "EXISTS (SELECT 1 FROM registros_bitacoras rb WHERE rb.fk_codigo_accesos = " + alias + ".idcodigos_accesos)";
            return conDadas
                ? "(" + bitacoras + " OR EXISTS (SELECT 1 FROM asistencias_dadas ad WHERE ad.fk_codigo_accesos = " + alias + ".idcodigos_accesos))"
                : bitacoras;
        }

        /// <summary>
        /// Cuantas asistencias dio el profesor (db/22, tambien las anuladas) en la
        /// sesion de la extra alias.idhorario_excepciones, como columna "dadas"; sin
        /// la 22, 0.
        /// </summary>
        private static string DadasDeLaExtra(bool conDadas, string alias)
        {
            return conDadas
                ? "(SELECT COUNT(*) FROM codigos_accesos cd INNER JOIN asistencias_dadas ad ON ad.fk_codigo_accesos = cd.idcodigos_accesos"
                  + " WHERE cd.horario_excepciones_id = " + alias + ".idhorario_excepciones) AS dadas"
                : "0 AS dadas";
        }

        /// <summary>
        /// "ya tiene bitacoras registradas" o, si su profesor tambien dio asistencias
        /// en ella (db/22), "... o asistencias que dio su docente": lo que impide
        /// recortar o quitar una sesion extra o lo que le quedo a una clase.
        /// </summary>
        internal static string YaTieneRegistros(int dadas)
        {
            return dadas > 0 ? "ya tiene bitácoras registradas o asistencias que dio su docente" : "ya tiene bitácoras registradas";
        }

        /// <summary>
        /// Las franjas con lo de su clase. vigente_desde y vigente_hasta son los
        /// dias en que se da (VigenciaFranjas: la de su clase recortada a la suya);
        /// franja_desde y franja_hasta, los suyos tal como estan (NULL sin db/20);
        /// clase_desde y clase_hasta, los de su clase.
        /// </summary>
        private static string SelectFranja(bool conVigencia)
        {
            return @"
            SELECT h.idhorarios, h.clases_idclases, h.laboratorios_idlaboratorios, l.nombre_laboratorio,
                   h.dia_semana, h.hora_inicio, h.hora_fin,
                   c.codigo, c.usuarios_idusuarios,
                   CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                   u.nombre AS profesor_nombre, u.apellido_paterno AS profesor_paterno,
                   c.grupos_idgrupos, g.nombre_grupo, m.nombre_materia,
                   CAST(" + VigenciaFranjas.Desde(conVigencia) + @" AS DATE) AS vigente_desde,
                   CAST(" + VigenciaFranjas.Hasta(conVigencia) + @" AS DATE) AS vigente_hasta,
                   c.vigente_desde AS clase_desde, c.vigente_hasta AS clase_hasta, c.activa,
                   " + VigenciaFranjas.Columnas(conVigencia) + @"
            FROM horarios h
            INNER JOIN clases c        ON c.idclases = h.clases_idclases
            INNER JOIN usuarios u      ON u.idusuarios = c.usuarios_idusuarios
            INNER JOIN grupos g        ON g.idgrupos = c.grupos_idgrupos
            INNER JOIN materias m      ON m.id_materia = c.materias_id_materia
            INNER JOIN laboratorios l  ON l.idlaboratorios = h.laboratorios_idlaboratorios";
        }

        // ---------------------------------------------------------------------
        // Clases
        // ---------------------------------------------------------------------

        public List<ClaseInfo> ObtenerClases(bool soloActivas)
        {
            return LeerClases((soloActivas ? " WHERE c.activa = 1" : "")
                + " ORDER BY c.activa DESC, m.nombre_materia, g.nombre_grupo", null);
        }

        public List<ClaseInfo> ObtenerClasesDeProfesor(int idProfesor)
        {
            return LeerClases(" WHERE c.usuarios_idusuarios = @Profesor ORDER BY c.activa DESC, m.nombre_materia, g.nombre_grupo",
                cmd => cmd.Parameters.AddWithValue("@Profesor", idProfesor));
        }

        public ClaseInfo ObtenerClase(int idClase)
        {
            return LeerClases(" WHERE c.idclases = @Id", cmd => cmd.Parameters.AddWithValue("@Id", idClase)).FirstOrDefault();
        }

        /// <param name="resto">Lo que va despues de SelectClase: WHERE y ORDER BY.</param>
        private List<ClaseInfo> LeerClases(string resto, Action<MySqlCommand> parametros)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                List<ClaseInfo> lista = LeerClasesEn(conexion, null, resto, parametros);
                AnotarSesionesDeClases(conexion, lista);
                return lista;
            }
        }

        /// <summary>
        /// Una clase en una conexion (y transaccion) que ya se tiene, sin
        /// SesionesTotales: para las guardas, y como era antes de un cambio para
        /// AnotarClase (tambien desde otra clase de datos, en su transaccion).
        /// </summary>
        internal static ClaseInfo LeerClaseEn(MySqlConnection conexion, MySqlTransaction transaccion, int idClase)
        {
            return LeerClasesEn(conexion, transaccion, " WHERE c.idclases = @Id",
                cmd => cmd.Parameters.AddWithValue("@Id", idClase)).FirstOrDefault();
        }

        /// <param name="resto">Lo que va despues de SelectClase: WHERE y ORDER BY.</param>
        private static List<ClaseInfo> LeerClasesEn(MySqlConnection conexion, MySqlTransaction transaccion, string resto, Action<MySqlCommand> parametros)
        {
            List<ClaseInfo> lista = new List<ClaseInfo>();
            string consulta = SelectClase(Esquema.AsistenciaDada(conexion, transaccion)) + resto;
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion, transaccion))
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
                            ProfesorCorto = NombreCorto(Texto(reader, "profesor_nombre"), Texto(reader, "profesor_paterno")),
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
        /// A cada clase, todas sus sesiones (ClaseInfo.SesionesTotales) y la primera
        /// y la ultima fecha. Son las de VigenciaFranjas.SesionDeLaClase, pero de
        /// todas las clases a la vez y por partes que usan los indices (enlazadas a
        /// una de sus franjas, a una de sus extras, o sin enlace de su terna): con
        /// el OR de SesionDeLaClase, MariaDB 5.5 recorreria codigos_accesos entera
        /// por cada clase. La parte de las extras no cuenta otra vez una sesion que
        /// ya conto la de las franjas. El dia de cada sesion, con
        /// VigenciaFranjas.DiaDeSesion (las de antes de los horarios, sin fecha).
        /// </summary>
        private static void AnotarSesionesDeClases(MySqlConnection conexion, List<ClaseInfo> clases)
        {
            if (clases.Count == 0)
            {
                return;
            }

            string ids = string.Join(",", clases.Select(c => c.Id.ToString(CultureInfo.InvariantCulture)).Distinct());
            string dia = VigenciaFranjas.DiaDeSesion();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT t.clase, SUM(t.n) AS n, MIN(t.primera) AS primera, MAX(t.ultima) AS ultima
                FROM (SELECT hh.clases_idclases AS clase, COUNT(*) AS n, MIN(" + dia + @") AS primera, MAX(" + dia + @") AS ultima
                      FROM horarios hh
                      INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios
                      WHERE hh.clases_idclases IN (" + ids + @")
                      GROUP BY hh.clases_idclases
                      UNION ALL
                      SELECT xx.clases_idclases, COUNT(*), MIN(" + dia + @"), MAX(" + dia + @")
                      FROM horario_excepciones xx
                      INNER JOIN codigos_accesos s ON s.horario_excepciones_id = xx.idhorario_excepciones
                      WHERE xx.clases_idclases IN (" + ids + @")
                        AND NOT EXISTS (SELECT 1 FROM horarios h3 WHERE h3.idhorarios = s.horarios_idhorarios AND h3.clases_idclases = xx.clases_idclases)
                      GROUP BY xx.clases_idclases
                      UNION ALL
                      SELECT c2.idclases, COUNT(*), MIN(" + dia + @"), MAX(" + dia + @")
                      FROM clases c2
                      INNER JOIN codigos_accesos s
                              ON s.usuarios_idusuarios = c2.usuarios_idusuarios AND s.materias_id_materia = c2.materias_id_materia
                             AND s.grupos_idgrupos = c2.grupos_idgrupos AND " + dia + @" BETWEEN c2.vigente_desde AND c2.vigente_hasta
                             AND s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
                      WHERE c2.idclases IN (" + ids + @")
                      GROUP BY c2.idclases) t
                GROUP BY t.clase", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                Dictionary<int, List<ClaseInfo>> porId = clases.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.ToList());
                while (reader.Read())
                {
                    List<ClaseInfo> suyas;
                    if (reader.IsDBNull(0) || !porId.TryGetValue(Convert.ToInt32(reader.GetValue(0)), out suyas))
                    {
                        continue;
                    }

                    foreach (ClaseInfo clase in suyas)
                    {
                        clase.SesionesTotales = Convert.ToInt32(reader.GetValue(1));
                        clase.PrimeraSesion = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2).Date;
                        clase.UltimaSesion = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3).Date;
                    }
                }
            }
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

            throw new InvalidOperationException("Todos los códigos que se probaron ya estaban en uso. Pulsa «Proponer otro» o escribe uno a mano.");
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
            Permisos.Exigir("Crear una clase");
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    codigo = CodigoParaClase(conexion, transaccion, idProfesor, codigo);
                    ValidarClase(codigo, desde, hasta);
                    ExigirSinCruceDeTerna(conexion, transaccion, 0, idProfesor, idGrupo, idMateria, desde, hasta);
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

                    if (HistorialCambios.Activo(conexion, transaccion))
                    {
                        AnotarClase(conexion, transaccion, AccionDeCambio.Alta, id, null, null);
                    }

                    transaccion.Commit();
                    Log.Info("Clase creada: id " + id + ", codigo " + codigo + ".");
                    return id;
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new InvalidOperationException("El código «" + codigo + "» ya está en uso en otra clase: elige otro.");
            }
        }

        /// <summary>
        /// Edita una clase sin reescribir su historia (en la capa de datos, no solo
        /// en DialogoClase): con sesiones de dias pasados o con bitacoras no
        /// cambia de profesor, grupo ni materia (Faltas y la asistencia las
        /// pondrian a nombre de otros), y su vigencia no puede dejar fuera
        /// ninguna de sus sesiones. Una clase activa tampoco puede cruzar sus
        /// fechas con otra activa del mismo profesor, grupo y materia.
        /// InvalidOperationException con el motivo.
        /// </summary>
        public void ActualizarClase(int idClase, int idProfesor, int idGrupo, int idMateria, string codigo, DateTime desde, DateTime hasta)
        {
            Permisos.Exigir("Editar una clase");
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    ClaseInfo actual = LeerClaseEn(conexion, transaccion, idClase);
                    if (actual == null)
                    {
                        throw new InvalidOperationException("Esa clase ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.");
                    }

                    // Si cambia de profesor, pasa a tener el codigo del nuevo.
                    codigo = CodigoParaClase(conexion, transaccion, idProfesor, codigo);
                    ValidarClase(codigo, desde, hasta);
                    bool otraTerna = actual.ProfesorId != idProfesor || actual.GrupoId != idGrupo || actual.MateriaId != idMateria;
                    if (otraTerna && TieneHistoriaDeClase(conexion, transaccion, idClase))
                    {
                        throw new InvalidOperationException("La clase ya tiene sesiones: no se le puede cambiar quién la imparte, el grupo ni la materia "
                            + "(lo que ya pasó quedaría con los datos nuevos). Termínala y crea otra clase con los datos nuevos.");
                    }

                    SesionesDeClase sesiones = VigenciaFranjas.Sesiones(conexion, idClase, transaccion);
                    if (sesiones.Primera.HasValue && desde.Date > sesiones.Primera.Value)
                    {
                        throw new InvalidOperationException("La clase tiene sesiones desde el " + sesiones.Primera.Value.ToString("dd/MM/yyyy")
                            + ": su vigencia no puede empezar después, dejaría fuera esas sesiones.");
                    }

                    // Si no se cambia su ultimo dia no deja fuera nada nuevo: una clase que
                    // "Ahora mismo" termino ayer tiene sesiones de hoy y se tiene que poder editar.
                    if (sesiones.Ultima.HasValue && hasta.Date < sesiones.Ultima.Value && hasta.Date != actual.VigenteHasta.Date)
                    {
                        throw new InvalidOperationException("La clase tiene sesiones hasta el " + sesiones.Ultima.Value.ToString("dd/MM/yyyy")
                            + ": su vigencia no puede terminar antes, dejaría fuera esas sesiones.");
                    }

                    if (actual.Activa)
                    {
                        ExigirSinCruceDeTerna(conexion, transaccion, idClase, idProfesor, idGrupo, idMateria, desde, hasta);
                    }

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

                    // Las sesiones de hoy en adelante en las que aun no registro nadie
                    // (con la generacion de codigos el profesor la abre aunque no
                    // llegue nadie) toman el profesor, el grupo y la materia nuevos: si
                    // no, las bitacoras que lleguen despues saldrian con los viejos. Su
                    // codigo no se toca. Con otra terna solo se llega aqui si la clase
                    // no tiene sesiones de dias pasados ni con bitacoras (arriba). Una
                    // con asistencias que dio su profesor (db/22) tampoco cambia.
                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE codigos_accesos ca
                        LEFT JOIN horarios h ON h.idhorarios = ca.horarios_idhorarios
                        LEFT JOIN horario_excepciones x ON x.idhorario_excepciones = ca.horario_excepciones_id
                           SET ca.usuarios_idusuarios = @Profesor, ca.grupos_idgrupos = @Grupo, ca.materias_id_materia = @Materia
                         WHERE (h.clases_idclases = @Id OR x.clases_idclases = @Id)
                           AND ca.fecha >= CURDATE()
                           AND " + SinRegistros(Esquema.AsistenciaDada(conexion, transaccion), "ca"), conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", idClase);
                        comando.Parameters.AddWithValue("@Profesor", idProfesor);
                        comando.Parameters.AddWithValue("@Grupo", idGrupo);
                        comando.Parameters.AddWithValue("@Materia", idMateria);
                        comando.ExecuteNonQuery();
                    }

                    if (HistorialCambios.Activo(conexion, transaccion))
                    {
                        AnotarClase(conexion, transaccion, AccionDeCambio.Cambio, idClase, actual, null);
                    }

                    transaccion.Commit();
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new InvalidOperationException("El código «" + codigo + "» ya está en uso en otra clase: elige otro.");
            }
        }

        private static string CodigoParaClase(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor, string codigo)
        {
            return Esquema.CodigoPorProfesor(conexion, transaccion)
                ? CodigosDeProfesor.ObtenerOCrear(conexion, transaccion, idProfesor)
                : (codigo ?? "").Trim();
        }

        /// <summary>
        /// Activa o desactiva una clase. Una clase con sesiones no se desactiva:
        /// desactivada desaparece de Faltas y de la agenda tambien su pasado; se
        /// termina en una fecha (TerminarClase). Al activarla, no puede cruzar
        /// sus fechas con otra activa del mismo profesor, grupo y materia.
        /// InvalidOperationException con el motivo.
        /// </summary>
        public void CambiarEstadoClase(int idClase, bool activa)
        {
            Permisos.Exigir(activa ? "Activar una clase" : "Desactivar una clase");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ClaseInfo clase = LeerClaseEn(conexion, transaccion, idClase);
                if (!activa)
                {
                    if (VigenciaFranjas.Sesiones(conexion, idClase, transaccion).Cuantas > 0)
                    {
                        throw new InvalidOperationException("La clase ya tiene sesiones: termínala en una fecha en lugar de desactivarla.");
                    }
                }
                else if (clase != null && !clase.Activa)
                {
                    ExigirSinCruceDeTerna(conexion, transaccion, idClase, clase.ProfesorId, clase.GrupoId, clase.MateriaId, clase.VigenteDesde, clase.VigenteHasta);
                }

                using (MySqlCommand comando = new MySqlCommand("UPDATE clases SET activa = @Activa WHERE idclases = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Activa", activa ? 1 : 0);
                    comando.Parameters.AddWithValue("@Id", idClase);
                    comando.ExecuteNonQuery();
                }

                // Solo si de verdad cambio: activar una ya activa no es un cambio.
                if (clase != null && clase.Activa != activa && HistorialCambios.Activo(conexion, transaccion))
                {
                    AnotarClase(conexion, transaccion, activa ? AccionDeCambio.Activar : AccionDeCambio.Desactivar, idClase, clase, null);
                }

                transaccion.Commit();
            }
        }

        /// <summary>
        /// Termina una clase en una fecha (por omision, hoy): su vigencia acaba ese
        /// dia y lo que ya paso queda como fue (Faltas, la asistencia, la agenda
        /// de esos dias). Es lo que se hace con una clase con sesiones en vez de
        /// desactivarla. No puede terminar antes de empezar ni antes de su ultima
        /// sesion, y no se alarga (eso es editar sus fechas): con la fecha en que
        /// ya termina no hace nada. InvalidOperationException con el motivo.
        /// </summary>
        public void TerminarClase(int idClase, DateTime hasta)
        {
            Permisos.Exigir("Terminar una clase");
            DateTime fin = hasta.Date;
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ClaseInfo clase = LeerClaseEn(conexion, transaccion, idClase);
                if (clase == null)
                {
                    throw new InvalidOperationException("Esa clase ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.");
                }

                if (fin == clase.VigenteHasta.Date)
                {
                    return;
                }

                if (fin > clase.VigenteHasta.Date)
                {
                    throw new InvalidOperationException("La clase ya termina el " + clase.VigenteHasta.ToString("dd/MM/yyyy")
                        + ". Para alargarla, edita sus fechas.");
                }

                if (fin < clase.VigenteDesde.Date)
                {
                    throw new InvalidOperationException("La clase empieza el " + clase.VigenteDesde.ToString("dd/MM/yyyy") + ": no puede terminar antes.");
                }

                SesionesDeClase sesiones = VigenciaFranjas.Sesiones(conexion, idClase, transaccion);
                if (sesiones.Ultima.HasValue && fin < sesiones.Ultima.Value)
                {
                    throw new InvalidOperationException("La clase tiene sesiones hasta el " + sesiones.Ultima.Value.ToString("dd/MM/yyyy")
                        + ": no puede terminar antes.");
                }

                using (MySqlCommand comando = new MySqlCommand("UPDATE clases SET vigente_hasta = @Hasta WHERE idclases = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Hasta", fin);
                    comando.Parameters.AddWithValue("@Id", idClase);
                    comando.ExecuteNonQuery();
                }

                if (HistorialCambios.Activo(conexion, transaccion))
                {
                    AnotarClase(conexion, transaccion, AccionDeCambio.Fin, idClase, clase,
                        ": termina el " + HistorialCambios.Fecha(fin) + " (antes, el " + HistorialCambios.Fecha(clase.VigenteHasta) + ")");
                }

                transaccion.Commit();
                Log.Info("Clase " + idClase + " terminada el " + fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + " (antes hasta el " + clase.VigenteHasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ").");
            }
        }

        /// <summary>
        /// InvalidOperationException si otra clase activa del mismo profesor, grupo
        /// y materia cruza sus fechas con desde-hasta: la asistencia (y lo que el
        /// profesor elige para sus clases, db/19) atribuye las sesiones por esa
        /// terna, y dos clases cruzadas las contarian en las dos.
        /// </summary>
        private static void ExigirSinCruceDeTerna(MySqlConnection conexion, MySqlTransaction transaccion, int excluirClaseId,
            int idProfesor, int idGrupo, int idMateria, DateTime desde, DateTime hasta)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.codigo, c.vigente_desde, c.vigente_hasta, m.nombre_materia, g.nombre_grupo
                FROM clases c
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                WHERE c.usuarios_idusuarios = @Profesor AND c.grupos_idgrupos = @Grupo AND c.materias_id_materia = @Materia
                  AND c.activa = 1 AND c.idclases <> @Excluir
                  AND c.vigente_desde <= @Hasta AND c.vigente_hasta >= @Desde
                ORDER BY c.vigente_desde
                LIMIT 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Profesor", idProfesor);
                comando.Parameters.AddWithValue("@Grupo", idGrupo);
                comando.Parameters.AddWithValue("@Materia", idMateria);
                comando.Parameters.AddWithValue("@Excluir", excluirClaseId);
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        // Sin dias propios (ClaseInfo.SinDiasPropios): lo de ese dia es suyo y no se le puede quitar.
                        if (reader.GetDateTime("vigente_hasta").Date < reader.GetDateTime("vigente_desde").Date)
                        {
                            throw new InvalidOperationException("Ya hay otra clase de " + Texto(reader, "nombre_materia") + " - " + Texto(reader, "nombre_grupo")
                                + " que imparte la misma persona, dada de baja el " + reader.GetDateTime("vigente_desde").ToString("dd/MM/yyyy")
                                + " con registros de ese día: esta clase no puede abarcar ese día. Para seguir con ella, crea una clase nueva que empiece ese día o después.");
                        }

                        throw new InvalidOperationException("Ya hay otra clase de " + Texto(reader, "nombre_materia") + " - " + Texto(reader, "nombre_grupo")
                            + " que imparte la misma persona, vigente del " + reader.GetDateTime("vigente_desde").ToString("dd/MM/yyyy")
                            + " al " + reader.GetDateTime("vigente_hasta").ToString("dd/MM/yyyy")
                            + ": dos clases iguales no pueden cruzar sus fechas (sus sesiones y su asistencia contarían en las dos). "
                            + "Cambia las fechas o termina antes la otra.");
                    }
                }
            }
        }

        /// <summary>
        /// La clase tiene sesiones que ya son historia (VigenciaFranjas.SesionDeLaClase):
        /// de un dia pasado (las de antes de los horarios, sin fecha, lo son), o
        /// con bitacoras. Las vacias de hoy en adelante no cuentan: ActualizarClase
        /// les pone los datos nuevos. Con db/22, tambien una asistencia que dio su
        /// profesor (aun anulada, y aunque su sesion sea de hoy): con otra terna
        /// aparecerian en la lista de otro profesor o grupo.
        /// </summary>
        private static bool TieneHistoriaDeClase(MySqlConnection conexion, MySqlTransaction transaccion, int idClase)
        {
            bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
            string sesiones = "SELECT COUNT(*) FROM clases c JOIN codigos_accesos s ON " + VigenciaFranjas.SesionDeLaClase()
                + " WHERE c.idclases = @Clase AND (" + VigenciaFranjas.DiaDeSesion() + " < CURDATE()"
                + " OR " + ConRegistros(conDadas, "s") + ")";
            using (MySqlCommand comando = new MySqlCommand(conDadas
                ? "SELECT (" + sesiones + ") + (SELECT COUNT(*) FROM asistencias_dadas ad WHERE ad.fk_clase = @Clase)"
                : sesiones, conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Clase", idClase);
                return Convert.ToInt32(comando.ExecuteScalar()) > 0;
            }
        }

        private static void ValidarClase(string codigo, DateTime desde, DateTime hasta)
        {
            if (string.IsNullOrWhiteSpace(codigo) || codigo.Trim().Length > 16)
            {
                throw new InvalidOperationException("El código tiene que tener entre 1 y 16 caracteres.");
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
            // Todas, tambien las que ya terminaron o aun no empiezan (db/20): la
            // cuadricula de Horarios ensena cualquier semana y coloca las de esa.
            return LeerFranjas(conVigencia => " WHERE h.laboratorios_idlaboratorios = @Lab ORDER BY h.dia_semana, h.hora_inicio",
                cmd => cmd.Parameters.AddWithValue("@Lab", idLaboratorio));
        }

        /// <summary>
        /// Las de todos los laboratorios, como ObtenerFranjasDeLaboratorio (tambien
        /// las que ya terminaron o aun no empiezan): Horario semanal ensena las de
        /// uno y cuenta los choques de la semana entre todas (un mismo grupo o
        /// docente en dos laboratorios a la vez), en una sola lectura.
        /// </summary>
        public List<FranjaInfo> ObtenerTodasLasFranjas()
        {
            return LeerFranjas(conVigencia => " ORDER BY h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio", cmd => { });
        }

        public List<FranjaInfo> ObtenerFranjasDeClase(int idClase)
        {
            return LeerFranjas(conVigencia => " WHERE h.clases_idclases = @Clase ORDER BY h.dia_semana, h.hora_inicio",
                cmd => cmd.Parameters.AddWithValue("@Clase", idClase));
        }

        /// <summary>
        /// El horario de un profesor de ahora en adelante: las franjas de sus
        /// clases activas que se dan hoy o despues (reloj del servidor) y, de una
        /// clase que ya termino, las de su ultimo dia (Mis grupos ensena tambien
        /// las de hace poco). Sin las franjas que ya terminaron (db/20); sin la
        /// 20, todas las de sus clases activas, como antes.
        /// </summary>
        /// <summary>Los laboratorios que hoy estan deshabilitados.</summary>
        public HashSet<int> LaboratoriosDeshabilitadosHoy()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LaboratoriosDeshabilitados.Ids(conexion);
            }
        }

        public List<FranjaInfo> ObtenerFranjasDeProfesor(int idProfesor)
        {
            return LeerFranjas(conVigencia =>
                {
                    string desde = VigenciaFranjas.Desde(conVigencia);
                    string hasta = VigenciaFranjas.Hasta(conVigencia);
                    return " WHERE c.usuarios_idusuarios = @Profesor AND c.activa = 1 AND " + desde + " <= " + hasta
                        + " AND (" + hasta + " >= CURDATE() OR (c.vigente_hasta < CURDATE() AND " + hasta + " >= c.vigente_hasta))"
                        + " ORDER BY h.dia_semana, h.hora_inicio";
                },
                cmd => cmd.Parameters.AddWithValue("@Profesor", idProfesor));
        }

        /// <param name="donde">El WHERE y el ORDER BY que van tras SelectFranja, con o sin db/20.</param>
        private List<FranjaInfo> LeerFranjas(Func<bool, string> donde, Action<MySqlCommand> parametros)
        {
            List<FranjaInfo> lista = new List<FranjaInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                using (MySqlCommand comando = new MySqlCommand(SelectFranja(conVigencia) + donde(conVigencia), conexion))
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
                                ProfesorCorto = NombreCorto(Texto(reader, "profesor_nombre"), Texto(reader, "profesor_paterno")),
                                GrupoId = reader.GetInt32("grupos_idgrupos"),
                                Grupo = reader.GetString("nombre_grupo"),
                                Materia = reader.GetString("nombre_materia"),
                                VigenteDesde = reader.GetDateTime("vigente_desde"),
                                VigenteHasta = reader.GetDateTime("vigente_hasta"),
                                Activa = reader.GetBoolean("activa"),
                                FranjaDesde = Fecha(reader, "franja_desde"),
                                FranjaHasta = Fecha(reader, "franja_hasta"),
                                ClaseDesde = reader.GetDateTime("clase_desde"),
                                ClaseHasta = reader.GetDateTime("clase_hasta")
                            });
                        }
                    }
                }
            }

            return lista;
        }

        /// <summary>Una fecha que puede venir NULL.</summary>
        private static DateTime? Fecha(MySqlDataReader reader, string columna)
        {
            int i = reader.GetOrdinal(columna);
            return reader.IsDBNull(i) ? (DateTime?)null : reader.GetDateTime(i).Date;
        }

        /// <summary>
        /// Devuelve null si la franja se puede guardar, o el conflicto en texto:
        /// mismo laboratorio, mismo profesor o mismo grupo a la misma hora, entre
        /// clases activas, algun dia en que se darian las dos (con db/20, con la
        /// vigencia de cada franja: la que termina antes de que empiece la otra
        /// no choca con ella). Con excluirFranjaId, la que se edita en su sitio,
        /// con la vigencia propia que ya tiene. La firma no cambia:
        /// deploy\probar_horarios.ps1 la busca por su nombre.
        /// </summary>
        public string ValidarFranja(int? excluirFranjaId, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                FranjaGuardada editada = excluirFranjaId.HasValue ? LeerFranjaGuardada(conexion, null, excluirFranjaId.Value) : null;
                return ValidarFranjaEn(conexion, null, excluirFranjaId, idClase, idLaboratorio, diaSemana, inicio, fin,
                    editada == null ? (DateTime?)null : editada.FranjaDesde, editada == null ? (DateTime?)null : editada.FranjaHasta);
            }
        }

        /// <summary>
        /// ValidarFranja para una franja con su propia vigencia (desde y hasta;
        /// null = la de su clase), como la que empieza a partir de una fecha
        /// (CrearFranja con desde, CambiarFranjaDesde). Sin db/20 no hay vigencia
        /// propia: lo mismo que ValidarFranja.
        /// </summary>
        public string ValidarFranjaConVigencia(int? excluirFranjaId, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin,
            DateTime? desde, DateTime? hasta)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return ValidarFranjaEn(conexion, null, excluirFranjaId, idClase, idLaboratorio, diaSemana, inicio, fin, desde, hasta);
            }
        }

        /// <summary>
        /// Tambien contra las sesiones extra ya puestas de hoy en adelante, en las
        /// fechas de su vigencia que caen en su dia (ChoqueConExtra): si no, ese
        /// dia habria dos clases a la vez, aunque ValidarExtra si lo impide al reves.
        /// </summary>
        /// <param name="excluirTambien">Otras franjas que no cuentan (las que un reemplazo va a recortar, db/25); null: ninguna.</param>
        /// <param name="cancelacionesDe">
        /// La franja cuyas cancelaciones tendra (la que se cambia desde una fecha o
        /// el trozo de antes de una partida); null: excluirFranjaId. Esos dias no
        /// se da y no choca con una sesion extra.
        /// </param>
        private static string ValidarFranjaEn(MySqlConnection conexion, MySqlTransaction transaccion, int? excluirFranjaId, int idClase,
            int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime? propiaDesde, DateTime? propiaHasta,
            IEnumerable<int> excluirTambien = null, int? cancelacionesDe = null)
        {
            // Solo numeros: van en el texto de la consulta.
            string fueraTambien = excluirTambien == null || !excluirTambien.Any() ? ""
                : " AND h.idhorarios NOT IN (" + string.Join(",", excluirTambien.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")";
            if (diaSemana < 1 || diaSemana > 7)
            {
                return "El día de la semana no es válido: elige uno de lunes a domingo.";
            }

            if (fin <= inicio)
            {
                return "La hora de fin tiene que ser posterior a la de inicio.";
            }

            ClaseInfo clase = LeerClaseEn(conexion, transaccion, idClase);
            if (clase == null)
            {
                return "Esa clase ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.";
            }

            // Los dias en que se daria: la vigencia de su clase recortada a la suya.
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            DateTime desde = clase.VigenteDesde.Date;
            DateTime hasta = clase.VigenteHasta.Date;
            if (conVigencia && propiaDesde.HasValue && propiaDesde.Value.Date > desde)
            {
                desde = propiaDesde.Value.Date;
            }

            if (conVigencia && propiaHasta.HasValue && propiaHasta.Value.Date < hasta)
            {
                hasta = propiaHasta.Value.Date;
            }

            if (desde > hasta)
            {
                return "La franja no tendría ningún día: su clase es vigente del " + clase.VigenteDesde.ToString("dd/MM/yyyy")
                    + " al " + clase.VigenteHasta.ToString("dd/MM/yyyy")
                    + (conVigencia && propiaDesde.HasValue ? " y la franja empezaría el " + propiaDesde.Value.ToString("dd/MM/yyyy") : "") + ".";
            }

            // Sin db/20, las vigencias de las clases cruzadas, como siempre. Con
            // ella, las de las franjas, y en el cruce tiene que caer su dia de la
            // semana: una franja de los lunes que termina un martes no choca con
            // otra de los lunes que empieza el jueves de esa semana.
            string cruce = "c.vigente_desde <= @Hasta AND c.vigente_hasta >= @Desde";
            if (conVigencia)
            {
                string desdeComun = "GREATEST(DATE(@Desde), " + VigenciaFranjas.Desde(true) + ")";
                string hastaComun = "LEAST(DATE(@Hasta), " + VigenciaFranjas.Hasta(true) + ")";
                cruce = "DATE(DATE_ADD(" + desdeComun + ", INTERVAL MOD(@Dia + 6 - WEEKDAY(" + desdeComun + "), 7) DAY)) <= DATE(" + hastaComun + ")";
            }

            // Con la vigencia de la otra (la de su clase recortada a la suya) para
            // decir hasta cuando choca si tiene inicio o fin propio (DescribirConflicto).
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT l.nombre_laboratorio, c.codigo, m.nombre_materia, g.nombre_grupo, h.hora_inicio, h.hora_fin,
                       (h.laboratorios_idlaboratorios = @Lab) AS mismo_lab,
                       (c.usuarios_idusuarios = @Profesor) AS mismo_profesor,
                       (c.grupos_idgrupos = @Grupo) AS mismo_grupo,
                       CAST(" + VigenciaFranjas.Desde(conVigencia) + @" AS DATE) AS vigente_desde,
                       CAST(" + VigenciaFranjas.Hasta(conVigencia) + @" AS DATE) AS vigente_hasta,
                       " + VigenciaFranjas.Columnas(conVigencia) + @"
                FROM horarios h
                INNER JOIN clases c       ON c.idclases = h.clases_idclases
                INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                WHERE h.dia_semana = @Dia
                  AND h.idhorarios <> @Excluir" + fueraTambien + @"
                  AND c.activa = 1
                  AND h.hora_inicio < @Fin AND h.hora_fin > @Inicio
                  AND " + cruce + @"
                  AND (h.laboratorios_idlaboratorios = @Lab OR c.usuarios_idusuarios = @Profesor OR c.grupos_idgrupos = @Grupo)
                ORDER BY h.hora_inicio, mismo_lab DESC, h.idhorarios
                LIMIT 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Profesor", clase.ProfesorId);
                comando.Parameters.AddWithValue("@Grupo", clase.GrupoId);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Excluir", excluirFranjaId ?? 0);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Desde", desde);
                comando.Parameters.AddWithValue("@Hasta", hasta);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return DescribirConflicto(reader, VigenciaDeLaOtra(reader));
                    }
                }
            }

            return ChoqueConExtra(conexion, transaccion, clase, idLaboratorio, diaSemana, inicio, fin, desde, hasta,
                cancelacionesDe ?? excluirFranjaId, excluirTambien);
        }

        /// <summary>
        /// ValidarFranjaEn contra las sesiones extra (tambien lo que le queda de
        /// un recorte a otra clase): las de hoy en adelante, de desde a hasta, que
        /// caen en su dia de la semana, de clases activas y vigentes ese dia, por
        /// laboratorio, profesor o grupo, como ValidarExtraEn al reves. Lo que ya
        /// paso no se puede dar dos veces. No cuentan los dias en que la franja
        /// (propias) esta cancelada, ni lo que le queda de un recorte a una de las
        /// excluidas: el reemplazo la quita o la parte (Recortar). Null si nada.
        /// </summary>
        private static string ChoqueConExtra(MySqlConnection conexion, MySqlTransaction transaccion, ClaseInfo clase, int idLaboratorio, int diaSemana,
            TimeSpan inicio, TimeSpan fin, DateTime desde, DateTime hasta, int? propias, IEnumerable<int> excluidas)
        {
            // Solo numeros: van en el texto de la consulta.
            string restosDeExcluidas = excluidas == null || !excluidas.Any() ? "" : @"
                  AND NOT EXISTS (SELECT 1 FROM horario_excepciones y
                                  INNER JOIN horarios hy ON hy.idhorarios = y.horarios_idhorarios
                                   WHERE y.tipo = 'cancelada' AND y.fecha = x.fecha
                                     AND hy.idhorarios IN (" + string.Join(",", excluidas.Select(i => i.ToString(CultureInfo.InvariantCulture))) + @")
                                     AND hy.clases_idclases = x.clases_idclases AND hy.laboratorios_idlaboratorios = x.laboratorios_idlaboratorios
                                     AND x.hora_inicio >= hy.hora_inicio AND x.hora_fin <= hy.hora_fin)";
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT l.nombre_laboratorio, c.codigo, m.nombre_materia, g.nombre_grupo, x.hora_inicio, x.hora_fin, x.fecha,
                       (x.laboratorios_idlaboratorios = @Lab) AS mismo_lab,
                       (c.usuarios_idusuarios = @Profesor) AS mismo_profesor,
                       (c.grupos_idgrupos = @Grupo) AS mismo_grupo
                FROM horario_excepciones x
                INNER JOIN clases c       ON c.idclases = x.clases_idclases
                INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                INNER JOIN laboratorios l ON l.idlaboratorios = x.laboratorios_idlaboratorios
                WHERE x.tipo = 'extra'
                  AND x.fecha BETWEEN @Desde AND @Hasta AND x.fecha >= CURDATE() AND WEEKDAY(x.fecha) + 1 = @Dia
                  AND x.hora_inicio < @Fin AND x.hora_fin > @Inicio
                  AND c.activa = 1 AND x.fecha BETWEEN c.vigente_desde AND c.vigente_hasta
                  AND (x.laboratorios_idlaboratorios = @Lab OR c.usuarios_idusuarios = @Profesor OR c.grupos_idgrupos = @Grupo)
                  AND NOT EXISTS (SELECT 1 FROM horario_excepciones z
                                   WHERE z.tipo = 'cancelada' AND z.horarios_idhorarios = @Propias AND z.fecha = x.fecha)" + restosDeExcluidas + @"
                ORDER BY x.fecha, x.hora_inicio
                LIMIT 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Profesor", clase.ProfesorId);
                comando.Parameters.AddWithValue("@Grupo", clase.GrupoId);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                comando.Parameters.AddWithValue("@Propias", propias ?? 0);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    // "... de 10:00 a 11:00 el 05/11/2026 (sesión extra) en Lab 1: mismo grupo."
                    return DescribirConflicto(reader, " el " + reader.GetDateTime("fecha").ToString("dd/MM/yyyy") + " (sesión extra)");
                }
            }
        }

        /// <param name="vigencia">Tras sus horas, desde y hasta cuando se da la otra (" (hasta el 19/10)"), o "".</param>
        private static string DescribirConflicto(MySqlDataReader reader, string vigencia = "")
        {
            List<string> motivos = new List<string>();
            if (reader.GetBoolean("mismo_lab")) motivos.Add("mismo laboratorio");
            if (reader.GetBoolean("mismo_profesor")) motivos.Add("misma persona docente");
            if (reader.GetBoolean("mismo_grupo")) motivos.Add("mismo grupo");

            return "Se solapa con " + reader.GetString("nombre_materia") + " - " + reader.GetString("nombre_grupo")
                + " (" + reader.GetString("codigo") + ") de " + Hora(reader.GetTimeSpan("hora_inicio")) + " a " + Hora(reader.GetTimeSpan("hora_fin"))
                + vigencia + " en " + reader.GetString("nombre_laboratorio") + ": " + string.Join(", ", motivos) + ".";
        }

        /// <summary>
        /// La vigencia de la franja con la que se choca, si tiene inicio o fin
        /// propio (db/20): " (desde el 20/10)", " (hasta el 19/10)" o " (del 20/10
        /// al 26/10)", con la de su clase recortada a la suya. Asi se ve desde que
        /// fecha ya no chocaria. Sin propios (o sin db/20), "": vale toda su clase.
        /// </summary>
        private static string VigenciaDeLaOtra(MySqlDataReader reader)
        {
            DateTime? propiaDesde = Fecha(reader, "franja_desde");
            DateTime? propiaHasta = Fecha(reader, "franja_hasta");
            DateTime? desde = Fecha(reader, "vigente_desde");
            DateTime? hasta = Fecha(reader, "vigente_hasta");
            if (propiaDesde.HasValue && propiaHasta.HasValue && desde.HasValue && hasta.HasValue)
            {
                return " (del " + desde.Value.ToString("dd/MM") + " al " + hasta.Value.ToString("dd/MM") + ")";
            }

            if (propiaDesde.HasValue && desde.HasValue)
            {
                return " (desde el " + desde.Value.ToString("dd/MM") + ")";
            }

            if (propiaHasta.HasValue && hasta.HasValue)
            {
                return " (hasta el " + hasta.Value.ToString("dd/MM") + ")";
            }

            return "";
        }

        // ---------------------------------------------------------------------
        // Historia de una franja (db/20): lo que ya paso no se reescribe
        // ---------------------------------------------------------------------

        /// <summary>Una franja tal como esta guardada, con la vigencia de su clase.</summary>
        private sealed class FranjaGuardada
        {
            public int Id;
            public int ClaseId;
            public int LaboratorioId;
            public int DiaSemana;
            public TimeSpan Inicio;
            public TimeSpan Fin;
            /// <summary>La suya: null = la de su clase (siempre null sin db/20).</summary>
            public DateTime? FranjaDesde;
            public DateTime? FranjaHasta;
            public DateTime ClaseDesde;
            public DateTime ClaseHasta;

            /// <summary>El primer dia en que se da (VigenciaFranjas.Desde).</summary>
            public DateTime Desde
            {
                get { return FranjaDesde.HasValue && FranjaDesde.Value > ClaseDesde ? FranjaDesde.Value : ClaseDesde; }
            }

            /// <summary>El ultimo dia en que se da (VigenciaFranjas.Hasta).</summary>
            public DateTime Hasta
            {
                get { return FranjaHasta.HasValue && FranjaHasta.Value < ClaseHasta ? FranjaHasta.Value : ClaseHasta; }
            }

            public bool Igual(int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
            {
                return ClaseId == idClase && LaboratorioId == idLaboratorio && DiaSemana == diaSemana && Inicio == inicio && Fin == fin;
            }
        }

        private static FranjaGuardada LeerFranjaGuardada(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja)
        {
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT h.idhorarios, h.clases_idclases, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin,
                       c.vigente_desde AS clase_desde, c.vigente_hasta AS clase_hasta,
                       " + VigenciaFranjas.Columnas(conVigencia) + @"
                FROM horarios h
                INNER JOIN clases c ON c.idclases = h.clases_idclases
                WHERE h.idhorarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new FranjaGuardada
                    {
                        Id = reader.GetInt32("idhorarios"),
                        ClaseId = reader.GetInt32("clases_idclases"),
                        LaboratorioId = reader.GetInt32("laboratorios_idlaboratorios"),
                        DiaSemana = reader.GetInt32("dia_semana"),
                        Inicio = reader.GetTimeSpan("hora_inicio"),
                        Fin = reader.GetTimeSpan("hora_fin"),
                        FranjaDesde = Fecha(reader, "franja_desde"),
                        FranjaHasta = Fecha(reader, "franja_hasta"),
                        ClaseDesde = reader.GetDateTime("clase_desde").Date,
                        ClaseHasta = reader.GetDateTime("clase_hasta").Date
                    };
                }
            }
        }

        /// <summary>
        /// Lo que ya paso con una franja: si tiene historia, cuantas sesiones y
        /// semanas pasadas tiene, sus cancelaciones de hoy en adelante y desde
        /// cuando puede aplicar un cambio (reloj del servidor). Con ConFecha, una
        /// franja con historia se cambia o se quita desde una fecha
        /// (CambiarFranjaDesde, QuitarFranjaDesde); sin el, en su sitio como
        /// antes, y la pantalla avisa de que cambian tambien sus semanas pasadas.
        /// </summary>
        public HistoriaFranja HistoriaDeFranja(int idFranja)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                FranjaGuardada franja = LeerFranjaGuardada(conexion, null, idFranja);
                if (franja == null)
                {
                    throw new InvalidOperationException("Esa franja ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.");
                }

                DateTime hoy;
                return LeerHistoria(conexion, null, franja, out hoy);
            }
        }

        /// <summary>
        /// HistoriaDeFranja en una conexion que ya se tiene. Tiene historia si tiene
        /// sesiones, alguna cancelacion (de cualquier fecha: la justificacion de
        /// una clase que no se dio, o una ya prevista) o algun dia pasado de su
        /// vigencia en que se daba: Faltas ya lo juzgo, aunque no haya sesion.
        /// </summary>
        private static HistoriaFranja LeerHistoria(MySqlConnection conexion, MySqlTransaction transaccion, FranjaGuardada franja, out DateTime hoy)
        {
            HistoriaFranja historia = new HistoriaFranja();
            int canceladas;
            // Con db/22, una sesion de hoy con asistencias que dio su profesor cuenta
            // como con bitacoras (el cambio, desde manana); hoy_con_dadas, para el aviso.
            bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT CURDATE() AS hoy,
                       (SELECT COUNT(*) FROM codigos_accesos ca WHERE ca.horarios_idhorarios = @Id) AS sesiones,
                       (SELECT COUNT(*) FROM codigos_accesos ca
                         WHERE ca.horarios_idhorarios = @Id AND ca.fecha = CURDATE()
                           AND " + ConRegistros(conDadas, "ca") + @") AS hoy_con_bitacoras,
                       " + (conDadas ? @"(SELECT COUNT(*) FROM codigos_accesos ca
                         WHERE ca.horarios_idhorarios = @Id AND ca.fecha = CURDATE()
                           AND EXISTS (SELECT 1 FROM asistencias_dadas ad WHERE ad.fk_codigo_accesos = ca.idcodigos_accesos))" : "0") + @" AS hoy_con_dadas,
                       (SELECT COUNT(*) FROM horario_excepciones x WHERE x.tipo = 'cancelada' AND x.horarios_idhorarios = @Id) AS canceladas,
                       (SELECT COUNT(*) FROM horario_excepciones x
                         WHERE x.tipo = 'cancelada' AND x.horarios_idhorarios = @Id AND x.fecha >= CURDATE()) AS canceladas_futuras", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", franja.Id);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    reader.Read();
                    hoy = reader.GetDateTime("hoy").Date;
                    historia.Sesiones = Convert.ToInt32(reader["sesiones"]);
                    historia.SesionHoyConBitacoras = Convert.ToInt32(reader["hoy_con_bitacoras"]) > 0;
                    historia.SesionHoyConDadas = Convert.ToInt32(reader["hoy_con_dadas"]) > 0;
                    canceladas = Convert.ToInt32(reader["canceladas"]);
                    historia.CancelacionesFuturas = Convert.ToInt32(reader["canceladas_futuras"]);
                }
            }

            DateTime ayer = hoy.AddDays(-1);
            historia.SemanasPasadas = VecesQueCae(franja.Desde, franja.Hasta < ayer ? franja.Hasta : ayer, franja.DiaSemana);
            historia.TieneHistoria = historia.Sesiones > 0 || canceladas > 0 || historia.SemanasPasadas > 0;
            historia.PrimerDiaPosible = historia.SesionHoyConBitacoras ? hoy.AddDays(1) : hoy;
            historia.ConFecha = VigenciaFranjas.ConFecha(conexion, transaccion);
            return historia;
        }

        /// <summary>Cuantas fechas de ese dia de la semana (1 = lunes) hay de desde a hasta, los dos incluidos.</summary>
        private static int VecesQueCae(DateTime desde, DateTime hasta, int diaSemana)
        {
            if (hasta.Date < desde.Date)
            {
                return 0;
            }

            DateTime primera = desde.Date.AddDays((diaSemana - DiaDeLaSemana(desde) + 7) % 7);
            return primera > hasta.Date ? 0 : (int)((hasta.Date - primera).TotalDays / 7) + 1;
        }

        /// <summary>1 = lunes ... 7 = domingo, como horarios.dia_semana (WEEKDAY() + 1).</summary>
        private static int DiaDeLaSemana(DateTime fecha)
        {
            return ((int)fecha.DayOfWeek + 6) % 7 + 1;
        }

        /// <summary>
        /// Una franja con historia y los cambios desde una fecha encendidos se
        /// cambia o se quita sin tocar lo pasado; si no, se hace en su sitio, como
        /// antes: con ConFecha apagado (con aviso en la pantalla), o sin historia y
        /// sin dias antes de desde en que se daria (desde no pasa de hoy ni de su
        /// primer dia). Una sin historia pero que aun se daria antes de desde
        /// tambien se parte: es lo que pidio el administrador.
        /// </summary>
        private static bool EnSuSitio(HistoriaFranja historia, FranjaGuardada franja, DateTime hoy, DateTime desde)
        {
            return !historia.ConFecha || (!historia.TieneHistoria && desde <= (hoy > franja.Desde ? hoy : franja.Desde));
        }

        /// <summary>InvalidOperationException si desde es anterior al primer dia en que puede aplicar un cambio.</summary>
        private static void ExigirPrimerDiaPosible(HistoriaFranja historia, DateTime hoy, DateTime desde, string que)
        {
            string problema = ProblemaDePrimerDia(historia, hoy, desde, que);
            if (problema != null)
            {
                throw new InvalidOperationException(problema);
            }
        }

        /// <summary>Por que desde es anterior al primer dia en que puede aplicar un cambio, o null (tambien la vista previa de un reemplazo).</summary>
        private static string ProblemaDePrimerDia(HistoriaFranja historia, DateTime hoy, DateTime desde, string que)
        {
            if (desde >= historia.PrimerDiaPosible)
            {
                return null;
            }

            return historia.SesionHoyConDadas
                ? "Hoy ya hubo clase en esa franja, con bitácoras o asistencias que dio su docente: " + que + " desde mañana ("
                  + historia.PrimerDiaPosible.ToString("dd/MM/yyyy") + ")."
                : historia.SesionHoyConBitacoras
                ? "Hoy ya hubo clase en esa franja y se registraron bitácoras: " + que + " desde mañana (" + historia.PrimerDiaPosible.ToString("dd/MM/yyyy") + ")."
                : "No puede ser desde una fecha que ya pasó: " + que + " desde hoy (" + hoy.ToString("dd/MM/yyyy") + ") en adelante.";
        }

        /// <summary>
        /// Las fechas de esas cancelaciones cuyo resto (lo que le quedaba a la
        /// clase de un recorte, LeerRestos, con las horas de antes) ya no seria
        /// resto de la franja con sus datos nuevos: otra clase u otro laboratorio,
        /// otro dia de la semana, o fuera de sus horas. Quedaria como una sesion
        /// extra suelta junto a la franja nueva cancelada ese dia. Sin contarUsados,
        /// uno que ya tiene bitacoras (o asistencias dadas) no cuenta: es lo que
        /// paso ese dia (cambio en su sitio, sin fecha que elegir).
        /// </summary>
        private static List<DateTime> RecortesQueNoCaben(MySqlConnection conexion, MySqlTransaction transaccion, List<CanceladaDeFranja> canceladas,
            FranjaGuardada vieja, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, bool contarUsados)
        {
            bool mismoSitio = idClase == vieja.ClaseId && idLaboratorio == vieja.LaboratorioId;
            return canceladas
                .Where(x => LeerRestos(conexion, transaccion, x.Id).Any(r => (contarUsados || (r.Bitacoras == 0 && r.Dadas == 0))
                    && (!mismoSitio || DiaDeLaSemana(x.Fecha) != diaSemana || r.Inicio < inicio || r.Fin > fin)))
                .Select(x => x.Fecha).ToList();
        }

        /// <summary>Que hacer con RecortesQueNoCaben; conFecha: el cambio aplica desde una fecha que se puede elegir.</summary>
        private static string TextoDeRecortesQueNoCaben(List<DateTime> recortes, bool conFecha)
        {
            return "Primero quita " + (recortes.Count == 1 ? "el recorte del " : "los recortes del ") + ListaDeFechas(recortes)
                + " (en Cambios de un día)" + (conFecha ? " o elige otra fecha" : "") + ": lo que le quedaba a la clase "
                + (recortes.Count == 1 ? "ese día" : "esos días") + " ya no encajaría en el horario nuevo.";
        }

        /// <summary>
        /// Un cambio en su sitio (ActualizarFranja, o CambiarFranjaDesde sin
        /// fecha; sin db/20, como en la escuela, siempre) se lleva sus
        /// cancelaciones, pero no lo que le quedaba a la clase de un recorte de hoy
        /// en adelante: si ya no le cabe (RecortesQueNoCaben), ese dia quedaria el
        /// resto con las horas de antes y la franja nueva cancelada (y, al quitar
        /// la cancelacion, la clase dos veces). InvalidOperationException con que
        /// hacer, como la rama con fecha.
        /// </summary>
        private static void ExigirQueLosRestosQuepan(MySqlConnection conexion, MySqlTransaction transaccion, FranjaGuardada vieja, DateTime hoy,
            int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            List<DateTime> recortes = RecortesQueNoCaben(conexion, transaccion, CanceladasDesde(conexion, transaccion, vieja.Id, hoy),
                vieja, idClase, idLaboratorio, diaSemana, inicio, fin, false);
            if (recortes.Count > 0)
            {
                throw new InvalidOperationException(TextoDeRecortesQueNoCaben(recortes, false));
            }
        }

        private static DateTime Hoy(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT CURDATE()", conexion, transaccion))
            {
                return Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture).Date;
            }
        }

        public int CrearFranja(int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            return CrearFranja(idClase, idLaboratorio, diaSemana, inicio, fin, null);
        }

        /// <summary>
        /// Crea una franja. Con los cambios desde una fecha encendidos
        /// (VigenciaFranjas.ConFecha) y una clase que ya empezo, la franja empieza
        /// en desde (sin desde, hoy segun el servidor): si no, apareceria tambien
        /// en las semanas pasadas y Faltas las pintaria de rojo. Un desde posterior
        /// al inicio de su clase se guarda aunque la clase no haya empezado; uno
        /// anterior no hace falta. Si no, vale toda la vigencia de su clase, como
        /// antes. desde no puede ser anterior a hoy (servidor):
        /// InvalidOperationException. Devuelve su id.
        /// </summary>
        public int CrearFranja(int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime? desde)
        {
            Permisos.Exigir("Crear una franja del horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                DateTime hoy = Hoy(conexion, transaccion);
                if (desde.HasValue && desde.Value.Date < hoy)
                {
                    throw new InvalidOperationException("La franja no puede empezar el " + desde.Value.ToString("dd/MM/yyyy")
                        + ": ese día ya pasó. Elige hoy (" + hoy.ToString("dd/MM/yyyy") + ") o una fecha posterior.");
                }

                DateTime? propia = null;
                ClaseInfo clase = LeerClaseEn(conexion, transaccion, idClase);
                if (clase != null && VigenciaFranjas.ConFecha(conexion, transaccion))
                {
                    DateTime? dia = desde.HasValue ? desde.Value.Date : (clase.VigenteDesde.Date < hoy ? hoy : (DateTime?)null);
                    if (dia.HasValue && dia.Value > clase.VigenteDesde.Date)
                    {
                        propia = dia;
                    }
                }

                string conflicto = ValidarFranjaEn(conexion, transaccion, null, idClase, idLaboratorio, diaSemana, inicio, fin, propia, null);
                if (conflicto != null)
                {
                    throw new InvalidOperationException(conflicto);
                }

                int id = InsertarFranja(conexion, transaccion, idClase, idLaboratorio, diaSemana, inicio, fin, propia, null);
                if (HistorialCambios.Activo(conexion, transaccion))
                {
                    FotoFranja nueva = FotoDeFranja(conexion, transaccion, id);
                    AnotarFranja(conexion, transaccion, AccionDeCambio.Alta, id, null, nueva, propia, nueva == null ? null
                        : nueva.Clase + ": " + nueva.Donde + (propia.HasValue ? ", desde el " + HistorialCambios.Fecha(propia.Value) : "") + ".");
                }

                transaccion.Commit();
                if (propia.HasValue)
                {
                    Log.Info("Franja " + id + " creada a partir del " + propia.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".");
                }

                return id;
            }
        }

        /// <summary>El INSERT de una franja; con desde o hasta, con su vigencia propia (solo con db/20).</summary>
        private static int InsertarFranja(MySqlConnection conexion, MySqlTransaction transaccion, int idClase, int idLaboratorio, int diaSemana,
            TimeSpan inicio, TimeSpan fin, DateTime? desde, DateTime? hasta)
        {
            bool conFechas = desde.HasValue || hasta.HasValue;
            using (MySqlCommand comando = new MySqlCommand(conFechas
                ? @"
                INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin, vigente_desde, vigente_hasta)
                VALUES (@Clase, @Lab, @Dia, @Inicio, @Fin, @Desde, @Hasta);
                SELECT LAST_INSERT_ID();"
                : @"
                INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin)
                VALUES (@Clase, @Lab, @Dia, @Inicio, @Fin);
                SELECT LAST_INSERT_ID();", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Desde", desde.HasValue ? (object)desde.Value.Date : DBNull.Value);
                comando.Parameters.AddWithValue("@Hasta", hasta.HasValue ? (object)hasta.Value.Date : DBNull.Value);
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        /// <summary>
        /// Cambia una franja en su sitio, como siempre: tambien en sus semanas
        /// pasadas. Con los cambios desde una fecha encendidos (ConFecha), una
        /// franja con historia no se cambia asi (InvalidOperationException): se
        /// usa CambiarFranjaDesde, para que nada reescriba el pasado por error.
        /// Tampoco si lo que le queda a la clase de un recorte de hoy en adelante
        /// ya no le cabria (ExigirQueLosRestosQuepan).
        /// </summary>
        public void ActualizarFranja(int idFranja, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            Permisos.Exigir("Editar una franja del horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                FranjaGuardada franja = LeerFranjaGuardada(conexion, transaccion, idFranja);
                bool conHistoria = false;
                if (franja != null)
                {
                    DateTime hoy;
                    HistoriaFranja historia = LeerHistoria(conexion, transaccion, franja, out hoy);
                    // Para el administrador: la pantalla que lo pide leyo la franja
                    // antes de que ganara historia (CambiarFranjaDesde es lo que toca).
                    if (historia.ConFecha && historia.TieneHistoria)
                    {
                        throw new InvalidOperationException("Esta franja ya se dio antes: el cambio tiene que aplicar desde una fecha. "
                            + "Vuelve a abrirla y elige «Aplica desde».");
                    }

                    conHistoria = historia.TieneHistoria;
                    ExigirQueLosRestosQuepan(conexion, transaccion, franja, hoy, idClase, idLaboratorio, diaSemana, inicio, fin);
                }

                string conflicto = ValidarFranjaEn(conexion, transaccion, idFranja, idClase, idLaboratorio, diaSemana, inicio, fin,
                    franja == null ? (DateTime?)null : franja.FranjaDesde, franja == null ? (DateTime?)null : franja.FranjaHasta);
                if (conflicto != null)
                {
                    throw new InvalidOperationException(conflicto);
                }

                FotoFranja antes = HistorialCambios.Activo(conexion, transaccion) ? FotoDeFranja(conexion, transaccion, idFranja) : null;
                ActualizarFranjaEn(conexion, transaccion, idFranja, idClase, idLaboratorio, diaSemana, inicio, fin);
                AnotarFranjaCambiada(conexion, transaccion, idFranja, antes, conHistoria);
                transaccion.Commit();
            }
        }

        /// <summary>El UPDATE de ActualizarFranja, y su sesion de hoy aun sin bitacoras con las horas nuevas.</summary>
        private static void ActualizarFranjaEn(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja, int idClase, int idLaboratorio,
            int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                UPDATE horarios
                   SET clases_idclases = @Clase, laboratorios_idlaboratorios = @Lab, dia_semana = @Dia, hora_inicio = @Inicio, hora_fin = @Fin
                 WHERE idhorarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.ExecuteNonQuery();
            }

            // La sesion de hoy de esta franja que ya se creo (o abrio el panel del
            // profesor) pero en la que nadie ha registrado: con las horas, el
            // laboratorio y la clase nuevos, que es lo que ensenan la agenda, el
            // panel y la pantalla del alumno; si no, se quedaba con los viejos todo
            // el dia y nadie podia registrar. Conserva su codigo, que quiza ya se
            // dicto. Con bitacoras (o asistencias que dio su profesor, db/22) se
            // deja: con esas horas se registraron.
            using (MySqlCommand sesion = new MySqlCommand(@"
                UPDATE codigos_accesos ca
                INNER JOIN clases c ON c.idclases = @Clase
                   SET ca.hora_entrada = @Entrada, ca.hora_salida = @Salida, ca.laboratorios_idlaboratorios = @Lab,
                       ca.usuarios_idusuarios = c.usuarios_idusuarios, ca.grupos_idgrupos = c.grupos_idgrupos,
                       ca.materias_id_materia = c.materias_id_materia
                 WHERE ca.horarios_idhorarios = @Id AND ca.fecha = CURDATE() AND @Dia = WEEKDAY(CURDATE()) + 1
                   AND " + SinRegistros(Esquema.AsistenciaDada(conexion, transaccion), "ca"), conexion, transaccion))
            {
                sesion.Parameters.AddWithValue("@Id", idFranja);
                sesion.Parameters.AddWithValue("@Clase", idClase);
                sesion.Parameters.AddWithValue("@Lab", idLaboratorio);
                sesion.Parameters.AddWithValue("@Dia", diaSemana);
                sesion.Parameters.AddWithValue("@Entrada", HoraParaGuardar(inicio));
                sesion.Parameters.AddWithValue("@Salida", HoraParaGuardar(fin));
                if (sesion.ExecuteNonQuery() > 0)
                {
                    Log.Info("Franja " + idFranja + " cambiada: su sesion de hoy, aun sin bitacoras, pasa a " + Hora(inicio) + "-" + Hora(fin) + ".");
                }
            }
        }

        /// <summary>
        /// Cambia una franja (su clase, laboratorio, dia u horas) a partir de una
        /// fecha sin tocar lo que ya paso (db/20): la vieja termina el dia antes
        /// (vigente_hasta) y se crea la nueva desde ese dia hasta donde llegaba la
        /// vieja. desde no puede ser anterior a hoy (servidor), ni a manana si hoy
        /// ya hubo sesion de la franja con bitacoras. Con desde = hoy, su sesion
        /// de hoy aun sin bitacoras pasa a la nueva, con sus horas (como en
        /// ActualizarFranja), o se queda sin franja si la nueva no se da hoy. Las
        /// cancelaciones de la vieja de ese dia en adelante pasan a la nueva si
        /// caen en su dia de la semana y sus restos (un recorte) le caben (misma
        /// clase y laboratorio, dentro de sus horas); si no, no se guarda nada
        /// (InvalidOperationException que las enumera). La nueva se valida como
        /// en ValidarFranja, con su vigencia: no choca con la vieja.
        /// Con ConFecha apagado, o una franja sin historia que tampoco se daria
        /// antes de desde, se cambia en su sitio como en ActualizarFranja. Todo en
        /// una transaccion. Devuelve el id de la franja nueva, o el de la misma si
        /// se cambio en su sitio o no cambia nada.
        /// </summary>
        public int CambiarFranjaDesde(int idFranja, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime desde)
        {
            Permisos.Exigir("Editar una franja del horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                int id = CambiarFranjaDesdeEn(conexion, transaccion, idFranja, idClase, idLaboratorio, diaSemana, inicio, fin, desde, null);
                transaccion.Commit();
                return id;
            }
        }

        /// <summary>
        /// CambiarFranjaDesde en una transaccion que ya se tiene (sin Commit): asi
        /// la usa tambien el reemplazo de una clase por otra (db/25), todo junto.
        /// porque, si no es null, va al final del renglon del historial.
        /// </summary>
        private static int CambiarFranjaDesdeEn(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja, int idClase, int idLaboratorio,
            int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime desde, string porque)
        {
            DateTime dia = desde.Date;
            {
                FranjaGuardada vieja = LeerFranjaGuardada(conexion, transaccion, idFranja);
                if (vieja == null)
                {
                    throw new InvalidOperationException("Esa franja ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.");
                }

                DateTime hoy;
                HistoriaFranja historia = LeerHistoria(conexion, transaccion, vieja, out hoy);
                string conflicto;
                bool conHistorial = HistorialCambios.Activo(conexion, transaccion);
                if (EnSuSitio(historia, vieja, hoy, dia))
                {
                    ExigirQueLosRestosQuepan(conexion, transaccion, vieja, hoy, idClase, idLaboratorio, diaSemana, inicio, fin);
                    conflicto = ValidarFranjaEn(conexion, transaccion, idFranja, idClase, idLaboratorio, diaSemana, inicio, fin, vieja.FranjaDesde, vieja.FranjaHasta);
                    if (conflicto != null)
                    {
                        throw new InvalidOperationException(conflicto);
                    }

                    FotoFranja enSuSitio = conHistorial ? FotoDeFranja(conexion, transaccion, idFranja) : null;
                    ActualizarFranjaEn(conexion, transaccion, idFranja, idClase, idLaboratorio, diaSemana, inicio, fin);
                    AnotarFranjaCambiada(conexion, transaccion, idFranja, enSuSitio, historia.TieneHistoria, porque);
                    return idFranja;
                }

                if (vieja.Igual(idClase, idLaboratorio, diaSemana, inicio, fin))
                {
                    return idFranja;
                }

                ExigirPrimerDiaPosible(historia, hoy, dia, "el cambio puede aplicar");
                if (dia > vieja.Hasta)
                {
                    throw new InvalidOperationException("Esa franja termina el " + vieja.Hasta.ToString("dd/MM/yyyy") + ": desde el "
                        + dia.ToString("dd/MM/yyyy") + " ya no hay nada que cambiar.");
                }

                // Sus cancelaciones de ese dia en adelante pasan a la nueva, si caen
                // en su dia de la semana; las demas se quedarian sin nada que cancelar.
                List<CanceladaDeFranja> canceladas = CanceladasDesde(conexion, transaccion, idFranja, dia);
                List<DateTime> otroDia = canceladas.Where(x => DiaDeLaSemana(x.Fecha) != diaSemana).Select(x => x.Fecha).ToList();
                if (otroDia.Count > 0)
                {
                    throw new InvalidOperationException("Primero quita " + (otroDia.Count == 1 ? "la cancelación del " : "las cancelaciones del ")
                        + ListaDeFechas(otroDia) + ": desde el " + dia.ToString("dd/MM/yyyy") + " esa clase pasa al "
                        + NombresDia[diaSemana].ToLower(Espanol) + " y ya no habría nada que cancelar " + (otroDia.Count == 1 ? "ese día." : "esos días."));
                }

                // Lo que le quedaba a la clase de un recorte (LeerRestos, con las
                // horas de la vieja) solo sigue siendo su resto con la nueva si esta
                // es de la misma clase y laboratorio y lo contiene; si no, quedaria
                // como una sesion extra suelta junto a la clase entera.
                List<DateTime> recortes = RecortesQueNoCaben(conexion, transaccion, canceladas, vieja, idClase, idLaboratorio, diaSemana, inicio, fin, true);
                if (recortes.Count > 0)
                {
                    throw new InvalidOperationException(TextoDeRecortesQueNoCaben(recortes, true));
                }

                // La nueva no empieza antes que la vieja (si esta empezaba despues de desde).
                DateTime nuevaDesde = vieja.FranjaDesde.HasValue && vieja.FranjaDesde.Value > dia ? vieja.FranjaDesde.Value : dia;
                FotoFranja antes = conHistorial ? FotoDeFranja(conexion, transaccion, idFranja) : null;
                using (MySqlCommand comando = new MySqlCommand("UPDATE horarios SET vigente_hasta = @Hasta WHERE idhorarios = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Hasta", dia.AddDays(-1));
                    comando.Parameters.AddWithValue("@Id", idFranja);
                    comando.ExecuteNonQuery();
                }

                // Ya con la vieja terminada: no choca con la nueva. Los dias de sus
                // cancelaciones, que pasan a la nueva, no chocan con una sesion extra.
                conflicto = ValidarFranjaEn(conexion, transaccion, null, idClase, idLaboratorio, diaSemana, inicio, fin, nuevaDesde, vieja.FranjaHasta,
                    null, idFranja);
                if (conflicto != null)
                {
                    throw new InvalidOperationException(conflicto);
                }

                int nueva = InsertarFranja(conexion, transaccion, idClase, idLaboratorio, diaSemana, inicio, fin, nuevaDesde, vieja.FranjaHasta);
                if (canceladas.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE horario_excepciones SET horarios_idhorarios = @Nueva
                         WHERE tipo = 'cancelada' AND horarios_idhorarios = @Vieja AND fecha >= @Desde", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Nueva", nueva);
                        comando.Parameters.AddWithValue("@Vieja", idFranja);
                        comando.Parameters.AddWithValue("@Desde", dia);
                        comando.ExecuteNonQuery();
                    }
                }

                // Desde hoy: la sesion de hoy de la vieja, aun sin bitacoras, pasa a
                // la nueva si la nueva se da hoy (con bitacoras no se llega aqui con
                // desde = hoy). Conserva su codigo, que quiza ya se dicto. Si la
                // nueva no se da hoy (otro dia de la semana, su clase no es
                // vigente...), se queda sin franja: la vieja ya no se da hoy.
                if (dia == hoy)
                {
                    using (MySqlCommand sesion = new MySqlCommand(@"
                        UPDATE codigos_accesos ca
                        INNER JOIN clases c ON c.idclases = @Clase
                           SET ca.horarios_idhorarios = @Nueva, ca.hora_entrada = @Entrada, ca.hora_salida = @Salida,
                               ca.laboratorios_idlaboratorios = @Lab, ca.usuarios_idusuarios = c.usuarios_idusuarios,
                               ca.grupos_idgrupos = c.grupos_idgrupos, ca.materias_id_materia = c.materias_id_materia
                         WHERE ca.horarios_idhorarios = @Vieja AND ca.fecha = CURDATE() AND @Dia = WEEKDAY(CURDATE()) + 1
                           AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta AND @NuevaDesde <= CURDATE()
                           AND " + SinRegistros(Esquema.AsistenciaDada(conexion, transaccion), "ca"), conexion, transaccion))
                    {
                        sesion.Parameters.AddWithValue("@Nueva", nueva);
                        sesion.Parameters.AddWithValue("@Vieja", idFranja);
                        sesion.Parameters.AddWithValue("@Clase", idClase);
                        sesion.Parameters.AddWithValue("@Lab", idLaboratorio);
                        sesion.Parameters.AddWithValue("@Dia", diaSemana);
                        sesion.Parameters.AddWithValue("@NuevaDesde", nuevaDesde);
                        sesion.Parameters.AddWithValue("@Entrada", HoraParaGuardar(inicio));
                        sesion.Parameters.AddWithValue("@Salida", HoraParaGuardar(fin));
                        if (sesion.ExecuteNonQuery() > 0)
                        {
                            Log.Info("Franja " + idFranja + " cambiada desde hoy: su sesion de hoy, aun sin bitacoras, pasa a la franja " + nueva + ".");
                        }
                    }

                    // La que no paso a la nueva (esta no se da hoy).
                    DesenlazarSesionDeHoy(conexion, transaccion, idFranja);
                }

                // Un solo renglon: la vieja termina y la nueva sigue (es lo que pidio el administrador).
                if (antes != null)
                {
                    FotoFranja despues = FotoDeFranja(conexion, transaccion, nueva);
                    AnotarFranja(conexion, transaccion, AccionDeCambio.Cambio, idFranja, antes, despues, dia, despues == null ? null
                        : (antes.ClaseId == despues.ClaseId ? despues.Clase + ": desde el " + HistorialCambios.Fecha(dia) + ", de " + antes.Donde + " a " + despues.Donde
                            : "Desde el " + HistorialCambios.Fecha(dia) + ", de " + antes.Clase + ", " + antes.Donde + ", a " + despues.Clase + ", " + despues.Donde)
                          + (canceladas.Count == 1 ? "; su cancelación de esa fecha en adelante pasa a la nueva"
                             : canceladas.Count > 1 ? "; sus " + canceladas.Count + " cancelaciones de esa fecha en adelante pasan a la nueva" : "")
                          + "." + (porque == null ? "" : " " + porque));
                }

                Log.Info("Franja " + idFranja + " cambiada desde el " + dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": termina el dia antes y sigue como la franja "
                    + nueva + (canceladas.Count > 0 ? " (con " + canceladas.Count + " cancelaciones)" : "") + ".");
                return nueva;
            }
        }

        /// <summary>
        /// Quita una franja a partir de una fecha sin tocar lo que ya paso (db/20):
        /// termina el dia antes (vigente_hasta; nunca se alarga) y se borran sus
        /// cancelaciones de ese dia en adelante, cada una con sus restos (lo que
        /// le quedaba a la clase de un recorte, LeerRestos): si un resto ya tiene
        /// bitacoras no se guarda nada (InvalidOperationException). Con desde =
        /// hoy, su sesion de hoy aun sin bitacoras se queda sin franja. desde no
        /// puede ser anterior a hoy (servidor), ni a manana si hoy ya hubo sesion
        /// de la franja con bitacoras. Con ConFecha apagado, o una franja sin
        /// historia que tampoco se daria antes de desde, se borra como en
        /// BorrarFranja.
        /// </summary>
        public void QuitarFranjaDesde(int idFranja, DateTime desde)
        {
            Permisos.Exigir("Borrar una franja del horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                QuitarFranjaDesdeEn(conexion, transaccion, idFranja, desde, null);
                transaccion.Commit();
            }
        }

        /// <summary>
        /// QuitarFranjaDesde en una transaccion que ya se tiene (sin Commit), para
        /// el reemplazo y "Quitar horas" con lo que queda por asignar (db/25).
        /// porque, si no es null, va al final del renglon del historial.
        /// </summary>
        private static void QuitarFranjaDesdeEn(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja, DateTime desde, string porque)
        {
            DateTime dia = desde.Date;
            {
                FranjaGuardada franja = LeerFranjaGuardada(conexion, transaccion, idFranja);
                if (franja == null)
                {
                    return;
                }

                DateTime hoy;
                HistoriaFranja historia = LeerHistoria(conexion, transaccion, franja, out hoy);
                bool conHistorial = HistorialCambios.Activo(conexion, transaccion);
                if (EnSuSitio(historia, franja, hoy, dia))
                {
                    BorrarFranjaConHistorial(conexion, transaccion, idFranja, historia.TieneHistoria, porque);
                    return;
                }

                ExigirPrimerDiaPosible(historia, hoy, dia, "se puede quitar");
                FotoFranja antes = conHistorial ? FotoDeFranja(conexion, transaccion, idFranja) : null;

                // Sus cancelaciones de ese dia en adelante se van con sus restos: si
                // no, el resto seguiria como sesion extra de una clase que ya no se
                // da. Antes de escribir nada: con bitacoras en un resto, no se quita.
                List<CanceladaDeFranja> canceladas = CanceladasDesde(conexion, transaccion, idFranja, dia);
                Dictionary<int, List<RestoDeClase>> restos = canceladas.ToDictionary(x => x.Id, x => LeerRestos(conexion, transaccion, x.Id));
                string conRegistros = ProblemaDeRestosUsados(canceladas, restos, dia);
                if (conRegistros != null)
                {
                    throw new InvalidOperationException(conRegistros);
                }

                if (!franja.FranjaHasta.HasValue || franja.FranjaHasta.Value > dia.AddDays(-1))
                {
                    using (MySqlCommand comando = new MySqlCommand("UPDATE horarios SET vigente_hasta = @Hasta WHERE idhorarios = @Id", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Hasta", dia.AddDays(-1));
                        comando.Parameters.AddWithValue("@Id", idFranja);
                        comando.ExecuteNonQuery();
                    }
                }

                int restosBorrados = 0;
                foreach (CanceladaDeFranja cancelada in canceladas)
                {
                    foreach (RestoDeClase resto in restos[cancelada.Id])
                    {
                        BorrarExcepcionEn(conexion, transaccion, resto.Id);
                        restosBorrados++;
                    }

                    BorrarExcepcionEn(conexion, transaccion, cancelada.Id);
                }

                // Desde hoy ya no se da hoy: su sesion de hoy, si la abrieron, sin franja.
                if (dia == hoy)
                {
                    DesenlazarSesionDeHoy(conexion, transaccion, idFranja);
                }

                if (antes != null)
                {
                    AnotarFranja(conexion, transaccion, AccionDeCambio.Fin, idFranja, antes, FotoDeFranja(conexion, transaccion, idFranja), dia,
                        antes.Clase + ": " + antes.Donde + ". Ya no se da desde el " + HistorialCambios.Fecha(dia)
                        + (canceladas.Count == 1 ? "; se quita su cancelación de esa fecha en adelante"
                           : canceladas.Count > 1 ? "; se quitan sus " + canceladas.Count + " cancelaciones de esa fecha en adelante" : "")
                        + "." + (porque == null ? "" : " " + porque));
                }

                Log.Info("Franja " + idFranja + " quitada desde el " + dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + (canceladas.Count > 0 ? " (con " + canceladas.Count + " cancelaciones" + (restosBorrados > 0 ? " y " + restosBorrados + " restos de recortes" : "") + ")" : "") + ".");
            }
        }

        /// <summary>
        /// QuitarFranjaDesde: si un resto de esas cancelaciones (restos, por su id)
        /// ya tiene bitacoras o asistencias dadas, por que no se puede quitar desde
        /// dia; si no, null (tambien la vista previa de un reemplazo).
        /// </summary>
        private static string ProblemaDeRestosUsados(List<CanceladaDeFranja> canceladas, Dictionary<int, List<RestoDeClase>> restos, DateTime dia)
        {
            foreach (CanceladaDeFranja cancelada in canceladas)
            {
                RestoDeClase usado = restos[cancelada.Id].FirstOrDefault(r => r.Bitacoras > 0 || r.Dadas > 0);
                if (usado != null)
                {
                    return "El recorte del " + cancelada.Fecha.ToString("dd/MM/yyyy") + " " + YaTieneRegistros(usado.Dadas) + " "
                        + "(lo que le quedaba a la clase, " + usado + "): no se puede quitar la franja desde el " + dia.ToString("dd/MM/yyyy")
                        + ". Elige una fecha posterior al " + cancelada.Fecha.ToString("dd/MM/yyyy") + ".";
                }
            }

            return null;
        }

        /// <summary>Una cancelacion de una franja (CanceladasDesde).</summary>
        private sealed class CanceladaDeFranja
        {
            public int Id;
            public DateTime Fecha;
        }

        /// <summary>Las cancelaciones de una franja de desde en adelante, por fecha.</summary>
        private static List<CanceladaDeFranja> CanceladasDesde(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja, DateTime desde)
        {
            List<CanceladaDeFranja> canceladas = new List<CanceladaDeFranja>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idhorario_excepciones, fecha FROM horario_excepciones WHERE tipo = 'cancelada' AND horarios_idhorarios = @Id AND fecha >= @Desde ORDER BY fecha",
                conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        canceladas.Add(new CanceladaDeFranja { Id = reader.GetInt32(0), Fecha = reader.GetDateTime(1).Date });
                    }
                }
            }

            return canceladas;
        }

        /// <summary>
        /// La sesion de hoy de una franja que desde hoy ya no se da (termino
        /// ayer), si nadie ha registrado en ella: se queda sin franja, con sus
        /// datos, como la de una sesion extra que se quita. No se borra: un
        /// reporte del profesor (db/15) cuelga de ella y se iria con ella. Con
        /// bitacoras (o asistencias que dio su profesor, db/22) no llega aqui: la
        /// franja termina hoy como pronto.
        /// </summary>
        private static void DesenlazarSesionDeHoy(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                UPDATE codigos_accesos ca
                   SET ca.horarios_idhorarios = NULL
                 WHERE ca.horarios_idhorarios = @Id AND ca.fecha = CURDATE()
                   AND " + SinRegistros(Esquema.AsistenciaDada(conexion, transaccion), "ca"), conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                if (comando.ExecuteNonQuery() > 0)
                {
                    Log.Info("Franja " + idFranja + " ya no se da hoy: su sesion de hoy, aun sin bitacoras, se queda sin franja.");
                }
            }
        }

        /// <summary>"19/10/2026", "19/10/2026 y del 26/10/2026", "5/10/2026, del 12/10/2026 y del 19/10/2026".</summary>
        private static string ListaDeFechas(List<DateTime> fechas)
        {
            List<string> textos = fechas.Select(f => f.ToString("dd/MM/yyyy")).ToList();
            return textos.Count == 1 ? textos[0] : string.Join(", del ", textos.Take(textos.Count - 1)) + " y del " + textos[textos.Count - 1];
        }

        /// <summary>
        /// Borra una franja, como siempre: con ella sus cancelaciones (y lo que le
        /// quedaba a la clase de sus recortes de hoy en adelante), y sus
        /// sesiones se quedan sin enlace. Con los cambios desde una fecha
        /// encendidos (ConFecha), una franja con historia no se borra
        /// (InvalidOperationException): se usa QuitarFranjaDesde.
        /// </summary>
        public void BorrarFranja(int idFranja)
        {
            Permisos.Exigir("Borrar una franja del horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                FranjaGuardada franja = LeerFranjaGuardada(conexion, transaccion, idFranja);
                bool conHistoria = false;
                if (franja != null)
                {
                    DateTime hoy;
                    HistoriaFranja historia = LeerHistoria(conexion, transaccion, franja, out hoy);
                    // Para el administrador, como en ActualizarFranja (QuitarFranjaDesde es lo que toca).
                    if (historia.ConFecha && historia.TieneHistoria)
                    {
                        throw new InvalidOperationException("Esta franja ya se dio antes: quitarla tiene que aplicar desde una fecha. "
                            + "Vuelve a quitarla y elige «Quitar desde».");
                    }

                    conHistoria = historia.TieneHistoria;
                }

                BorrarFranjaConHistorial(conexion, transaccion, idFranja, conHistoria);
                transaccion.Commit();
            }
        }

        /// <summary>
        /// BorrarFranjaEn y su renglon en el historial (db/21), con lo que se lleva
        /// la base en cascada: sus cancelaciones de cualquier fecha. Antes, lo que
        /// le quedaba a la clase de sus recortes de hoy en adelante (LeerRestos):
        /// la cascada no se lo lleva y ese dia la clase seguiria aunque ya no
        /// este en el horario (sin db/20, como en la escuela, se borra asi). Un
        /// resto que ya tiene bitacoras o asistencias dadas es lo que paso ese dia
        /// y se queda, como los de dias pasados.
        /// </summary>
        private static void BorrarFranjaConHistorial(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja, bool conHistoria, string porque = null)
        {
            FotoFranja antes = HistorialCambios.Activo(conexion, transaccion) ? FotoDeFranja(conexion, transaccion, idFranja) : null;
            int canceladas = 0;
            if (antes != null)
            {
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT COUNT(*) FROM horario_excepciones WHERE tipo = 'cancelada' AND horarios_idhorarios = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", idFranja);
                    canceladas = Convert.ToInt32(comando.ExecuteScalar());
                }
            }

            int recortes = 0, restos = 0;
            foreach (CanceladaDeFranja cancelada in CanceladasDesde(conexion, transaccion, idFranja, Hoy(conexion, transaccion)))
            {
                List<RestoDeClase> sinUsar = LeerRestos(conexion, transaccion, cancelada.Id).Where(r => r.Bitacoras == 0 && r.Dadas == 0).ToList();
                foreach (RestoDeClase resto in sinUsar)
                {
                    BorrarExcepcionEn(conexion, transaccion, resto.Id);
                }

                recortes += sinUsar.Count > 0 ? 1 : 0;
                restos += sinUsar.Count;
            }

            BorrarFranjaEn(conexion, transaccion, idFranja);
            if (antes != null)
            {
                AnotarFranja(conexion, transaccion, AccionDeCambio.Quitar, idFranja, antes, null, null,
                    antes.Clase + ": " + antes.Donde + ". Se borra del horario" + (conHistoria ? ", también de las semanas que ya pasaron" : "")
                    + (canceladas == 1 ? ", con su cancelación" : canceladas > 1 ? ", con sus " + canceladas + " cancelaciones" : "")
                    + (recortes == 1 ? " y lo que le quedaba a la clase de su recorte" : recortes > 1 ? " y lo que le quedaba a la clase de sus " + recortes + " recortes" : "")
                    + "." + (porque == null ? "" : " " + porque));
            }

            if (restos > 0)
            {
                Log.Info("Franja " + idFranja + " borrada con " + restos + " restos de sus recortes de hoy en adelante.");
            }
        }

        private static void BorrarFranjaEn(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja)
        {
            using (MySqlCommand comando = new MySqlCommand("DELETE FROM horarios WHERE idhorarios = @Id", conexion, transaccion))
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
                       c.usuarios_idusuarios AS profesor_id, u.nombre AS profesor_nombre, u.apellido_paterno AS profesor_paterno,
                       g.nombre_grupo, m.nombre_materia, x.creada_en,
                       (SELECT COUNT(*) FROM codigos_accesos ca INNER JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                         WHERE ca.horario_excepciones_id = x.idhorario_excepciones) AS bitacoras,
                       " + DadasDeLaExtra(Esquema.AsistenciaDada(conexion), "x") + @"
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
                            ProfesorId = Convert.ToInt32(reader["profesor_id"]),
                            Profesor = reader.GetString("profesor"),
                            ProfesorCorto = NombreCorto(Texto(reader, "profesor_nombre"), Texto(reader, "profesor_paterno")),
                            Grupo = reader.GetString("nombre_grupo"),
                            Materia = reader.GetString("nombre_materia"),
                            CreadaEn = reader.IsDBNull(reader.GetOrdinal("creada_en")) ? (DateTime?)null : reader.GetDateTime("creada_en"),
                            Bitacoras = Convert.ToInt32(reader["bitacoras"]),
                            Dadas = Convert.ToInt32(reader["dadas"])
                        });
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Cancela una franja un dia. Solo si ese dia se da (su dia de la semana,
        /// clase activa y dentro de su vigencia, db/20): si no, no hay nada que
        /// cancelar (InvalidOperationException).
        /// </summary>
        public int CancelarFranja(int idFranja, DateTime fecha, string motivo, int creadaPor)
        {
            Permisos.Exigir("Cancelar una clase un dia");
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
                    using (MySqlCommand comando = new MySqlCommand(@"
                        SELECT COUNT(*) FROM horarios h
                        INNER JOIN clases c ON c.idclases = h.clases_idclases
                        WHERE h.idhorarios = @Id AND h.dia_semana = WEEKDAY(@Fecha) + 1
                          AND " + VigenciaFranjas.SeDa(conVigencia, "@Fecha"), conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", idFranja);
                        comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                        if (Convert.ToInt32(comando.ExecuteScalar()) == 0)
                        {
                            throw new InvalidOperationException("Esa clase no se da el " + fecha.ToString("dd/MM/yyyy")
                                + " (es otro día de la semana, o está fuera de su vigencia o desactivada): no hay nada que cancelar.");
                        }
                    }

                    int id = InsertarCancelada(conexion, transaccion, idFranja, fecha, motivo, creadaPor);
                    AnotarExcepcionNueva(conexion, transaccion, id);
                    transaccion.Commit();
                    return id;
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new InvalidOperationException("Esa franja ya está cancelada el " + fecha.ToString("dd/MM/yyyy") + ": no hay que hacer nada más.");
            }
        }

        /// <param name="creadaEn">
        /// Null: ahora. Las de un "No tomar en cuenta" llevan todas el mismo
        /// (NoTomarEnCuenta, y Aplicar con "Ahora mismo" para las de hoy que ya
        /// terminaron: CicloPreliminarDatos.Aplicar.cs).
        /// </param>
        internal static int InsertarCancelada(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja, DateTime fecha, string motivo, int creadaPor,
            DateTime? creadaEn = null)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO horario_excepciones (tipo, fecha, horarios_idhorarios, motivo, creada_por, creada_en)
                VALUES ('cancelada', @Fecha, @Franja, @Motivo, @CreadaPor, COALESCE(@CreadaEn, NOW()));
                SELECT LAST_INSERT_ID();", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                comando.Parameters.AddWithValue("@Franja", idFranja);
                comando.Parameters.AddWithValue("@Motivo", ParaMotivo(motivo));
                comando.Parameters.AddWithValue("@CreadaPor", creadaPor > 0 ? (object)creadaPor : DBNull.Value);
                comando.Parameters.AddWithValue("@CreadaEn", creadaEn.HasValue ? (object)creadaEn.Value : DBNull.Value);
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        /// <param name="creadaEn">Null: ahora. El trozo de una extra que se parte se queda con el del original.</param>
        private static int InsertarExtra(MySqlConnection conexion, MySqlTransaction transaccion, int idClase, int idLaboratorio, DateTime fecha,
            TimeSpan inicio, TimeSpan fin, string motivo, int creadaPor, DateTime? creadaEn = null)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO horario_excepciones (tipo, fecha, clases_idclases, laboratorios_idlaboratorios, hora_inicio, hora_fin, motivo, creada_por, creada_en)
                VALUES ('extra', @Fecha, @Clase, @Lab, @Inicio, @Fin, @Motivo, @CreadaPor, COALESCE(@CreadaEn, NOW()));
                SELECT LAST_INSERT_ID();", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Motivo", ParaMotivo(motivo));
                comando.Parameters.AddWithValue("@CreadaPor", creadaPor > 0 ? (object)creadaPor : DBNull.Value);
                comando.Parameters.AddWithValue("@CreadaEn", creadaEn.HasValue ? (object)creadaEn.Value : DBNull.Value);
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        /// <summary>
        /// Null si la clase se puede dar ese dia (activa y vigente), o por que no:
        /// fuera de eso sus alumnos no podrian entrar a una sesion extra suya.
        /// </summary>
        private static string ProblemaDeVigencia(ClaseInfo clase, DateTime fecha)
        {
            if (clase.VigenteEn(fecha))
            {
                return null;
            }

            string cual = "La clase " + clase.Codigo + " " + clase.Materia + " - " + clase.Grupo;
            return (clase.Activa
                    ? cual + " solo es vigente del " + clase.VigenteDesde.ToString("dd/MM/yyyy") + " al " + clase.VigenteHasta.ToString("dd/MM/yyyy")
                    : cual + " está desactivada")
                + ": el " + fecha.ToString("dd/MM/yyyy") + " sus estudiantes no podrían entrar a una sesión extra suya.";
        }

        /// <summary>
        /// Lo que cabe en horario_excepciones.motivo (200): en MySQL estricto lo de mas
        /// daria error y en la escuela se cortaria sin avisar. Sin emojis: la tabla de
        /// la escuela es utf8 de 3 bytes y los guardaba como "??" (y asi salian en
        /// Excepciones, en el panel y en el historial).
        /// </summary>
        private static object ParaMotivo(string motivo)
        {
            string texto = HistorialCambios.Limpiar(motivo, 200);
            return texto == null ? (object)DBNull.Value : texto;
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
                return "Esa clase ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.";
            }

            string vigencia = ProblemaDeVigencia(clase, fecha);
            if (vigencia != null)
            {
                return vigencia;
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return ValidarExtraEn(conexion, null, excluirExcepcionId, clase, idLaboratorio, fecha, inicio, fin);
            }
        }

        /// <summary>
        /// ValidarExtra en una conexion (y transaccion) que ya se tiene: la de
        /// AplicarExcepcion ve asi las franjas que acaba de cancelar. Una sesion
        /// extra de una clase desactivada o ya terminada ese dia no cuenta (nadie la
        /// puede usar), como en ChoqueConExtra.
        /// </summary>
        /// <param name="devuelta">
        /// Es una franja a la que se le quita la cancelacion (QuitarCancelacionEn):
        /// un choque con otra franja ya estaba en el horario de todas las semanas
        /// (ValidarFranjaEn no mira las cancelaciones) y no impide devolverla, como
        /// en "Volver a contar" de Faltas; tampoco lo que le queda de un recorte a
        /// una de esas franjas, que es parte de ella. Solo cuenta lo puesto para ese
        /// dia: las demas sesiones extra.
        /// </param>
        private static string ValidarExtraEn(MySqlConnection conexion, MySqlTransaction transaccion, int? excluirExcepcionId, ClaseInfo clase,
            int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin, bool devuelta = false)
        {
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            if (!devuelta)
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
                      AND " + VigenciaFranjas.SeDa(conVigencia, "@Fecha") + @"
                      AND h.hora_inicio < @Fin AND h.hora_fin > @Inicio
                      AND (h.laboratorios_idlaboratorios = @Lab OR c.usuarios_idusuarios = @Profesor OR c.grupos_idgrupos = @Grupo)
                      AND NOT EXISTS (SELECT 1 FROM horario_excepciones x
                                       WHERE x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios AND x.fecha = @Fecha)
                    ORDER BY h.hora_inicio
                    LIMIT 1", conexion, transaccion))
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
            }

            // Lo que le queda de un recorte a otra franja (LeerRestos) es parte de ella.
            string sinRestos = !devuelta ? "" : @"
                  AND NOT EXISTS (SELECT 1 FROM horario_excepciones y
                                  INNER JOIN horarios hy ON hy.idhorarios = y.horarios_idhorarios
                                   WHERE y.tipo = 'cancelada' AND y.fecha = x.fecha
                                     AND hy.clases_idclases = x.clases_idclases AND hy.laboratorios_idlaboratorios = x.laboratorios_idlaboratorios
                                     AND x.hora_inicio >= hy.hora_inicio AND x.hora_fin <= hy.hora_fin)";
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
                  AND c.activa = 1 AND x.fecha BETWEEN c.vigente_desde AND c.vigente_hasta
                  AND (x.laboratorios_idlaboratorios = @Lab OR c.usuarios_idusuarios = @Profesor OR c.grupos_idgrupos = @Grupo)" + sinRestos + @"
                ORDER BY x.hora_inicio
                LIMIT 1", conexion, transaccion))
            {
                AgregarParametrosConflicto(comando, clase, idLaboratorio, fecha, inicio, fin, excluirExcepcionId ?? 0);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return DescribirConflicto(reader) + " (sesión extra)";
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

        /// <summary>
        /// Una sesion extra en horas libres, comprobada contra la agenda efectiva
        /// de ese dia (ValidarExtra). Sobre clases del horario se pone con
        /// AplicarExcepcion, que les quita solo lo que se cruza. Se quitan con
        /// QuitarExtra; las cancelaciones, con QuitarCancelacion (con sus restos).
        /// </summary>
        public int CrearExtra(int idClase, int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin, string motivo, int creadaPor)
        {
            Permisos.Exigir("Crear una sesion extra");
            if (fin <= inicio)
            {
                throw new InvalidOperationException("La hora de fin tiene que ser posterior a la de inicio.");
            }

            string conflicto = ValidarExtra(null, idClase, idLaboratorio, fecha, inicio, fin);
            if (conflicto != null)
            {
                throw new InvalidOperationException(conflicto);
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ComprobarQueNoPaso(conexion, transaccion, fecha, inicio, fin);
                int id = InsertarExtra(conexion, transaccion, idClase, idLaboratorio, fecha, inicio, fin, motivo, creadaPor);
                AnotarExcepcionNueva(conexion, transaccion, id);
                transaccion.Commit();
                return id;
            }
        }

        /// <summary>
        /// Una sesion extra cuya hora de fin ya paso (reloj del servidor) no la
        /// podria usar nadie: InvalidOperationException. Se mira el fin, no el
        /// inicio: el administrador suele ponerla con la clase ya empezada.
        /// </summary>
        private static void ComprobarQueNoPaso(MySqlConnection conexion, MySqlTransaction transaccion, DateTime fecha, TimeSpan inicio, TimeSpan fin)
        {
            DateTime ahora;
            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion, transaccion))
            {
                ahora = Convert.ToDateTime(comando.ExecuteScalar());
            }

            if (fecha.Date < ahora.Date || (fecha.Date == ahora.Date && fin <= ahora.TimeOfDay))
            {
                throw new InvalidOperationException("El " + fecha.ToString("dd/MM/yyyy") + " de " + Hora(inicio) + " a " + Hora(fin)
                    + " ya pasó: una sesión extra ahí no la podría usar nadie. Elige otra fecha.");
            }
        }

        // ---------------------------------------------------------------------
        // Excepcion sobre un tramo de la cuadricula: a lo que se cruza se le
        // quita SOLO lo que se cruza
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lo que le queda a una clase (inicio-fin) si se le quita un tramo
        /// (quitarDesde-quitarHasta): nada, lo de antes, lo de despues, o las dos
        /// cosas si el tramo cae en medio. Si no se cruzan, la clase entera.
        /// </summary>
        public static List<TramoHorario> Restos(TimeSpan inicio, TimeSpan fin, TimeSpan quitarDesde, TimeSpan quitarHasta)
        {
            List<TramoHorario> restos = new List<TramoHorario>();
            if (quitarDesde >= fin || quitarHasta <= inicio)
            {
                restos.Add(new TramoHorario(inicio, fin));
                return restos;
            }

            if (quitarDesde > inicio)
            {
                restos.Add(new TramoHorario(inicio, quitarDesde));
            }

            if (quitarHasta < fin)
            {
                restos.Add(new TramoHorario(quitarHasta, fin));
            }

            return restos;
        }

        /// <summary>Lo que se le quita a una clase (inicio-fin) con un tramo: el cruce.</summary>
        public static TramoHorario Cruce(TimeSpan inicio, TimeSpan fin, TimeSpan quitarDesde, TimeSpan quitarHasta)
        {
            return new TramoHorario(quitarDesde > inicio ? quitarDesde : inicio, quitarHasta < fin ? quitarHasta : fin);
        }

        /// <summary>El motivo de lo que le queda a una clase recortada.</summary>
        private static string MotivoDeResto(string motivo)
        {
            return "Le queda de su horario" + (string.IsNullOrWhiteSpace(motivo) ? "" : " (" + motivo.Trim() + ")");
        }

        /// <summary>
        /// Una excepcion sobre un tramo de un dia en un laboratorio (lo seleccionado
        /// en la cuadricula del administrador), todo o nada:
        /// - a cada franja que se cruce con el tramo se le quita SOLO lo que se
        ///   cruza: se cancela ese dia y lo que le sobra queda como sesion extra de
        ///   su misma clase. Asi lo entienden ya todos los equipos, tambien sin
        ///   conexion, sin migrar nada. Una franja ya cancelada ese dia se deja como
        ///   esta (no se le devuelve nada);
        /// - a cada sesion extra del laboratorio que se cruce (tambien un resto de
        ///   un recorte anterior), lo mismo: se acorta, se parte o se quita. Si ya
        ///   tiene bitacoras no se toca y no se hace nada;
        /// - con idClaseExtra, entra la sesion extra de esa clase en el tramo,
        ///   comprobada como cualquier otra (el profesor o el grupo en otro
        ///   laboratorio, otra extra), ya sin lo recortado.
        /// InvalidOperationException si la extra choca con algo, si sus horas ya
        /// pasaron o si hay que tocar algo con bitacoras. Cancelar sin extra si se
        /// puede hacia atras: es como se justifica una clase que no se dio.
        /// </summary>
        public ResultadoExcepcion AplicarExcepcion(int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin, int? idClaseExtra, string motivo, int creadaPor)
        {
            Permisos.Exigir("Aplicar una excepcion al horario");
            if (fin <= inicio)
            {
                throw new InvalidOperationException("La hora de fin tiene que ser posterior a la de inicio.");
            }

            ClaseInfo claseExtra = null;
            if (idClaseExtra.HasValue)
            {
                claseExtra = ObtenerClase(idClaseExtra.Value);
                if (claseExtra == null)
                {
                    throw new InvalidOperationException("La clase de la sesión extra ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.");
                }

                string vigencia = ProblemaDeVigencia(claseExtra, fecha);
                if (vigencia != null)
                {
                    throw new InvalidOperationException(vigencia);
                }
            }

            ResultadoExcepcion resultado = new ResultadoExcepcion();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                if (claseExtra != null)
                {
                    ComprobarQueNoPaso(conexion, transaccion, fecha, inicio, fin);
                }

                // Lo que se cruza: franjas que se dan ese dia (VigenciaFranjas.SeDa)...
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
                List<TramoDeClase> franjas;
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT h.idhorarios AS id, h.clases_idclases AS clase_id, h.hora_inicio AS inicio, h.hora_fin AS fin, NULL AS motivo, NULL AS creada_en,
                           c.codigo, m.nombre_materia, g.nombre_grupo, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                           (SELECT COUNT(*) FROM horario_excepciones y
                             WHERE y.tipo = 'cancelada' AND y.horarios_idhorarios = h.idhorarios AND y.fecha = @Fecha) AS marca,
                           0 AS dadas
                    FROM horarios h
                    INNER JOIN clases c   ON c.idclases = h.clases_idclases
                    INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    WHERE h.laboratorios_idlaboratorios = @Lab AND h.dia_semana = WEEKDAY(@Fecha) + 1
                      AND " + VigenciaFranjas.SeDa(conVigencia, "@Fecha") + @"
                      AND h.hora_inicio < @Fin AND h.hora_fin > @Inicio
                    ORDER BY h.hora_inicio", conexion, transaccion))
                {
                    AgregarParametrosTramo(comando, idLaboratorio, fecha, inicio, fin);
                    franjas = LeerTramosDeClase(comando);
                }

                // ...y sesiones extra del laboratorio ese dia, con sus bitacoras (y,
                // con db/22, las asistencias que dio su profesor en ellas).
                bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
                List<TramoDeClase> extras;
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT x.idhorario_excepciones AS id, x.clases_idclases AS clase_id, x.hora_inicio AS inicio, x.hora_fin AS fin, x.motivo, x.creada_en,
                           c.codigo, m.nombre_materia, g.nombre_grupo, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                           (SELECT COUNT(*) FROM codigos_accesos ca INNER JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                             WHERE ca.horario_excepciones_id = x.idhorario_excepciones) AS marca,
                           " + DadasDeLaExtra(conDadas, "x") + @"
                    FROM horario_excepciones x
                    INNER JOIN clases c   ON c.idclases = x.clases_idclases
                    INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    WHERE x.tipo = 'extra' AND x.laboratorios_idlaboratorios = @Lab AND x.fecha = @Fecha
                      AND x.hora_inicio < @Fin AND x.hora_fin > @Inicio
                    ORDER BY x.hora_inicio", conexion, transaccion))
                {
                    AgregarParametrosTramo(comando, idLaboratorio, fecha, inicio, fin);
                    extras = LeerTramosDeClase(comando);
                }

                TramoDeClase usada = extras.FirstOrDefault(e => e.Marca > 0 || e.Dadas > 0);
                if (usada != null)
                {
                    throw new InvalidOperationException("La sesión extra de " + usada.Describir() + " " + YaTieneRegistros(usada.Dadas) + ": no se puede recortar. "
                        + "Elige otras horas.");
                }

                // Historial (db/21): un renglon por clase tocada; lo que le queda a una
                // recortada va en el suyo (los restos no tienen renglon propio).
                string laboratorio = HistorialCambios.Activo(conexion, transaccion) ? NombreDeLaboratorio(conexion, transaccion, idLaboratorio) : null;
                foreach (TramoDeClase franja in franjas)
                {
                    if (franja.Marca > 0)
                    {
                        resultado.Lineas.Add(franja.Describir() + ": ya estaba cancelada ese día; se deja así.");
                        continue;
                    }

                    int idCancelada = InsertarCancelada(conexion, transaccion, franja.Id, fecha, motivo, creadaPor);
                    List<TramoHorario> restos = Restos(franja.Inicio, franja.Fin, inicio, fin);
                    foreach (TramoHorario resto in restos)
                    {
                        InsertarExtra(conexion, transaccion, franja.ClaseId, idLaboratorio, fecha, resto.Inicio, resto.Fin, MotivoDeResto(motivo), creadaPor);
                    }

                    if (laboratorio != null)
                    {
                        AnotarTramo(conexion, transaccion, TipoDeCambio.Cancelacion, restos.Count == 0 ? AccionDeCambio.Alta : AccionDeCambio.Recorte,
                            idCancelada, idLaboratorio, laboratorio, fecha, franja, inicio, fin, restos, motivo);
                    }

                    if (restos.Count == 0)
                    {
                        resultado.Canceladas++;
                        resultado.Lineas.Add(franja.Describir() + ": se cancela ese día.");
                    }
                    else
                    {
                        resultado.Recortadas++;
                        resultado.Lineas.Add(franja.Describir() + ": se le quita " + Cruce(franja.Inicio, franja.Fin, inicio, fin)
                            + "; ese día da " + string.Join(" y ", restos) + ".");
                    }
                }

                foreach (TramoDeClase extra in extras)
                {
                    List<TramoHorario> restos = Restos(extra.Inicio, extra.Fin, inicio, fin);
                    if (laboratorio != null)
                    {
                        AnotarTramo(conexion, transaccion, TipoDeCambio.Extra, restos.Count == 0 ? AccionDeCambio.Quitar : AccionDeCambio.Cambio,
                            extra.Id, idLaboratorio, laboratorio, fecha, extra, inicio, fin, restos, motivo);
                    }

                    if (restos.Count == 0)
                    {
                        // Sin bitacoras (comprobado arriba): su sesion vacia, si la abrieron, se queda sin enlace.
                        using (MySqlCommand comando = new MySqlCommand("DELETE FROM horario_excepciones WHERE idhorario_excepciones = @Id", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Id", extra.Id);
                            comando.ExecuteNonQuery();
                        }

                        resultado.Canceladas++;
                        resultado.Lineas.Add("Sesión extra de " + extra.Describir() + ": se quita.");
                        continue;
                    }

                    // Se queda con el primer trozo (y su sesion, si se abrio, con esas horas); el segundo, aparte.
                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE horario_excepciones SET hora_inicio = @Inicio, hora_fin = @Fin WHERE idhorario_excepciones = @Id;
                        UPDATE codigos_accesos SET hora_entrada = @Entrada, hora_salida = @Salida
                         WHERE horario_excepciones_id = @Id
                           AND " + SinRegistros(conDadas, "codigos_accesos") + ";", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", extra.Id);
                        comando.Parameters.AddWithValue("@Inicio", restos[0].Inicio);
                        comando.Parameters.AddWithValue("@Fin", restos[0].Fin);
                        comando.Parameters.AddWithValue("@Entrada", HoraParaGuardar(restos[0].Inicio));
                        comando.Parameters.AddWithValue("@Salida", HoraParaGuardar(restos[0].Fin));
                        comando.ExecuteNonQuery();
                    }

                    // El segundo trozo, con la hora en que se puso el original (el Calendario la mira).
                    for (int i = 1; i < restos.Count; i++)
                    {
                        InsertarExtra(conexion, transaccion, extra.ClaseId, idLaboratorio, fecha, restos[i].Inicio, restos[i].Fin, extra.Motivo, creadaPor, extra.CreadaEn);
                    }

                    resultado.Recortadas++;
                    resultado.Lineas.Add("Sesión extra de " + extra.Describir() + ": se le quita " + Cruce(extra.Inicio, extra.Fin, inicio, fin)
                        + "; queda " + string.Join(" y ", restos) + ".");
                }

                if (claseExtra != null)
                {
                    string conflicto = ValidarExtraEn(conexion, transaccion, null, claseExtra, idLaboratorio, fecha, inicio, fin);
                    if (conflicto != null)
                    {
                        throw new InvalidOperationException(conflicto);
                    }

                    int idExtra = InsertarExtra(conexion, transaccion, claseExtra.Id, idLaboratorio, fecha, inicio, fin, motivo, creadaPor);
                    AnotarExcepcionNueva(conexion, transaccion, idExtra);
                    resultado.ExtraCreada = true;
                    resultado.Lineas.Insert(0, "Entra la sesión extra de " + claseExtra.Codigo + " " + claseExtra.Materia + " - " + claseExtra.Grupo
                        + " (" + claseExtra.Profesor + "), de " + Hora(inicio) + " a " + Hora(fin) + ".");
                }

                transaccion.Commit();
            }

            return resultado;
        }

        /// <summary>Una franja o una extra que se cruza con el tramo de AplicarExcepcion.</summary>
        private sealed class TramoDeClase
        {
            public int Id;
            public int ClaseId;
            public TimeSpan Inicio;
            public TimeSpan Fin;
            public string Motivo;
            public string Codigo;
            public string Materia;
            public string Grupo;
            public string Profesor;
            /// <summary>En una extra, cuando se puso; en una franja, null.</summary>
            public DateTime? CreadaEn;
            /// <summary>En una franja, si ya estaba cancelada ese dia (mayor que 0); en una extra, sus alumnos con bitacora.</summary>
            public int Marca;
            /// <summary>En una extra, las asistencias que dio su profesor en su sesion (db/22, tambien anuladas); en una franja, 0.</summary>
            public int Dadas;

            public string Describir()
            {
                return Codigo + " " + Materia + " - " + Grupo + " (" + Profesor + ", " + Hora(Inicio) + "-" + Hora(Fin) + ")";
            }
        }

        private static void AgregarParametrosTramo(MySqlCommand comando, int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin)
        {
            comando.Parameters.AddWithValue("@Lab", idLaboratorio);
            comando.Parameters.AddWithValue("@Fecha", fecha.Date);
            comando.Parameters.AddWithValue("@Inicio", inicio);
            comando.Parameters.AddWithValue("@Fin", fin);
        }

        private static List<TramoDeClase> LeerTramosDeClase(MySqlCommand comando)
        {
            List<TramoDeClase> tramos = new List<TramoDeClase>();
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    tramos.Add(new TramoDeClase
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        ClaseId = Convert.ToInt32(reader["clase_id"]),
                        Inicio = reader.GetTimeSpan("inicio"),
                        Fin = reader.GetTimeSpan("fin"),
                        Motivo = Texto(reader, "motivo"),
                        Codigo = Texto(reader, "codigo"),
                        Materia = Texto(reader, "nombre_materia"),
                        Grupo = Texto(reader, "nombre_grupo"),
                        Profesor = Texto(reader, "profesor"),
                        CreadaEn = reader.IsDBNull(reader.GetOrdinal("creada_en")) ? (DateTime?)null : Convert.ToDateTime(reader["creada_en"]),
                        Marca = Convert.ToInt32(reader["marca"]),
                        Dadas = Convert.ToInt32(reader["dadas"])
                    });
                }
            }

            return tramos;
        }

        /// <summary>
        /// Lo que le quedo a la franja de una cancelacion (AplicarExcepcion): sus
        /// sesiones extra de la misma clase, ese dia, en su laboratorio y dentro de
        /// sus horas, con sus bitacoras.
        /// </summary>
        public List<RestoDeClase> RestosDeCancelacion(int idCancelacion)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerRestos(conexion, null, idCancelacion);
            }
        }

        private static List<RestoDeClase> LeerRestos(MySqlConnection conexion, MySqlTransaction transaccion, int idCancelacion)
        {
            List<RestoDeClase> restos = new List<RestoDeClase>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT r.idhorario_excepciones, r.hora_inicio, r.hora_fin,
                       (SELECT COUNT(*) FROM codigos_accesos ca INNER JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                         WHERE ca.horario_excepciones_id = r.idhorario_excepciones) AS bitacoras,
                       " + DadasDeLaExtra(Esquema.AsistenciaDada(conexion, transaccion), "r") + @"
                FROM horario_excepciones x
                INNER JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
                INNER JOIN horario_excepciones r
                        ON r.tipo = 'extra' AND r.clases_idclases = h.clases_idclases AND r.laboratorios_idlaboratorios = h.laboratorios_idlaboratorios
                       AND r.fecha = x.fecha AND r.hora_inicio >= h.hora_inicio AND r.hora_fin <= h.hora_fin
                WHERE x.idhorario_excepciones = @Id AND x.tipo = 'cancelada'
                ORDER BY r.hora_inicio", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idCancelacion);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        restos.Add(new RestoDeClase
                        {
                            Id = reader.GetInt32("idhorario_excepciones"),
                            Inicio = reader.GetTimeSpan("hora_inicio"),
                            Fin = reader.GetTimeSpan("hora_fin"),
                            Bitacoras = Convert.ToInt32(reader["bitacoras"]),
                            Dadas = Convert.ToInt32(reader["dadas"])
                        });
                    }
                }
            }

            return restos;
        }

        /// <summary>
        /// Quita una cancelacion y, con ella, lo que le quedaba a la clase como
        /// sesion extra (RestosDeCancelacion): si no, ese dia tendria su franja
        /// entera y ademas el resto, dos sesiones de la misma clase. Si un resto
        /// ya tiene bitacoras no se hace nada (InvalidOperationException).
        /// </summary>
        public void QuitarCancelacion(int idCancelacion)
        {
            Permisos.Exigir("Quitar una cancelacion");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                QuitarCancelacionEn(conexion, transaccion, idCancelacion);
                transaccion.Commit();
            }
        }

        private static void QuitarCancelacionEn(MySqlConnection conexion, MySqlTransaction transaccion, int idCancelacion)
        {
            FotoExcepcion antes = HistorialCambios.Activo(conexion, transaccion) ? FotoDeExcepcion(conexion, transaccion, idCancelacion) : null;
            List<RestoDeClase> restos = LeerRestos(conexion, transaccion, idCancelacion);
            RestoDeClase usado = restos.FirstOrDefault(r => r.Bitacoras > 0 || r.Dadas > 0);
            if (usado != null)
            {
                throw new InvalidOperationException("Lo que le quedaba de esa clase ese día (" + usado + ") " + YaTieneRegistros(usado.Dadas) + ": "
                    + "no se puede devolver la clase entera.");
            }

            foreach (RestoDeClase resto in restos)
            {
                BorrarExcepcionEn(conexion, transaccion, resto.Id);
            }

            // La clase vuelve entera ese dia: que no choque con lo que se puso para
            // ese dia (una sesion extra de otra clase en el laboratorio, o de su
            // profesor o su grupo en otro). Un choque con otra franja ya estaba en
            // el horario de todas las semanas y no la impide (ValidarExtraEn con
            // devuelta), como en "Volver a contar". Con la cancelacion aun puesta,
            // para que no choque consigo misma.
            ClaseInfo clase = null;
            int idLaboratorio = 0;
            DateTime fecha = DateTime.MinValue;
            TimeSpan inicio = TimeSpan.Zero, fin = TimeSpan.Zero;
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.idclases, c.usuarios_idusuarios, c.grupos_idgrupos, c.codigo, m.nombre_materia, g.nombre_grupo,
                       h.laboratorios_idlaboratorios, h.hora_inicio, h.hora_fin, x.fecha
                FROM horario_excepciones x
                INNER JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
                INNER JOIN clases c   ON c.idclases = h.clases_idclases
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                WHERE x.idhorario_excepciones = @Id AND x.tipo = 'cancelada'", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idCancelacion);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        clase = new ClaseInfo
                        {
                            Id = Convert.ToInt32(reader["idclases"]),
                            ProfesorId = Convert.ToInt32(reader["usuarios_idusuarios"]),
                            GrupoId = Convert.ToInt32(reader["grupos_idgrupos"]),
                            Codigo = Texto(reader, "codigo"),
                            Materia = Texto(reader, "nombre_materia"),
                            Grupo = Texto(reader, "nombre_grupo")
                        };
                        idLaboratorio = Convert.ToInt32(reader["laboratorios_idlaboratorios"]);
                        inicio = reader.GetTimeSpan("hora_inicio");
                        fin = reader.GetTimeSpan("hora_fin");
                        fecha = reader.GetDateTime("fecha");
                    }
                }
            }

            if (clase != null)
            {
                string conflicto = ValidarExtraEn(conexion, transaccion, null, clase, idLaboratorio, fecha, inicio, fin, true);
                if (conflicto != null)
                {
                    throw new InvalidOperationException("No se puede devolver " + clase.Codigo + " " + clase.Materia + " - " + clase.Grupo + " ("
                        + Hora(inicio) + "-" + Hora(fin) + ") entera el " + fecha.ToString("dd/MM/yyyy") + ". " + conflicto
                        + " Quita antes eso (o recórtalo con una excepción).");
                }
            }

            BorrarExcepcionEn(conexion, transaccion, idCancelacion);
            if (antes != null && !antes.EsExtra)
            {
                AnotarExcepcion(conexion, transaccion, AccionDeCambio.Quitar, antes, null,
                    antes.Clase + ": se da otra vez el " + antes.Cuando
                    + (restos.Count > 0 ? ", entera (se quita lo que le quedaba como sesión extra)" : "") + ".");
            }
        }

        private static void BorrarExcepcionEn(MySqlConnection conexion, MySqlTransaction transaccion, int idExcepcion)
        {
            using (MySqlCommand comando = new MySqlCommand("DELETE FROM horario_excepciones WHERE idhorario_excepciones = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idExcepcion);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Las cancelaciones que pudo provocar una sesion extra puesta sobre el
        /// horario (AplicarExcepcion): las de ese dia de franjas de su laboratorio
        /// que se cruzan con sus horas, de OTRAS clases (si es un resto, la de su
        /// propia clase es la que lo dejo, no algo que devolver). Solo de clases cuya vigencia
        /// cubre ese dia: a una que ya termino no se le puede devolver; una desactivada si puede volver.
        /// </summary>
        public List<ExcepcionInfo> CancelacionesBajoExtra(int idExtra)
        {
            List<ExcepcionInfo> lista = new List<ExcepcionInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.idhorario_excepciones, x.fecha, x.horarios_idhorarios, h.hora_inicio, h.hora_fin, c.codigo, m.nombre_materia, g.nombre_grupo,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       h.clases_idclases AS clase_id, h.laboratorios_idlaboratorios AS lab_id
                FROM horario_excepciones e
                INNER JOIN horario_excepciones x ON x.tipo = 'cancelada' AND x.fecha = e.fecha
                INNER JOIN horarios h ON h.idhorarios = x.horarios_idhorarios AND h.laboratorios_idlaboratorios = e.laboratorios_idlaboratorios
                                     AND h.hora_inicio < e.hora_fin AND h.hora_fin > e.hora_inicio AND h.clases_idclases <> e.clases_idclases
                INNER JOIN clases c   ON c.idclases = h.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                WHERE e.idhorario_excepciones = @Id AND e.tipo = 'extra'
                  AND " + VigenciaFranjas.SeCruzaCon(Esquema.VigenciaDeFranjas(conexion), "x.fecha", "x.fecha") + @"
                ORDER BY h.hora_inicio", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idExtra);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ExcepcionInfo
                        {
                            Id = reader.GetInt32("idhorario_excepciones"),
                            Tipo = "cancelada",
                            Fecha = reader.GetDateTime("fecha"),
                            FranjaId = reader.GetInt32("horarios_idhorarios"),
                            ClaseId = Convert.ToInt32(reader["clase_id"]),
                            LaboratorioId = Convert.ToInt32(reader["lab_id"]),
                            Inicio = reader.GetTimeSpan("hora_inicio"),
                            Fin = reader.GetTimeSpan("hora_fin"),
                            Codigo = reader.GetString("codigo"),
                            Materia = reader.GetString("nombre_materia"),
                            Grupo = reader.GetString("nombre_grupo"),
                            Profesor = reader.GetString("profesor"),
                            Motivo = ""
                        });
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Quita una sesion extra; con devolverCanceladas, tambien las cancelaciones
        /// que provoco (CancelacionesBajoExtra) con sus restos: esas clases vuelven
        /// a su horario de siempre ese dia. Si la extra (o un resto que haya que
        /// quitar) ya tiene bitacoras, no se hace nada (InvalidOperationException):
        /// borrarla dejaria esas bitacoras sueltas.
        /// </summary>
        public void QuitarExtra(int idExtra, bool devolverCanceladas)
        {
            Permisos.Exigir("Quitar una sesion extra");
            List<int> cancelaciones = devolverCanceladas ? CancelacionesBajoExtra(idExtra).Select(x => x.Id).ToList() : new List<int>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                // Con db/22, tambien con una asistencia que dio su profesor (aun
                // anulada): la sesion se quedaria sin su clase y la lista la perderia.
                bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT (SELECT COUNT(*) FROM codigos_accesos ca INNER JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                            WHERE ca.horario_excepciones_id = @Id) AS bitacoras,
                           " + DadasDeLaExtra(conDadas, "x") + @"
                    FROM horario_excepciones x WHERE x.idhorario_excepciones = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", idExtra);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        int bitacoras = 0, dadas = 0;
                        if (reader.Read())
                        {
                            bitacoras = Convert.ToInt32(reader["bitacoras"]);
                            dadas = Convert.ToInt32(reader["dadas"]);
                        }

                        if (dadas > 0)
                        {
                            throw new InvalidOperationException("Esa sesión extra ya tiene bitácoras registradas o asistencias que dio su docente: no se puede quitar.");
                        }

                        if (bitacoras > 0)
                        {
                            throw new InvalidOperationException("Esa sesión extra ya tiene bitácoras registradas: no se puede quitar.");
                        }
                    }
                }

                FotoExcepcion antes = HistorialCambios.Activo(conexion, transaccion) ? FotoDeExcepcion(conexion, transaccion, idExtra) : null;
                using (MySqlCommand comando = new MySqlCommand("DELETE FROM horario_excepciones WHERE idhorario_excepciones = @Id AND tipo = 'extra'", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", idExtra);
                    comando.ExecuteNonQuery();
                }

                // Las cancelaciones que se devuelven dejan cada una su renglon (QuitarCancelacionEn).
                if (antes != null && antes.EsExtra)
                {
                    AnotarExcepcion(conexion, transaccion, AccionDeCambio.Quitar, antes, null,
                        antes.Clase + ": ya no hay sesión extra el " + antes.Cuando + ".");
                }

                foreach (int id in cancelaciones)
                {
                    QuitarCancelacionEn(conexion, transaccion, id);
                }

                transaccion.Commit();
            }
        }

        // ---------------------------------------------------------------------
        // "No tomar en cuenta" (Faltas): las clases sin bitacoras de unas fechas,
        // canceladas en lote como "Marcar como no dada", y "Volver a contar"
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lo que se anade al motivo de cada cancelacion de NoTomarEnCuenta: en
        /// Cambios de un dia se ve de donde salio, y LeerLotesSinContar las
        /// reconoce sin tabla nueva (sin migracion).
        /// </summary>
        public const string MarcaNoTomarEnCuenta = " (no tomar en cuenta)";

        /// <summary>Lo mas largo del motivo que se escribe: con la marca cabe en horario_excepciones.motivo (200).</summary>
        public const int LargoMotivoNoTomar = 170;

        /// <summary>
        /// Las que entran en "No tomar en cuenta": una clase del horario (franja)
        /// que ya paso, contaba y no tiene bitacoras, en rojo en Faltas
        /// (ClaseDelCalendario.Falta). No las que tienen bitacoras o asistencias
        /// dadas (no son Falta). Tampoco las sesiones extra ni lo que le quedo a
        /// una clase recortada (EsExtra): "Marcar como no dada" las quita, y en
        /// lote se borraria algo que no se puso aqui (y no se podria deshacer).
        /// </summary>
        public static bool EntraEnNoTomar(ClaseDelCalendario clase)
        {
            return clase != null && clase.Falta && !clase.EsExtra && clase.FranjaId.HasValue;
        }

        /// <summary>
        /// "No tomar en cuenta": cancela ese dia, en UNA transaccion, cada clase de
        /// clases que siga en rojo, con el motivo y MarcaNoTomarEnCuenta y todas con
        /// el mismo creada_en (la clave de VolverAContar), como CancelarFranja
        /// (InsertarCancelada); deja un solo renglon en el historial (db/21; sin
        /// ella, ninguno). Antes vuelve a leer esas fechas como Faltas
        /// (LeerCalendario, en la misma conexion y transaccion): lo que ya no esta
        /// en rojo no se toca. No borra nada. Si no queda ninguna que cancelar,
        /// InvalidOperationException con que hacer.
        /// </summary>
        /// <param name="clases">Las de la vista previa (solo cuentan las que EntraEnNoTomar).</param>
        /// <param name="contarTodos">Como la casilla de Faltas (CalendarioBitacoras.ContarTodos).</param>
        public ResultadoNoTomar NoTomarEnCuenta(IList<ClaseDelCalendario> clases, DateTime desde, DateTime hasta, bool contarTodos,
            string motivo, int creadaPor)
        {
            Permisos.Exigir("No tomar en cuenta clases sin bitacoras");
            string limpio = HistorialCambios.Limpiar(motivo, LargoMotivoNoTomar);
            if (limpio == null)
            {
                throw new InvalidOperationException("Escribe el motivo (por ejemplo: cambio de horario): queda en cada clase que deja de contar.");
            }

            List<ClaseDelCalendario> pedidas = (clases ?? new List<ClaseDelCalendario>()).Where(EntraEnNoTomar)
                .GroupBy(c => ClaveDeFranja(c.FranjaId.Value, c.Fecha)).Select(g => g.First()).ToList();
            if (pedidas.Count == 0)
            {
                throw new InvalidOperationException("En esas fechas no hay clases sin bitácoras: no hay nada que deje de contar.");
            }

            ResultadoNoTomar resultado = new ResultadoNoTomar();
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    // Como estan ahora: entre la vista previa y aceptar alguien pudo
                    // registrar, o otra persona cancelarlas.
                    DateTime primera = pedidas.Min(c => c.Fecha);
                    DateTime ultima = pedidas.Max(c => c.Fecha);
                    CalendarioBitacoras ahora = LeerCalendario(conexion, desde.Date < primera ? desde.Date : primera,
                        hasta.Date > ultima ? hasta.Date : ultima, contarTodos);
                    Dictionary<string, ClaseDelCalendario> enRojo = new Dictionary<string, ClaseDelCalendario>();
                    foreach (ClaseDelCalendario c in ahora.Clases.Where(EntraEnNoTomar))
                    {
                        enRojo[ClaveDeFranja(c.FranjaId.Value, c.Fecha)] = c;
                    }

                    List<ClaseDelCalendario> aCancelar = new List<ClaseDelCalendario>();
                    foreach (ClaseDelCalendario c in pedidas)
                    {
                        ClaseDelCalendario actual;
                        if (enRojo.TryGetValue(ClaveDeFranja(c.FranjaId.Value, c.Fecha), out actual))
                        {
                            aCancelar.Add(actual);
                        }
                    }

                    resultado.YaNoEnRojo = pedidas.Count - aCancelar.Count;
                    if (aCancelar.Count == 0)
                    {
                        throw new InvalidOperationException("Ninguna de esas clases sigue sin bitácoras: alguien registró o ya se cancelaron. "
                            + "Cierra este aviso y revisa la vista previa, que ya se volvió a leer.");
                    }

                    aCancelar = aCancelar.OrderBy(c => c.Fecha).ThenBy(c => c.Inicio).ThenBy(c => c.LaboratorioId).ToList();
                    string guardado = limpio + MarcaNoTomarEnCuenta;
                    DateTime creadaEn = MomentoDelLote(conexion, transaccion, guardado, creadaPor);
                    int primerId = 0;
                    foreach (ClaseDelCalendario c in aCancelar)
                    {
                        int id = InsertarCancelada(conexion, transaccion, c.FranjaId.Value, c.Fecha, guardado, creadaPor, creadaEn);
                        if (primerId == 0)
                        {
                            primerId = id;
                        }
                    }

                    resultado.Canceladas = aCancelar.Count;
                    if (HistorialCambios.Activo(conexion, transaccion))
                    {
                        AnotarNoTomar(conexion, transaccion, ahora, aCancelar, desde, hasta, limpio, primerId);
                    }

                    transaccion.Commit();
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new InvalidOperationException("Otra persona canceló alguna de esas clases mientras tanto y no se cambió nada. "
                    + "Cierra este aviso y vuelve a pulsar «No tomar en cuenta»: la vista previa ya se volvió a leer.");
            }

            Log.Info("No tomar en cuenta: " + resultado.Canceladas + " clases sin bitacoras canceladas del "
                + desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " al " + hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + (resultado.YaNoEnRojo > 0 ? "; " + resultado.YaNoEnRojo + " ya no estaban en rojo" : ""));
            return resultado;
        }

        /// <summary>
        /// El creada_en de un lote: la hora del servidor, o un segundo despues si
        /// ya hay otra cancelacion con la misma clave (mismo motivo, quien y
        /// segundo): VolverAContar la quitaria con este lote. Tambien lo usa
        /// Aplicar con "Ahora mismo" (CicloPreliminarDatos.Aplicar.cs).
        /// </summary>
        internal static DateTime MomentoDelLote(MySqlConnection conexion, MySqlTransaction transaccion, string motivo, int creadaPor)
        {
            DateTime momento;
            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion, transaccion))
            {
                momento = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            momento = new DateTime(momento.Year, momento.Month, momento.Day, momento.Hour, momento.Minute, momento.Second);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT COUNT(*) FROM horario_excepciones
                WHERE tipo = 'cancelada' AND motivo = @Motivo AND creada_en = @CreadaEn AND creada_por <=> @CreadaPor", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Motivo", ParaMotivo(motivo));
                comando.Parameters.AddWithValue("@CreadaPor", creadaPor > 0 ? (object)creadaPor : DBNull.Value);
                MySqlParameter cuando = comando.Parameters.AddWithValue("@CreadaEn", momento);
                for (int i = 0; i < 120 && Convert.ToInt32(comando.ExecuteScalar()) > 0; i++)
                {
                    momento = momento.AddSeconds(1);
                    cuando.Value = momento;
                }
            }

            return momento;
        }

        /// <summary>"Prueba Codigo unico" o "3 laboratorios", de las clases de un lote.</summary>
        private static string DondeDelLote(CalendarioBitacoras calendario, List<ClaseDelCalendario> clases)
        {
            List<int> laboratorios = clases.Select(c => c.LaboratorioId).Distinct().ToList();
            string nombre;
            return laboratorios.Count == 1
                ? (calendario.Laboratorios.TryGetValue(laboratorios[0], out nombre) ? nombre : "el laboratorio " + laboratorios[0])
                : laboratorios.Count + " laboratorios";
        }

        /// <summary>"1 clase", "17 clases".</summary>
        private static string ClasesTexto(int cuantas)
        {
            return cuantas + (cuantas == 1 ? " clase" : " clases");
        }

        /// <summary>El renglon del historial de un "No tomar en cuenta", con la lista de sus clases (sin su codigo).</summary>
        private static void AnotarNoTomar(MySqlConnection conexion, MySqlTransaction transaccion, CalendarioBitacoras calendario,
            List<ClaseDelCalendario> clases, DateTime desde, DateTime hasta, string motivo, int primerId)
        {
            List<int> laboratorios = clases.Select(c => c.LaboratorioId).Distinct().ToList();
            string donde = DondeDelLote(calendario, clases);
            string fechas = HistorialCambios.Vigencia(desde, hasta);
            List<string> lista = clases.Select(c =>
            {
                string laboratorio;
                calendario.Laboratorios.TryGetValue(c.LaboratorioId, out laboratorio);
                return "- " + HistorialCambios.DiaYFecha(c.Fecha) + ", " + HistorialCambios.Horas(c.Inicio, c.Fin)
                    + (laboratorios.Count > 1 ? ", " + (laboratorio ?? "") : "") + ", " + TextoDeClase(c.Materia, c.Grupo, c.Profesor);
            }).ToList();
            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = TipoDeCambio.Cancelacion,
                Accion = AccionDeCambio.NoContar,
                Tabla = "horario_excepciones",
                RegistroId = primerId > 0 ? primerId : (int?)null,
                LaboratorioId = laboratorios.Count == 1 ? laboratorios[0] : (int?)null,
                Fecha = desde.Date,
                Descripcion = ClasesTexto(clases.Count) + " sin bitácoras " + fechas + " en " + donde
                    + (clases.Count == 1 ? " deja" : " dejan") + " de contar como faltas: se cancelan esos días." + ConMotivo(motivo),
                Antes = HistorialCambios.Datos(
                    "Laboratorio", donde,
                    "Fechas", fechas,
                    "Clases", ClasesTexto(clases.Count) + " sin bitácoras (cuentan como faltas)"),
                Despues = HistorialCambios.Datos(
                    "Laboratorio", donde,
                    "Fechas", fechas,
                    "Clases", ClasesTexto(clases.Count) + (clases.Count == 1 ? " cancelada ese día (no cuenta)" : " canceladas esos días (no cuentan)"),
                    "Motivo", motivo) + "\n" + string.Join("\n", lista)
            });
        }

        /// <summary>
        /// Cada vez que se uso "No tomar en cuenta" y sigue puesta alguna de sus
        /// cancelaciones, de la mas reciente a la mas vieja (como mucho 300).
        /// </summary>
        public List<LoteSinContar> LeerLotesSinContar()
        {
            List<LoteSinContar> lotes = new List<LoteSinContar>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.creada_en, x.creada_por, x.motivo, COUNT(*) AS clases, MIN(x.fecha) AS desde, MAX(x.fecha) AS hasta,
                       COUNT(DISTINCT h.laboratorios_idlaboratorios) AS laboratorios, MIN(l.nombre_laboratorio) AS laboratorio,
                       MAX(CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno)) AS quien
                FROM horario_excepciones x
                INNER JOIN horarios h     ON h.idhorarios = x.horarios_idhorarios
                LEFT JOIN laboratorios l  ON l.idlaboratorios = h.laboratorios_idlaboratorios
                LEFT JOIN usuarios u      ON u.idusuarios = x.creada_por
                WHERE x.tipo = 'cancelada' AND x.motivo LIKE @Marca
                GROUP BY x.creada_en, x.creada_por, x.motivo
                ORDER BY x.creada_en DESC
                LIMIT 300", conexion))
            {
                comando.Parameters.AddWithValue("@Marca", "%" + MarcaNoTomarEnCuenta);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string guardado = Texto(reader, "motivo");
                        string motivo = guardado.EndsWith(MarcaNoTomarEnCuenta, StringComparison.OrdinalIgnoreCase)
                            ? guardado.Substring(0, guardado.Length - MarcaNoTomarEnCuenta.Length).Trim() : guardado;
                        lotes.Add(new LoteSinContar
                        {
                            CreadaEn = reader.GetDateTime("creada_en"),
                            CreadaPor = reader.IsDBNull(reader.GetOrdinal("creada_por")) ? (int?)null : Convert.ToInt32(reader["creada_por"]),
                            MotivoGuardado = guardado,
                            Motivo = motivo,
                            Quien = Texto(reader, "quien").Trim(),
                            Clases = Convert.ToInt32(reader["clases"]),
                            Desde = reader.GetDateTime("desde").Date,
                            Hasta = reader.GetDateTime("hasta").Date,
                            Laboratorios = Convert.ToInt32(reader["laboratorios"]),
                            Laboratorio = Texto(reader, "laboratorio")
                        });
                    }
                }
            }

            return lotes;
        }

        /// <summary>
        /// "Volver a contar": quita, en una transaccion, las cancelaciones de ese
        /// "No tomar en cuenta" que sigan puestas (las que tienen su clave: mismo
        /// motivo, quien y creada_en; nada mas) y deja un renglon en el historial
        /// (db/21). Sus clases vuelven a salir en Faltas como antes. Si a alguna se
        /// le puso despues una sesion extra de su misma clase en sus horas (con la
        /// clase entera otra vez serian dos), no se hace nada
        /// (InvalidOperationException con que hacer). Devuelve cuantas se quitaron.
        /// </summary>
        public int VolverAContar(LoteSinContar lote)
        {
            Permisos.Exigir("Volver a contar clases que no se tomaban en cuenta");
            if (lote == null || string.IsNullOrEmpty(lote.MotivoGuardado))
            {
                throw new InvalidOperationException("Elige en la lista cuál volver a contar.");
            }

            const string DelLote = @"x.tipo = 'cancelada' AND x.motivo = @Motivo AND x.creada_en = @CreadaEn AND x.creada_por <=> @CreadaPor";
            Action<MySqlCommand> clave = comando =>
            {
                comando.Parameters.AddWithValue("@Motivo", lote.MotivoGuardado);
                comando.Parameters.AddWithValue("@CreadaEn", lote.CreadaEn);
                comando.Parameters.AddWithValue("@CreadaPor", lote.CreadaPor.HasValue ? (object)lote.CreadaPor.Value : DBNull.Value);
            };

            int quitadas;
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                // Sus cancelaciones, bloqueadas hasta el final, con lo que dice el historial.
                List<string> lista = new List<string>();
                HashSet<int> laboratorios = new HashSet<int>();
                string laboratorio = "";
                DateTime desde = DateTime.MaxValue, hasta = DateTime.MinValue;
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT x.idhorario_excepciones, x.fecha, h.hora_inicio, h.hora_fin, h.laboratorios_idlaboratorios AS lab_id,
                           l.nombre_laboratorio, m.nombre_materia, g.nombre_grupo,
                           CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor
                    FROM horario_excepciones x
                    INNER JOIN horarios h     ON h.idhorarios = x.horarios_idhorarios
                    LEFT JOIN laboratorios l  ON l.idlaboratorios = h.laboratorios_idlaboratorios
                    LEFT JOIN clases c        ON c.idclases = h.clases_idclases
                    LEFT JOIN usuarios u      ON u.idusuarios = c.usuarios_idusuarios
                    LEFT JOIN grupos g        ON g.idgrupos = c.grupos_idgrupos
                    LEFT JOIN materias m      ON m.id_materia = c.materias_id_materia
                    WHERE " + DelLote + @"
                    ORDER BY x.fecha, h.hora_inicio
                    FOR UPDATE", conexion, transaccion))
                {
                    clave(comando);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime fecha = reader.GetDateTime("fecha").Date;
                            desde = fecha < desde ? fecha : desde;
                            hasta = fecha > hasta ? fecha : hasta;
                            laboratorios.Add(Convert.ToInt32(reader["lab_id"]));
                            laboratorio = Texto(reader, "nombre_laboratorio");
                            lista.Add("- " + HistorialCambios.DiaYFecha(fecha) + ", "
                                + HistorialCambios.Horas(LeerHora(reader["hora_inicio"]) ?? TimeSpan.Zero, LeerHora(reader["hora_fin"]) ?? TimeSpan.Zero)
                                + ", " + laboratorio + ", " + TextoDeClase(Texto(reader, "nombre_materia"), Texto(reader, "nombre_grupo"), Texto(reader, "profesor")));
                        }
                    }
                }

                if (lista.Count == 0)
                {
                    throw new InvalidOperationException("Esas clases ya vuelven a contar: alguien quitó sus cancelaciones. La lista ya se volvió a leer.");
                }

                // Una sesion extra de su misma clase en sus horas ese dia, puesta despues.
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT x.fecha, r.hora_inicio, r.hora_fin, m.nombre_materia, g.nombre_grupo
                    FROM horario_excepciones x
                    INNER JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
                    INNER JOIN horario_excepciones r
                            ON r.tipo = 'extra' AND r.clases_idclases = h.clases_idclases AND r.laboratorios_idlaboratorios = h.laboratorios_idlaboratorios
                           AND r.fecha = x.fecha AND r.hora_inicio < h.hora_fin AND r.hora_fin > h.hora_inicio AND r.creada_en >= x.creada_en
                    LEFT JOIN clases c   ON c.idclases = h.clases_idclases
                    LEFT JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    LEFT JOIN materias m ON m.id_materia = c.materias_id_materia
                    WHERE " + DelLote + @"
                    ORDER BY x.fecha, r.hora_inicio
                    LIMIT 1", conexion, transaccion))
                {
                    clave(comando);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            throw new InvalidOperationException("No se cambió nada: el " + HistorialCambios.DiaYFecha(reader.GetDateTime("fecha"))
                                + ", " + Texto(reader, "nombre_materia") + " - " + Texto(reader, "nombre_grupo") + " tiene ahora una sesión extra de "
                                + HistorialCambios.Horas(LeerHora(reader["hora_inicio"]) ?? TimeSpan.Zero, LeerHora(reader["hora_fin"]) ?? TimeSpan.Zero)
                                + " en sus horas, y con la clase entera serían dos. Quita antes esa sesión extra en Cambios de un día y vuelve a intentarlo.");
                        }
                    }
                }

                using (MySqlCommand comando = new MySqlCommand("DELETE x FROM horario_excepciones x WHERE " + DelLote, conexion, transaccion))
                {
                    clave(comando);
                    quitadas = comando.ExecuteNonQuery();
                }

                if (quitadas != lista.Count)
                {
                    throw new InvalidOperationException("No se cambió nada: sus cancelaciones cambiaron mientras tanto. Vuelve a intentarlo.");
                }

                if (HistorialCambios.Activo(conexion, transaccion))
                {
                    string donde = laboratorios.Count == 1 ? laboratorio : laboratorios.Count + " laboratorios";
                    string fechas = HistorialCambios.Vigencia(desde, hasta);
                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.Cancelacion,
                        Accion = AccionDeCambio.VolverAContar,
                        Tabla = "horario_excepciones",
                        LaboratorioId = laboratorios.Count == 1 ? laboratorios.First() : (int?)null,
                        Fecha = desde,
                        Descripcion = (quitadas == 1 ? "Vuelve a contar 1 clase" : "Vuelven a contar " + quitadas + " clases") + " que no se tomaba"
                            + (quitadas == 1 ? "" : "n") + " en cuenta (" + fechas + " en " + donde + "): se quita" + (quitadas == 1 ? " su cancelación." : "n sus cancelaciones.")
                            + ConMotivo(lote.Motivo),
                        Antes = HistorialCambios.Datos(
                            "Laboratorio", donde,
                            "Fechas", fechas,
                            "Clases", ClasesTexto(quitadas) + (quitadas == 1 ? " cancelada (no cuenta)" : " canceladas (no cuentan)"),
                            "Motivo", HistorialCambios.Limpiar(lote.Motivo, 200),
                            "Se puso", "el " + HistorialCambios.Fecha(lote.CreadaEn) + " a las " + lote.CreadaEn.ToString("HH:mm", CultureInfo.InvariantCulture))
                            + "\n" + string.Join("\n", lista),
                        Despues = HistorialCambios.Datos(
                            "Clases", ClasesTexto(quitadas) + (quitadas == 1 ? " vuelve a contar como antes" : " vuelven a contar como antes"))
                    });
                }

                transaccion.Commit();
            }

            Log.Info("Volver a contar: " + quitadas + " cancelaciones de un \"No tomar en cuenta\" del "
                + lote.CreadaEn.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " quitadas");
            return quitadas;
        }

        // ---------------------------------------------------------------------
        // Historial de cambios (db/21): lo que se anota de cada cambio, en la
        // misma transaccion y con lo que se lee en ella. Sin db/21 no se llega
        // aqui (HistorialCambios.Activo). Nunca el codigo de la clase: el
        // encargado de laboratorio tambien lee el historial.
        // ---------------------------------------------------------------------

        /// <summary>Una franja como se describe en el historial: su clase, laboratorio, dia, horas y vigencia.</summary>
        private sealed class FotoFranja
        {
            public int Id;
            public int ClaseId;
            public int LaboratorioId;
            public int DiaSemana;
            public TimeSpan Inicio;
            public TimeSpan Fin;
            public DateTime? FranjaDesde;
            public DateTime? FranjaHasta;
            public DateTime ClaseDesde;
            public DateTime ClaseHasta;
            /// <summary>"Redes - 5A (Ana Ruiz)".</summary>
            public string Clase = "";
            public string Laboratorio = "";

            /// <summary>"martes de 07:00 a 09:00 en Laboratorio 1".</summary>
            public string Donde
            {
                get { return NombreDia(DiaSemana).ToLowerInvariant() + " de " + HistorialCambios.Horas(Inicio, Fin) + " en " + Laboratorio; }
            }

            /// <summary>Un dato por linea; la vigencia, la de verdad (VigenciaFranjas: la de su clase recortada a la suya).</summary>
            public string Datos()
            {
                DateTime desde = FranjaDesde.HasValue && FranjaDesde.Value > ClaseDesde ? FranjaDesde.Value : ClaseDesde;
                DateTime hasta = FranjaHasta.HasValue && FranjaHasta.Value < ClaseHasta ? FranjaHasta.Value : ClaseHasta;
                return HistorialCambios.Datos(
                    "Clase", Clase,
                    "Laboratorio", Laboratorio,
                    "Día", NombreDia(DiaSemana),
                    "Horario", HistorialCambios.Horas(Inicio, Fin),
                    "Vigencia", HistorialCambios.Vigencia(desde, hasta) + (FranjaDesde.HasValue || FranjaHasta.HasValue ? "" : " (la de su clase)"));
            }
        }

        /// <summary>Como esta ahora una franja (dentro de la transaccion), o null si ya no esta.</summary>
        private static FotoFranja FotoDeFranja(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja)
        {
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT h.idhorarios, h.clases_idclases, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin,
                       c.vigente_desde AS clase_desde, c.vigente_hasta AS clase_hasta,
                       " + VigenciaFranjas.Columnas(conVigencia) + @",
                       m.nombre_materia, g.nombre_grupo, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       l.nombre_laboratorio
                FROM horarios h
                INNER JOIN clases c      ON c.idclases = h.clases_idclases
                LEFT JOIN usuarios u     ON u.idusuarios = c.usuarios_idusuarios
                LEFT JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                LEFT JOIN materias m     ON m.id_materia = c.materias_id_materia
                LEFT JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                WHERE h.idhorarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new FotoFranja
                    {
                        Id = reader.GetInt32("idhorarios"),
                        ClaseId = reader.GetInt32("clases_idclases"),
                        LaboratorioId = reader.GetInt32("laboratorios_idlaboratorios"),
                        DiaSemana = reader.GetInt32("dia_semana"),
                        Inicio = LeerHora(reader["hora_inicio"]) ?? TimeSpan.Zero,
                        Fin = LeerHora(reader["hora_fin"]) ?? TimeSpan.Zero,
                        FranjaDesde = Fecha(reader, "franja_desde"),
                        FranjaHasta = Fecha(reader, "franja_hasta"),
                        ClaseDesde = reader.GetDateTime("clase_desde").Date,
                        ClaseHasta = reader.GetDateTime("clase_hasta").Date,
                        Clase = TextoDeClase(Texto(reader, "nombre_materia"), Texto(reader, "nombre_grupo"), Texto(reader, "profesor")),
                        Laboratorio = Texto(reader, "nombre_laboratorio")
                    };
                }
            }
        }

        /// <summary>Un renglon de una franja: de como era (antes) a como quedo (despues); null en un alta o al borrarla.</summary>
        private static void AnotarFranja(MySqlConnection conexion, MySqlTransaction transaccion, string accion, int idFranja,
            FotoFranja antes, FotoFranja despues, DateTime? fecha, string descripcion)
        {
            FotoFranja cual = despues ?? antes;
            if (cual == null || descripcion == null)
            {
                return;
            }

            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = TipoDeCambio.Franja,
                Accion = accion,
                Tabla = "horarios",
                RegistroId = idFranja,
                LaboratorioId = cual.LaboratorioId,
                Fecha = fecha,
                Descripcion = descripcion,
                Antes = antes == null ? null : antes.Datos(),
                Despues = despues == null ? null : despues.Datos()
            });
        }

        /// <summary>
        /// Un cambio en su sitio (ActualizarFranja, o CambiarFranjaDesde sin partirla):
        /// de como era a como quedo, y si alcanzo a sus semanas pasadas. Nada si
        /// no cambio nada (o sin historial: antes = null).
        /// </summary>
        private static void AnotarFranjaCambiada(MySqlConnection conexion, MySqlTransaction transaccion, int idFranja, FotoFranja antes, bool conHistoria,
            string porque = null)
        {
            if (antes == null)
            {
                return;
            }

            FotoFranja despues = FotoDeFranja(conexion, transaccion, idFranja);
            if (despues == null || despues.Datos() == antes.Datos())
            {
                return;
            }

            AnotarFranja(conexion, transaccion, AccionDeCambio.Cambio, idFranja, antes, despues, null,
                (antes.ClaseId == despues.ClaseId ? despues.Clase + ": de " + antes.Donde + " a " + despues.Donde
                    : "De " + antes.Clase + ", " + antes.Donde + ", a " + despues.Clase + ", " + despues.Donde)
                + (conHistoria ? " (también en las semanas que ya pasaron)" : "") + "." + (porque == null ? "" : " " + porque));
        }

        /// <summary>Una cancelacion o una sesion extra como se describe en el historial.</summary>
        private sealed class FotoExcepcion
        {
            public int Id;
            public bool EsExtra;
            public DateTime Fecha;
            public int LaboratorioId;
            public TimeSpan Inicio;
            public TimeSpan Fin;
            public string Motivo = "";
            /// <summary>"Redes - 5A (Ana Ruiz)".</summary>
            public string Clase = "";
            public string Laboratorio = "";

            /// <summary>"martes 13/10/2026 de 07:00 a 09:00 en Laboratorio 1".</summary>
            public string Cuando
            {
                get { return HistorialCambios.DiaYFecha(Fecha) + " de " + HistorialCambios.Horas(Inicio, Fin) + " en " + Laboratorio; }
            }

            public string Datos()
            {
                return HistorialCambios.Datos(
                    "Clase", Clase,
                    "Día", HistorialCambios.DiaYFecha(Fecha),
                    "Horario", HistorialCambios.Horas(Inicio, Fin),
                    "Laboratorio", Laboratorio,
                    "Motivo", Motivo.Length > 0 ? Motivo : null);
            }
        }

        /// <summary>
        /// Como esta ahora una cancelacion (con la clase, el laboratorio y las horas
        /// de su franja) o una sesion extra, o null si ya no esta.
        /// </summary>
        private static FotoExcepcion FotoDeExcepcion(MySqlConnection conexion, MySqlTransaction transaccion, int idExcepcion)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.idhorario_excepciones, x.tipo, x.fecha, x.motivo,
                       COALESCE(x.laboratorios_idlaboratorios, h.laboratorios_idlaboratorios) AS lab_id,
                       COALESCE(x.hora_inicio, h.hora_inicio) AS inicio, COALESCE(x.hora_fin, h.hora_fin) AS fin,
                       m.nombre_materia, g.nombre_grupo, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       l.nombre_laboratorio
                FROM horario_excepciones x
                LEFT JOIN horarios h     ON h.idhorarios = x.horarios_idhorarios
                LEFT JOIN clases c       ON c.idclases = COALESCE(x.clases_idclases, h.clases_idclases)
                LEFT JOIN usuarios u     ON u.idusuarios = c.usuarios_idusuarios
                LEFT JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                LEFT JOIN materias m     ON m.id_materia = c.materias_id_materia
                LEFT JOIN laboratorios l ON l.idlaboratorios = COALESCE(x.laboratorios_idlaboratorios, h.laboratorios_idlaboratorios)
                WHERE x.idhorario_excepciones = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idExcepcion);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new FotoExcepcion
                    {
                        Id = reader.GetInt32("idhorario_excepciones"),
                        EsExtra = Texto(reader, "tipo") == "extra",
                        Fecha = reader.GetDateTime("fecha").Date,
                        LaboratorioId = reader["lab_id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["lab_id"]),
                        Inicio = LeerHora(reader["inicio"]) ?? TimeSpan.Zero,
                        Fin = LeerHora(reader["fin"]) ?? TimeSpan.Zero,
                        // Sin lo que la tabla del historial no guarda (emojis): sin un espacio suelto al final.
                        Motivo = HistorialCambios.Limpiar(Texto(reader, "motivo"), 200) ?? "",
                        Clase = TextoDeClase(Texto(reader, "nombre_materia"), Texto(reader, "nombre_grupo"), Texto(reader, "profesor")),
                        Laboratorio = Texto(reader, "nombre_laboratorio")
                    };
                }
            }
        }

        private static void AnotarExcepcion(MySqlConnection conexion, MySqlTransaction transaccion, string accion,
            FotoExcepcion antes, FotoExcepcion despues, string descripcion)
        {
            FotoExcepcion cual = despues ?? antes;
            if (cual == null)
            {
                return;
            }

            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = cual.EsExtra ? TipoDeCambio.Extra : TipoDeCambio.Cancelacion,
                Accion = accion,
                Tabla = "horario_excepciones",
                RegistroId = cual.Id,
                LaboratorioId = cual.LaboratorioId,
                Fecha = cual.Fecha,
                Descripcion = descripcion,
                Antes = antes == null ? null : antes.Datos(),
                Despues = despues == null ? null : despues.Datos()
            });
        }

        /// <summary>Una cancelacion o una sesion extra recien puesta (ya con su id), con su motivo. Sin historial, nada.</summary>
        private static void AnotarExcepcionNueva(MySqlConnection conexion, MySqlTransaction transaccion, int idExcepcion)
        {
            if (!HistorialCambios.Activo(conexion, transaccion))
            {
                return;
            }

            FotoExcepcion nueva = FotoDeExcepcion(conexion, transaccion, idExcepcion);
            if (nueva != null)
            {
                AnotarExcepcion(conexion, transaccion, AccionDeCambio.Alta, null, nueva,
                    (nueva.EsExtra ? nueva.Clase + ": sesión extra el " + nueva.Cuando : nueva.Clase + " no se da el " + nueva.Cuando)
                    + "." + ConMotivo(nueva.Motivo));
            }
        }

        /// <summary>
        /// Una clase o una sesion extra que toca una excepcion (AplicarExcepcion):
        /// cancelada o quitada entera, o recortada (lo que le queda va en su mismo
        /// renglon: los restos no tienen renglon propio). Con lo que ya se leyo.
        /// </summary>
        private static void AnotarTramo(MySqlConnection conexion, MySqlTransaction transaccion, string tipo, string accion, int idExcepcion,
            int idLaboratorio, string laboratorio, DateTime fecha, TramoDeClase tramo, TimeSpan inicio, TimeSpan fin, List<TramoHorario> restos, string motivo)
        {
            bool extra = tipo == TipoDeCambio.Extra;
            string clase = TextoDeClase(tramo.Materia, tramo.Grupo, tramo.Profesor);
            string dia = HistorialCambios.DiaYFecha(fecha);
            string horario = HistorialCambios.Horas(tramo.Inicio, tramo.Fin);
            string queda = string.Join(" y ", restos.Select(r => HistorialCambios.Horas(r.Inicio, r.Fin)));
            string descripcion;
            if (restos.Count == 0)
            {
                descripcion = extra
                    ? clase + ": se quita su sesión extra del " + dia + " de " + horario + " en " + laboratorio + "."
                    : clase + " no se da el " + dia + " de " + horario + " en " + laboratorio + ".";
            }
            else
            {
                TramoHorario cruce = Cruce(tramo.Inicio, tramo.Fin, inicio, fin);
                descripcion = clase + (extra ? ", sesión extra del " : ", ") + dia + " en " + laboratorio + ": se le quita de "
                    + HistorialCambios.Horas(cruce.Inicio, cruce.Fin) + "; " + (extra ? "queda de " : "ese día da de ")
                    + string.Join(" y de ", restos.Select(r => HistorialCambios.Horas(r.Inicio, r.Fin))) + ".";
            }

            string motivoExtra = extra ? HistorialCambios.Limpiar(tramo.Motivo, 200) : null;
            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = tipo,
                Accion = accion,
                Tabla = "horario_excepciones",
                RegistroId = idExcepcion,
                LaboratorioId = idLaboratorio,
                Fecha = fecha,
                Descripcion = descripcion + ConMotivo(motivo),
                Antes = HistorialCambios.Datos("Clase", clase, "Día", dia, "Horario", horario, "Laboratorio", laboratorio, "Motivo", motivoExtra),
                Despues = extra && restos.Count == 0 ? null
                    : HistorialCambios.Datos("Clase", clase, "Día", dia, "Horario", restos.Count == 0 ? "no se da (cancelada)" : queda,
                        "Laboratorio", laboratorio, "Motivo", extra ? motivoExtra : HistorialCambios.Limpiar(motivo, 200))
            });
        }

        /// <summary>" Motivo: dia festivo." o nada (sin emojis: con uno al final quedaba "festivo .").</summary>
        private static string ConMotivo(string motivo)
        {
            string limpio = HistorialCambios.Limpiar(motivo, 200);
            return limpio == null ? "" : " Motivo: " + limpio.TrimEnd('.', ' ') + ".";
        }

        /// <summary>
        /// Un renglon de una clase con como quedo (y como era, si se da antes).
        /// Sin detalle: un alta o un cambio de estado dicen su vigencia; un cambio
        /// dice que cambio, y si no cambio nada no se anota. Tambien para quien
        /// cambia clases fuera de Horarios en su transaccion (la baja de un
        /// profesor, su codigo): antes, con LeerClaseEn, y solo con
        /// HistorialCambios.Activo.
        /// </summary>
        internal static void AnotarClase(MySqlConnection conexion, MySqlTransaction transaccion, string accion, int idClase, ClaseInfo antes, string detalle)
        {
            ClaseInfo ahora = LeerClaseEn(conexion, transaccion, idClase);
            if (ahora == null)
            {
                return;
            }

            string datosAntes = antes == null ? null : DatosDeClase(antes);
            string datosAhora = DatosDeClase(ahora);
            if (detalle == null && accion == AccionDeCambio.Cambio && antes != null)
            {
                List<string> cambia = HistorialCambios.QueCambia(datosAntes, datosAhora);
                // El codigo no se escribe (lo lee el encargado): solo que cambio.
                if (!string.Equals(antes.Codigo, ahora.Codigo, StringComparison.Ordinal))
                {
                    cambia.Add("Código");
                }

                if (cambia.Count == 0)
                {
                    return;
                }

                // "Docente" con su articulo seria "el docente": se dice quien la imparte.
                detalle = ": cambia " + HistorialCambios.Enumerar(cambia
                    .Select(campo => campo == CampoDocente ? "quién la imparte" : HistorialCambios.ConArticulo(campo)).ToList());
            }
            else if (detalle == null)
            {
                detalle = ", " + HistorialCambios.Vigencia(ahora.VigenteDesde, ahora.VigenteHasta);
            }

            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = TipoDeCambio.Clase,
                Accion = accion,
                Tabla = "clases",
                RegistroId = idClase,
                Descripcion = TextoDeClase(ahora.Materia, ahora.Grupo, ahora.Profesor) + detalle + ".",
                Antes = datosAntes,
                Despues = datosAhora
            });
        }

        /// <summary>"Redes - 5A (Ana Ruiz)": una clase sin su codigo.</summary>
        private static string TextoDeClase(string materia, string grupo, string profesor)
        {
            string texto = (materia ?? "").Trim() + " - " + (grupo ?? "").Trim();
            return string.IsNullOrWhiteSpace(profesor) ? texto : texto + " (" + profesor.Trim() + ")";
        }

        /// <summary>El campo de quien imparte la clase en el historial (DatosDeClase); los renglones de antes dicen "Profesor".</summary>
        private const string CampoDocente = "Docente";

        private static string DatosDeClase(ClaseInfo clase)
        {
            return HistorialCambios.Datos(
                "Materia", clase.Materia,
                "Grupo", clase.Grupo,
                CampoDocente, clase.Profesor,
                "Vigencia", HistorialCambios.Vigencia(clase.VigenteDesde, clase.VigenteHasta),
                "Activa", HistorialCambios.SiNo(clase.Activa));
        }

        private static string NombreDeLaboratorio(MySqlConnection conexion, MySqlTransaction transaccion, int idLaboratorio)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT nombre_laboratorio FROM laboratorios WHERE idlaboratorios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idLaboratorio);
                object nombre = comando.ExecuteScalar();
                return nombre == null || nombre == DBNull.Value ? "el laboratorio " + idLaboratorio : Convert.ToString(nombre);
            }
        }

        /// <summary>"Martes" (1 = lunes ... 7 = domingo); "" si el numero no es un dia.</summary>
        private static string NombreDia(int diaSemana)
        {
            return diaSemana >= 1 && diaSemana <= 7 ? NombresDia[diaSemana] : "";
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
        /// <param name="paraElAlumno">
        /// Solo lo que el servidor acepta: tambien las extras de clases activas y
        /// vigentes, y sin los profesores dados de baja (db/08). Es lo que ve y
        /// elige el alumno; la vista del administrador lo ensena todo.
        /// </param>
        private static List<SesionAgenda> LeerAgenda(MySqlConnection conexion, int? idLaboratorio, DateTime fecha, bool paraElAlumno = false)
        {
            string filtroFranja = idLaboratorio.HasValue ? "h.laboratorios_idlaboratorios = @Lab AND " : "";
            string filtroExtra = idLaboratorio.HasValue ? " AND x.laboratorios_idlaboratorios = @Lab" : "";
            string orden = idLaboratorio.HasValue ? "inicio" : "laboratorio_id, inicio";
            bool conBajas = Esquema.TieneBajas(conexion);
            string profesorActivo = paraElAlumno && conBajas ? " AND u.activo = 1" : "";
            string extraVigente = paraElAlumno ? " AND c.activa = 1 AND @Fecha BETWEEN c.vigente_desde AND c.vigente_hasta" : "";
            // Lo que el servidor deja abrir (SesionAgenda.Utilizable); de las franjas
            // ya vienen solo las que se dan ese dia (clase activa y vigente, y con
            // db/20 tambien la vigencia de la franja: VigenciaFranjas.SeDa).
            string profesorUtil = conBajas ? "u.activo = 1" : "1 = 1";
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
            // Lo de un laboratorio deshabilitado no corre: ni para el alumno ni para su docente.
            string franjaHabilitada = LaboratoriosDeshabilitados.SinDeshabilitados(conexion, "h.laboratorios_idlaboratorios");
            string extraHabilitada = LaboratoriosDeshabilitados.SinDeshabilitados(conexion, "x.laboratorios_idlaboratorios");

            List<SesionAgenda> lista = new List<SesionAgenda>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT 0 AS es_extra, h.idhorarios AS franja_id, NULL AS excepcion_id, c.idclases, c.codigo,
                       c.usuarios_idusuarios AS profesor_id,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       g.nombre_grupo, m.nombre_materia, h.laboratorios_idlaboratorios AS laboratorio_id,
                       h.hora_inicio AS inicio, h.hora_fin AS fin,
                       (x.idhorario_excepciones IS NOT NULL) AS cancelada, x.motivo,
                       c.materias_id_materia AS materia_id, c.grupos_idgrupos AS grupo_id,
                       (" + profesorUtil + @") AS utilizable, NULL AS creada_en
                FROM horarios h
                INNER JOIN clases c   ON c.idclases = h.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                LEFT JOIN horario_excepciones x ON x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios AND x.fecha = @Fecha
                WHERE " + filtroFranja + @"h.dia_semana = WEEKDAY(@Fecha) + 1
                  AND " + VigenciaFranjas.SeDa(conVigencia, "@Fecha") + profesorActivo + franjaHabilitada + @"
                UNION ALL
                SELECT 1, NULL, x.idhorario_excepciones, c.idclases, c.codigo,
                       c.usuarios_idusuarios,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno),
                       g.nombre_grupo, m.nombre_materia, x.laboratorios_idlaboratorios,
                       x.hora_inicio, x.hora_fin, 0, x.motivo,
                       c.materias_id_materia, c.grupos_idgrupos,
                       (c.activa = 1 AND @Fecha BETWEEN c.vigente_desde AND c.vigente_hasta AND " + profesorUtil + @"), x.creada_en
                FROM horario_excepciones x
                INNER JOIN clases c   ON c.idclases = x.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                WHERE x.tipo = 'extra'" + filtroExtra + @" AND x.fecha = @Fecha" + extraVigente + profesorActivo + extraHabilitada + @"
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
                            LaboratorioId = reader.IsDBNull(reader.GetOrdinal("laboratorio_id")) ? 0 : Convert.ToInt32(reader["laboratorio_id"]),
                            MateriaId = Convert.ToInt32(reader["materia_id"]),
                            GrupoId = Convert.ToInt32(reader["grupo_id"]),
                            Utilizable = !reader.IsDBNull(reader.GetOrdinal("utilizable")) && Convert.ToInt32(reader["utilizable"]) == 1,
                            CreadaEn = reader.IsDBNull(reader.GetOrdinal("creada_en")) ? (DateTime?)null : Convert.ToDateTime(reader["creada_en"], CultureInfo.InvariantCulture)
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
        /// consultas. Sin fecha, el dia de hoy segun el servidor. Con
        /// codigosParaDar (el panel en vivo) y "Los docentes no ven los codigos",
        /// tambien el codigo de cada clase (SesionAgenda.CodigoParaDar) para quien
        /// lo ve (la administracion y la persona responsable del laboratorio).
        /// </summary>
        public PanoramaLaboratorios PanoramaDelDia(DateTime? fecha = null, bool codigosParaDar = false)
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
                panorama.OrdenDeProfesores = LeerOrdenDeProfesores(conexion);
                panorama.ColoresElegidos = LeerColoresElegidos(conexion);
                panorama.ConModos = Esquema.ModoDeAcceso(conexion);
                panorama.Modos = ModosDeAcceso.LeerTodos(conexion);
                // La forma general y "Los docentes no ven los codigos", en una consulta.
                AjustesDeCodigos ajustes = ModosDeAcceso.LeerAjustes(conexion);
                panorama.GeneralGuardada = panorama.ConModos ? ajustes.General : null;
                // Si no se pudo leer (error de esquema), lo ultimo que se supo.
                panorama.CodigosSoloResponsables = ajustes.SoloResponsables ?? CodigosSoloResponsables.Activa;
                AnotarSesiones(conexion, panorama.Agenda, panorama.Fecha, null, true);
                if (codigosParaDar && panorama.CodigosSoloResponsables && Permisos.VeCodigosDeProfesor)
                {
                    AnotarCodigosParaDar(conexion, panorama);
                }

                // Los equipos que sus alumnos avisaron sin internet (db/10). Por
                // las sesiones ya encontradas (indice de la clave foranea), no por
                // fecha: codigos_accesos.fecha no tiene indice y esto va cada 30 s.
                List<int> sesiones = panorama.Agenda.Where(s => s.SesionId.HasValue).Select(s => s.SesionId.Value).Distinct().ToList();
                if (sesiones.Count > 0)
                {
                    Dictionary<int, List<string>> avisos = Consultas.LeerAvisosSinInternet(conexion,
                        "rb.fk_codigo_accesos IN (" + string.Join(",", sesiones) + ")", null);
                    foreach (SesionAgenda entrada in panorama.Agenda.Where(s => s.SesionId.HasValue && avisos.ContainsKey(s.SesionId.Value)))
                    {
                        entrada.SinInternet = Consultas.ContarSinInternet(avisos[entrada.SesionId.Value]);
                    }
                }

                // Las asistencias que dio cada profesor a quien no pudo registrar
                // (db/22): el panel en vivo, las alertas y el panel del profesor
                // las dicen. Tambien por las sesiones ya encontradas. Sin la 22, nada.
                panorama.ConDadas = AnotarDadas(conexion, panorama.Agenda);
            }

            return panorama;
        }

        // Leer los codigos generados para el panel fallo por algo que no es la red:
        // se repetiria cada 30 s; al registro, solo la primera vez.
        private static bool codigosParaDarFallo;

        /// <summary>
        /// Con "Los docentes no ven los codigos": a cada clase del dia sin cancelar,
        /// que se puede usar y de un laboratorio con codigo, el que se le da al
        /// grupo (SesionAgenda.CodigoParaDar): el de su sesion si ya la tiene; si
        /// no, el fijo de la clase o, con la generacion de codigos, el generado
        /// (CodigosGenerados.DelDiaDe, dos consultas para todos). Si los generados
        /// no se pueden leer (sin secreto), esas clases se quedan sin el; nunca
        /// lanza por eso, salvo sin conexion, como el resto del panorama.
        /// </summary>
        private static void AnotarCodigosParaDar(MySqlConnection conexion, PanoramaLaboratorios panorama)
        {
            List<SesionAgenda> conCodigo = panorama.Agenda
                .Where(s => !s.Cancelada && s.Utilizable && panorama.ModoDe(s.LaboratorioId) != ModoCodigo.SinCodigo).ToList();
            List<int> sinSesion = conCodigo
                .Where(s => string.IsNullOrEmpty(s.CodigoSesion) && panorama.ModoDe(s.LaboratorioId) == ModoCodigo.Renovable)
                .Select(s => s.LaboratorioId).Distinct().ToList();
            Dictionary<int, Dictionary<string, string>> generados = null;
            if (sinSesion.Count > 0)
            {
                try
                {
                    generados = CodigosGenerados.DelDiaDe(conexion, panorama.Fecha, sinSesion);
                }
                catch (Exception ex) when (ColaBitacoras.Clasificar(ex) != ClaseDeFallo.SinConexion)
                {
                    if (!codigosParaDarFallo)
                    {
                        codigosParaDarFallo = true;
                        Log.Error("Panel en vivo: no se pudieron calcular los codigos generados de hoy", ex);
                    }
                }
            }

            foreach (SesionAgenda s in conCodigo)
            {
                Dictionary<string, string> delLaboratorio;
                string generado;
                s.CodigoParaDar = !string.IsNullOrEmpty(s.CodigoSesion) ? s.CodigoSesion
                    : panorama.ModoDe(s.LaboratorioId) == ModoCodigo.Fijo ? (string.IsNullOrEmpty(s.Codigo) ? null : s.Codigo)
                    : generados != null && generados.TryGetValue(s.LaboratorioId, out delLaboratorio)
                        && delLaboratorio.TryGetValue(CodigosGenerados.Clave(s.EsExtra, (s.EsExtra ? s.ExcepcionId : s.FranjaId) ?? 0), out generado)
                        ? generado : null;
            }
        }

        /// <summary>
        /// A cada entrada con sesion, las asistencias que dio su profesor en ella
        /// (AsistenciasDadas.ContarPorSesion). Devuelve si la base tiene db/22 (y
        /// esta cuenta la ve), o null si no hizo falta preguntarlo: ninguna entrada
        /// tiene sesion.
        /// </summary>
        private static bool? AnotarDadas(MySqlConnection conexion, List<SesionAgenda> agenda)
        {
            List<SesionAgenda> conSesion = agenda.Where(s => s.SesionId.HasValue).ToList();
            if (conSesion.Count == 0)
            {
                return null;
            }

            if (!Esquema.AsistenciaDada(conexion))
            {
                return false;
            }

            Dictionary<int, int> dadas = AsistenciasDadas.ContarPorSesion(conexion, conSesion.Select(s => s.SesionId.Value));
            foreach (SesionAgenda s in conSesion)
            {
                int cuantas;
                s.AsistenciasDadas = dadas.TryGetValue(s.SesionId.Value, out cuantas) ? cuantas : 0;
            }

            return true;
        }

        /// <summary>
        /// A cada clase de la agenda, su sesion de ese dia si ya existe: el id, su
        /// codigo y cuantos alumnos registraron (y cuantos con falla, el criterio
        /// de la pantalla de bitacoras). La sesion de una franja es la de su dia;
        /// la de una sesion extra, la de su excepcion (codigos_accesos, db/01).
        /// </summary>
        /// <param name="deClasesTerminadas">
        /// Tambien lo que Faltas le cuenta a una clase sin sesion ni nada en ella:
        /// la sesion de la clase de antes de su terna (EsDeLaClaseDeAntes), en
        /// BitacorasEnOtra y EnOtra. Solo para lo que se ensena (los paneles y las
        /// alertas): lo que abre o acepta una clase (el alumno, el codigo generado)
        /// mira su sesion propia.
        /// </param>
        private static void AnotarSesiones(MySqlConnection conexion, List<SesionAgenda> agenda, DateTime fecha, int? idLaboratorio, bool deClasesTerminadas = false)
        {
            List<SesionAgenda> deAntes = new List<SesionAgenda>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.idcodigos_accesos, ca.horarios_idhorarios, ca.horario_excepciones_id, ca.codigo,
                       COUNT(DISTINCT rb.fk_usuario) AS alumnos,
                       COUNT(DISTINCT CASE WHEN " + Consultas.CondicionFallaPendiente(conexion) + @" THEN rb.fk_usuario END) AS con_fallas"
                + (deClasesTerminadas ? @",
                       ca.laboratorios_idlaboratorios AS lab, ca.usuarios_idusuarios AS profesor, ca.materias_id_materia AS materia,
                       ca.grupos_idgrupos AS grupo, ca.hora_entrada, ca.hora_salida,
                       MAX(cs.vigente_hasta < ca.fecha) AS de_terminada" : "") + @"
                FROM codigos_accesos ca
                LEFT JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos"
                + (deClasesTerminadas ? SesionConSuClase : "") + @"
                WHERE ca.fecha = @Fecha" + (idLaboratorio.HasValue ? " AND ca.laboratorios_idlaboratorios = @Lab" : "") + @"
                  AND (ca.horarios_idhorarios IS NOT NULL OR ca.horario_excepciones_id IS NOT NULL)
                GROUP BY ca.idcodigos_accesos, ca.horarios_idhorarios, ca.horario_excepciones_id, ca.codigo"
                + (deClasesTerminadas ? ", ca.laboratorios_idlaboratorios, ca.usuarios_idusuarios, ca.materias_id_materia, ca.grupos_idgrupos, ca.hora_entrada, ca.hora_salida" : ""), conexion))
            {
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                if (idLaboratorio.HasValue)
                {
                    comando.Parameters.AddWithValue("@Lab", idLaboratorio.Value);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int? franja = reader.IsDBNull(reader.GetOrdinal("horarios_idhorarios")) ? (int?)null : Convert.ToInt32(reader["horarios_idhorarios"]);
                        int? excepcion = reader.IsDBNull(reader.GetOrdinal("horario_excepciones_id")) ? (int?)null : Convert.ToInt32(reader["horario_excepciones_id"]);
                        SesionAgenda entrada = agenda.FirstOrDefault(s => s.EsExtra
                            ? excepcion.HasValue && s.ExcepcionId == excepcion
                            : franja.HasValue && s.FranjaId == franja);
                        if (entrada == null)
                        {
                            if (deClasesTerminadas && !reader.IsDBNull(reader.GetOrdinal("de_terminada")) && Convert.ToInt32(reader["de_terminada"]) == 1
                                && !reader.IsDBNull(reader.GetOrdinal("materia")) && !reader.IsDBNull(reader.GetOrdinal("grupo")) && !reader.IsDBNull(reader.GetOrdinal("profesor")))
                            {
                                TimeSpan? inicio = LeerHora(reader["hora_entrada"]);
                                TimeSpan? fin = LeerHora(reader["hora_salida"]);
                                if (inicio.HasValue && fin.HasValue)
                                {
                                    deAntes.Add(new SesionAgenda
                                    {
                                        SesionId = Convert.ToInt32(reader["idcodigos_accesos"]),
                                        CodigoSesion = Convert.ToString(reader["codigo"], CultureInfo.InvariantCulture),
                                        FranjaId = franja,
                                        ExcepcionId = excepcion,
                                        EsExtra = excepcion.HasValue,
                                        LaboratorioId = reader.IsDBNull(reader.GetOrdinal("lab")) ? 0 : Convert.ToInt32(reader["lab"]),
                                        ProfesorId = Convert.ToInt32(reader["profesor"]),
                                        MateriaId = Convert.ToInt32(reader["materia"]),
                                        GrupoId = Convert.ToInt32(reader["grupo"]),
                                        Inicio = inicio.Value,
                                        Fin = fin.Value,
                                        Alumnos = Convert.ToInt32(reader["alumnos"]),
                                        ConFallas = Convert.ToInt32(reader["con_fallas"])
                                    });
                                }
                            }

                            continue;
                        }

                        entrada.SesionId = Convert.ToInt32(reader["idcodigos_accesos"]);
                        entrada.CodigoSesion = Convert.ToString(reader["codigo"], CultureInfo.InvariantCulture);
                        entrada.Alumnos = Convert.ToInt32(reader["alumnos"]);
                        entrada.ConFallas = Convert.ToInt32(reader["con_fallas"]);
                    }
                }
            }

            if (deAntes.Count > 0)
            {
                AnotarLasDeLaClaseDeAntes(conexion, agenda, deAntes);
            }
        }

        /// <summary>
        /// JOIN de una sesion (alias ca) con la clase de la franja o la extra a la
        /// que esta enlazada (alias cs). Si su dia es posterior al ultimo de esa
        /// clase, es EsDeLaClaseDeAntes: con los datos de siempre no pasa, porque una
        /// clase no se puede terminar antes de su ultima sesion.
        /// </summary>
        private const string SesionConSuClase = @"
                LEFT JOIN horarios hs            ON hs.idhorarios = ca.horarios_idhorarios
                LEFT JOIN horario_excepciones xs ON xs.idhorario_excepciones = ca.horario_excepciones_id
                LEFT JOIN clases cs              ON cs.idclases = COALESCE(hs.clases_idclases, xs.clases_idclases)";

        /// <summary>
        /// La regla comun de EmparejarSesiones y AnotarSesiones (panel en vivo, alertas
        /// y panel del docente): una sesion enlazada
        /// a una clase que termino antes de su dia (solo pasa con "Ahora mismo":
        /// lo registrado hoy antes de aplicar, en una clase que termino ayer) es de
        /// la clase de ese dia de su misma persona docente, grupo y materia cuyas
        /// horas se crucen, aunque esa este en otro laboratorio.
        /// </summary>
        private static bool EsDeLaClaseDeAntes(int profesorSesion, int? materiaSesion, int? grupoSesion, TimeSpan entrada, TimeSpan? salida,
            int profesorClase, int materiaClase, int grupoClase, TimeSpan inicioClase, TimeSpan finClase)
        {
            return profesorSesion == profesorClase && materiaSesion == materiaClase && grupoSesion == grupoClase
                && entrada < finClase
                && (salida.HasValue && salida.Value > entrada ? salida.Value : entrada + TimeSpan.FromHours(1)) > inicioClase;
        }

        /// <summary>
        /// A cada clase del dia que siga sin bitacoras, lo que se registro en la
        /// sesion de la clase de antes de su terna (EsDeLaClaseDeAntes), con sus
        /// asistencias dadas, como en Faltas. Una sesion, una clase.
        /// </summary>
        private static void AnotarLasDeLaClaseDeAntes(MySqlConnection conexion, List<SesionAgenda> agenda, List<SesionAgenda> deAntes)
        {
            Dictionary<int, int> dadas = AsistenciasDadas.ContarPorSesion(conexion, deAntes.Select(s => s.SesionId.Value));
            foreach (SesionAgenda sesion in deAntes.OrderBy(s => s.SesionId))
            {
                int cuantas;
                sesion.AsistenciasDadas = dadas.TryGetValue(sesion.SesionId.Value, out cuantas) ? cuantas : 0;
                if (sesion.Alumnos == 0 && sesion.AsistenciasDadas == 0)
                {
                    continue;
                }

                SesionAgenda clase = agenda.Where(s => !s.Cancelada && s.Utilizable && s.Alumnos == 0 && !s.EnLaDeAntes
                        && EsDeLaClaseDeAntes(sesion.ProfesorId, sesion.MateriaId, sesion.GrupoId, sesion.Inicio, sesion.Fin, s.ProfesorId, s.MateriaId, s.GrupoId, s.Inicio, s.Fin))
                    .OrderBy(s => s.Inicio).ThenBy(s => s.LaboratorioId).FirstOrDefault();
                if (clase == null)
                {
                    continue;
                }

                // Lo que necesita quien abre esa sesion desde el panel (VerRegistro): sus datos y su enlace.
                sesion.Materia = clase.Materia;
                sesion.Grupo = clase.Grupo;
                sesion.Profesor = clase.Profesor;
                sesion.ClaseId = clase.ClaseId;
                sesion.Codigo = clase.Codigo;
                sesion.Motivo = "";
                clase.EnLaDeAntes = true;
                clase.EnOtra = sesion;
                clase.BitacorasEnOtra = sesion.Alumnos;
                clase.ConFallas = sesion.ConFallas;
                clase.AsistenciasDadas = sesion.AsistenciasDadas;
            }
        }

        // ---------------------------------------------------------------------
        // Calendario de bitacoras: que clases tocaban cada dia y si las hubo
        // ---------------------------------------------------------------------

        /// <summary>Lo mas que lee LeerCalendario de una vez (las fechas salen de una tabla de digitos, sin CTE: MariaDB 5.5).</summary>
        public const int DiasMaximosCalendario = 400;

        /// <summary>
        /// Todos los laboratorios, de desde a hasta: cada clase que tocaba cada
        /// dia (franjas de clases activas y vigentes, con su cancelacion si la
        /// hubo, mas las sesiones extra), su sesion y cuantos alumnos registraron
        /// bitacora, y su estado (EstadoEnCalendario). Una conexion, cinco
        /// consultas; lo demas se junta aqui. La sesion de una franja es la de su
        /// dia y la de una extra la de su excepcion; las que no tienen enlace (las
        /// que se quedaron sin el al borrar o cambiar algo) se emparejan por
        /// laboratorio, dia, profesor y horas, y las que sobran van a Otras.
        /// </summary>
        /// <param name="contarTodos">
        /// Tambien los laboratorios que aun no registran con DigLab (y lo de antes
        /// de que empezaran): sus clases sin bitacoras salen en rojo. Ver
        /// CalendarioBitacoras.ContarTodos.
        /// </param>
        public CalendarioBitacoras LeerCalendario(DateTime desde, DateTime hasta, bool contarTodos = false)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerCalendario(conexion, desde, hasta, contarTodos);
            }
        }

        /// <summary>
        /// LeerCalendario en una conexion que ya se tiene (Estadisticas lee todo en
        /// una): lo mismo, sin abrir la suya. Con db/22, una consulta mas: las
        /// asistencias que dio cada profesor (ClaseDelCalendario.AsistenciasDadas).
        /// </summary>
        internal static CalendarioBitacoras LeerCalendario(MySqlConnection conexion, DateTime desde, DateTime hasta, bool contarTodos)
        {
            if ((hasta.Date - desde.Date).TotalDays >= DiasMaximosCalendario)
            {
                desde = hasta.Date.AddDays(-(DiasMaximosCalendario - 1));
            }

            CalendarioBitacoras calendario = new CalendarioBitacoras { Desde = desde.Date, Hasta = hasta.Date, ContarTodos = contarTodos };
            List<SesionDelCalendario> sesiones = new List<SesionDelCalendario>();
            {
                using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
                {
                    calendario.Ahora = Convert.ToDateTime(comando.ExecuteScalar());
                }

                using (MySqlCommand comando = new MySqlCommand("SELECT idlaboratorios, nombre_laboratorio FROM laboratorios", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        calendario.Laboratorios[reader.GetInt32(0)] = reader.GetString(1);
                    }
                }

                foreach (KeyValuePair<int, ModoCodigo> modo in ModosDeAcceso.LeerTodos(conexion).Where(m => m.Value != ModoCodigo.Fijo))
                {
                    calendario.LaboratoriosSinCodigoFijo.Add(modo.Key);
                }

                calendario.EmpezoEl = LeerEmpezoEl(conexion);
                calendario.PeriodosDeshabilitados = LaboratoriosDeshabilitados.LeerPeriodos(conexion);

                // Utilizable: el profesor no estaba dado de baja ese dia (db/08: lo
                // de antes de su baja si cuenta).
                bool conBajas = Esquema.TieneBajas(conexion);
                string profesorEseDia = conBajas ? "(u.activo = 1 OR (u.baja_en IS NOT NULL AND {0} < DATE(u.baja_en)))" : "1 = 1";
                // Cada franja, solo los dias en que se daba (con db/20, su propia
                // vigencia): la que se cambio desde una fecha sigue juzgando las
                // semanas de antes con su horario de entonces.
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT d.fecha, h.idhorarios AS franja_id, c.idclases, c.codigo, h.laboratorios_idlaboratorios AS lab_id,
                           h.hora_inicio, h.hora_fin, c.usuarios_idusuarios AS profesor_id,
                           u.nombre AS profesor_nombre, u.apellido_paterno AS profesor_paterno,
                           CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                           g.nombre_grupo, m.nombre_materia, c.materias_id_materia AS materia_id, c.grupos_idgrupos AS grupo_id,
                           (x.idhorario_excepciones IS NOT NULL) AS cancelada, x.motivo,
                           (" + string.Format(profesorEseDia, "d.fecha") + @") AS utilizable
                    FROM (SELECT DATE_ADD(DATE(@Desde), INTERVAL (a.n + 10 * b.n + 100 * k.n) DAY) AS fecha
                          FROM (SELECT 0 AS n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                                UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) a
                          CROSS JOIN (SELECT 0 AS n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                                UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) b
                          CROSS JOIN (SELECT 0 AS n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3) k) d
                    INNER JOIN horarios h ON h.dia_semana = WEEKDAY(d.fecha) + 1
                    INNER JOIN clases c   ON c.idclases = h.clases_idclases
                    INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    LEFT JOIN horario_excepciones x ON x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios AND x.fecha = d.fecha
                    WHERE d.fecha <= DATE(@Hasta) AND " + VigenciaFranjas.SeDa(conVigencia, "d.fecha"), conexion))
                {
                    comando.Parameters.AddWithValue("@Desde", calendario.Desde);
                    comando.Parameters.AddWithValue("@Hasta", calendario.Hasta);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ClaseDelCalendario clase = LeerClaseDelCalendario(reader);
                            clase.FranjaId = Convert.ToInt32(reader["franja_id"]);
                            clase.Cancelada = Convert.ToInt32(reader["cancelada"]) == 1;
                            clase.Motivo = Texto(reader, "motivo");
                            calendario.Clases.Add(clase);
                        }
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT x.fecha, x.idhorario_excepciones, c.idclases, c.codigo, x.laboratorios_idlaboratorios AS lab_id,
                           x.hora_inicio, x.hora_fin, c.usuarios_idusuarios AS profesor_id,
                           u.nombre AS profesor_nombre, u.apellido_paterno AS profesor_paterno,
                           CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                           g.nombre_grupo, m.nombre_materia, c.materias_id_materia AS materia_id, c.grupos_idgrupos AS grupo_id, x.motivo, x.creada_en,
                           (c.activa = 1 AND x.fecha BETWEEN c.vigente_desde AND c.vigente_hasta AND " + string.Format(profesorEseDia, "x.fecha") + @") AS utilizable
                    FROM horario_excepciones x
                    INNER JOIN clases c   ON c.idclases = x.clases_idclases
                    INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    WHERE x.tipo = 'extra' AND x.fecha BETWEEN DATE(@Desde) AND DATE(@Hasta)", conexion))
                {
                    comando.Parameters.AddWithValue("@Desde", calendario.Desde);
                    comando.Parameters.AddWithValue("@Hasta", calendario.Hasta);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ClaseDelCalendario clase = LeerClaseDelCalendario(reader);
                            clase.EsExtra = true;
                            clase.ExcepcionId = Convert.ToInt32(reader["idhorario_excepciones"]);
                            clase.Motivo = Texto(reader, "motivo");
                            clase.CreadaEn = reader.IsDBNull(reader.GetOrdinal("creada_en")) ? (DateTime?)null : reader.GetDateTime("creada_en");
                            calendario.Clases.Add(clase);
                        }
                    }
                }

                // Las sesiones de esas fechas con sus alumnos. hora_salida puede ser
                // texto en las bases de siempre: se lee con LeerHora.
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT ca.idcodigos_accesos, COALESCE(ca.fecha, DATE(ca.hora_registro)) AS fecha, ca.laboratorios_idlaboratorios AS lab_id,
                           ca.horarios_idhorarios, ca.horario_excepciones_id, ca.usuarios_idusuarios AS profesor_id,
                           CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                           ca.hora_entrada, ca.hora_salida, COUNT(DISTINCT rb.fk_usuario) AS alumnos,
                           ca.materias_id_materia AS materia_id, ca.grupos_idgrupos AS grupo_id,
                           MAX(cs.vigente_hasta < COALESCE(ca.fecha, DATE(ca.hora_registro))) AS de_terminada
                    FROM codigos_accesos ca
                    LEFT JOIN usuarios u ON u.idusuarios = ca.usuarios_idusuarios
                    LEFT JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos" + SesionConSuClase + @"
                    WHERE COALESCE(ca.fecha, DATE(ca.hora_registro)) BETWEEN DATE(@Desde) AND DATE(@Hasta)
                      AND NOT " + CondicionUsoLibre + @"
                    GROUP BY ca.idcodigos_accesos, COALESCE(ca.fecha, DATE(ca.hora_registro)), ca.laboratorios_idlaboratorios,
                             ca.horarios_idhorarios, ca.horario_excepciones_id, ca.usuarios_idusuarios,
                             CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno), ca.hora_entrada, ca.hora_salida,
                             ca.materias_id_materia, ca.grupos_idgrupos", conexion))
                {
                    comando.Parameters.AddWithValue("@Desde", calendario.Desde);
                    comando.Parameters.AddWithValue("@Hasta", calendario.Hasta);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            sesiones.Add(new SesionDelCalendario
                            {
                                Id = Convert.ToInt32(reader["idcodigos_accesos"]),
                                Fecha = Convert.ToDateTime(reader["fecha"]).Date,
                                LaboratorioId = reader.IsDBNull(reader.GetOrdinal("lab_id")) ? 0 : Convert.ToInt32(reader["lab_id"]),
                                FranjaId = reader.IsDBNull(reader.GetOrdinal("horarios_idhorarios")) ? (int?)null : Convert.ToInt32(reader["horarios_idhorarios"]),
                                ExcepcionId = reader.IsDBNull(reader.GetOrdinal("horario_excepciones_id")) ? (int?)null : Convert.ToInt32(reader["horario_excepciones_id"]),
                                ProfesorId = reader.IsDBNull(reader.GetOrdinal("profesor_id")) ? 0 : Convert.ToInt32(reader["profesor_id"]),
                                Profesor = Texto(reader, "profesor"),
                                Entrada = LeerHora(reader["hora_entrada"]),
                                Salida = LeerHora(reader["hora_salida"]),
                                Alumnos = Convert.ToInt32(reader["alumnos"]),
                                MateriaId = reader.IsDBNull(reader.GetOrdinal("materia_id")) ? (int?)null : Convert.ToInt32(reader["materia_id"]),
                                GrupoId = reader.IsDBNull(reader.GetOrdinal("grupo_id")) ? (int?)null : Convert.ToInt32(reader["grupo_id"]),
                                DeClaseTerminada = !reader.IsDBNull(reader.GetOrdinal("de_terminada")) && Convert.ToInt32(reader["de_terminada"]) == 1
                            });
                        }
                    }
                }

                // Las asistencias que dio cada profesor a quien no pudo registrar
                // (db/22, solo las que cuentan): una clase sin bitacoras pero con
                // alguna se dio (EstadoDe: SinBitacorasConAsistencia). Sin la 22, nada.
                calendario.ConDadas = Esquema.AsistenciaDada(conexion);
                if (calendario.ConDadas)
                {
                    Dictionary<int, int> dadas = AsistenciasDadas.ContarPorSesionEnFechas(conexion, calendario.Desde, calendario.Hasta);
                    foreach (SesionDelCalendario s in sesiones)
                    {
                        int cuantas;
                        s.Dadas = dadas.TryGetValue(s.Id, out cuantas) ? cuantas : 0;
                    }
                }

                MarcarRestos(calendario);
                EmparejarSesiones(calendario, sesiones);
                foreach (ClaseDelCalendario clase in calendario.Clases)
                {
                    clase.Estado = EstadoDe(clase, calendario);
                }

                ContarRecortadas(calendario);
                ContarLlegadasAnticipadas(conexion, calendario);
            }

            calendario.Clases = calendario.Clases.OrderBy(c => c.LaboratorioId).ThenBy(c => c.Fecha).ThenBy(c => c.Inicio).ToList();
            return calendario;
        }

        /// <summary>
        /// Desde cuando registra cada laboratorio con DigLab: su primera sesion de
        /// una clase del horario con alguna bitacora. Las del sistema anterior no
        /// tienen enlace; y una sesion sin bitacoras (abierta por el panel con la
        /// generacion de codigos, o creada solo por un reporte del profesor, db/15)
        /// no dice que alli ya se registre con DigLab: contarla ponia en rojo, en
        /// Faltas y en las alertas, todas las clases de un laboratorio que aun
        /// registra con el sistema anterior.
        /// </summary>
        private static Dictionary<int, DateTime> LeerEmpezoEl(MySqlConnection conexion)
        {
            Dictionary<int, DateTime> empezo = new Dictionary<int, DateTime>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.laboratorios_idlaboratorios, MIN(COALESCE(ca.fecha, DATE(ca.hora_registro)))
                FROM codigos_accesos ca
                WHERE ca.laboratorios_idlaboratorios IS NOT NULL
                  AND (ca.horarios_idhorarios IS NOT NULL OR ca.horario_excepciones_id IS NOT NULL)
                  AND EXISTS (SELECT 1 FROM registros_bitacoras rb WHERE rb.fk_codigo_accesos = ca.idcodigos_accesos)
                GROUP BY ca.laboratorios_idlaboratorios", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!reader.IsDBNull(1))
                    {
                        empezo[reader.GetInt32(0)] = reader.GetDateTime(1).Date;
                    }
                }
            }

            return empezo;
        }

        // LeerEmpezoEl recorre codigos_accesos entera: el panel del profesor, que
        // recarga cada 30 s, la lee como mucho cada 10 minutos (solo cambia el
        // dia en que un laboratorio estrena DigLab).
        private static readonly object candadoEmpezoEl = new object();
        private static Dictionary<int, DateTime> empezoElGuardado;
        private static DateTime empezoElLeidoEn;

        private static Dictionary<int, DateTime> EmpezoElReciente(MySqlConnection conexion)
        {
            lock (candadoEmpezoEl)
            {
                if (empezoElGuardado != null && DateTime.Now - empezoElLeidoEn < TimeSpan.FromMinutes(10) && DateTime.Now >= empezoElLeidoEn)
                {
                    return empezoElGuardado;
                }
            }

            Dictionary<int, DateTime> leido = LeerEmpezoEl(conexion);
            lock (candadoEmpezoEl)
            {
                empezoElGuardado = leido;
                empezoElLeidoEn = DateTime.Now;
            }

            return leido;
        }

        /// <summary>
        /// Las correcciones de Faltas (LeerCalendario) para las clases de un
        /// profesor de su columna Ayer (FechaAyer: ayer o el ultimo dia antes con
        /// clases) y de hoy, para que su panel no pinte en rojo lo que Faltas da
        /// por bueno. Todo va por fecha, asi que vale igual con un dia de hace
        /// una semana. Con las mismas reglas (MarcarRestos, EstadoDe,
        /// ContarRecortadas, ContarLlegadasAnticipadas): lo que le queda a una clase
        /// recortada cuenta con las bitacoras de su franja (tambien mientras dura),
        /// una clase seguida con codigo fijo con los que llegaron antes y quedaron
        /// en la anterior, y no se juzga una clase de un laboratorio que aun no
        /// registraba con DigLab ni una extra puesta despues de su hora.
        /// </summary>
        private static void CorregirComoFaltas(MySqlConnection conexion, PanoramaProfesor panorama)
        {
            DateTime hoy = panorama.AhoraServidor.Date;
            DateTime ayer = panorama.FechaAyer == DateTime.MinValue ? hoy.AddDays(-1) : panorama.FechaAyer.Date;
            CalendarioBitacoras calendario = new CalendarioBitacoras
            {
                Ahora = panorama.AhoraServidor,
                Desde = ayer,
                Hasta = hoy,
                EmpezoEl = EmpezoElReciente(conexion),
                PeriodosDeshabilitados = LaboratoriosDeshabilitados.LeerPeriodos(conexion)
            };
            foreach (KeyValuePair<int, ModoCodigo> modo in panorama.Modos.Where(m => m.Value != ModoCodigo.Fijo))
            {
                calendario.LaboratoriosSinCodigoFijo.Add(modo.Key);
            }

            Dictionary<ClaseDelCalendario, SesionAgenda> suyas = new Dictionary<ClaseDelCalendario, SesionAgenda>();
            foreach (KeyValuePair<DateTime, List<SesionAgenda>> dia in new[]
            {
                new KeyValuePair<DateTime, List<SesionAgenda>>(ayer, panorama.Ayer),
                new KeyValuePair<DateTime, List<SesionAgenda>>(hoy, panorama.Hoy)
            })
            {
                foreach (SesionAgenda s in dia.Value)
                {
                    ClaseDelCalendario clase = new ClaseDelCalendario
                    {
                        Fecha = dia.Key,
                        LaboratorioId = s.LaboratorioId,
                        EsExtra = s.EsExtra,
                        FranjaId = s.FranjaId,
                        ExcepcionId = s.ExcepcionId,
                        ClaseId = s.ClaseId,
                        Codigo = s.Codigo,
                        ProfesorId = s.ProfesorId,
                        Profesor = s.Profesor,
                        Grupo = s.Grupo,
                        Materia = s.Materia,
                        Inicio = s.Inicio,
                        Fin = s.Fin,
                        Motivo = s.Motivo ?? "",
                        Cancelada = s.Cancelada,
                        Utilizable = s.Utilizable,
                        CreadaEn = s.CreadaEn,
                        // Con lo de la clase de antes (AnotarSesiones), como en Faltas.
                        SesionId = s.EnLaDeAntes && s.EnOtra != null ? s.EnOtra.SesionId : s.SesionId,
                        Bitacoras = s.EnLaDeAntes ? s.BitacorasEnOtra : s.Alumnos,
                        AsistenciasDadas = s.AsistenciasDadas
                    };
                    calendario.Clases.Add(clase);
                    suyas[clase] = s;
                }
            }

            MarcarRestos(calendario);
            foreach (ClaseDelCalendario clase in calendario.Clases)
            {
                clase.Estado = EstadoDe(clase, calendario);
            }

            ContarRecortadas(calendario);
            ContarLlegadasAnticipadas(conexion, calendario);

            ILookup<string, ClaseDelCalendario> franjasConBitacoras = calendario.Clases
                .Where(c => !c.EsExtra && c.Cancelada && c.Bitacoras > 0).ToLookup(ClaveDeClaseDia);
            foreach (KeyValuePair<ClaseDelCalendario, SesionAgenda> par in suyas)
            {
                ClaseDelCalendario clase = par.Key;
                SesionAgenda s = par.Value;
                s.EsResto = clase.EsResto;
                s.NoContaba = clase.Estado == EstadoEnCalendario.NoContaba;
                if (s.EnLaDeAntes)
                {
                    // Sus bitacoras ya estan en la sesion de la clase de antes.
                    continue;
                }

                ClaseDelCalendario otra = clase.OtraClase;
                if (otra == null && clase.EsResto && clase.Bitacoras == 0)
                {
                    // Un resto que aun no termina (ContarRecortadas solo ve los que ya
                    // pasaron): sus alumnos ya registraron en la sesion de su franja.
                    otra = franjasConBitacoras[ClaveDeClaseDia(clase)].FirstOrDefault(f => f.Inicio <= clase.Inicio && f.Fin >= clase.Fin);
                    clase.BitacorasEnOtra = otra == null ? 0 : otra.Bitacoras;
                }

                s.BitacorasEnOtra = otra == null ? 0 : clase.BitacorasEnOtra;
                s.EnOtra = otra == null ? null : suyas[otra];
            }
        }

        /// <summary>La clase, el laboratorio y el dia: lo que comparten una franja recortada y sus restos.</summary>
        private static string ClaveDeClaseDia(ClaseDelCalendario c)
        {
            return c.ClaseId + "|" + c.LaboratorioId + "|" + c.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Marca como resto (ClaseDelCalendario.EsResto) cada extra que cae dentro
        /// de una franja cancelada ese dia de su misma clase y laboratorio: lo que
        /// le quedo al recortarla (AplicarExcepcion). Se puso con la hora de ese
        /// momento, a veces con la clase ya empezada o ya pasada, asi que no se
        /// juzga como "puesta despues de su hora".
        /// </summary>
        internal static void MarcarRestos(CalendarioBitacoras calendario)
        {
            ILookup<string, ClaseDelCalendario> canceladas = calendario.Clases.Where(c => !c.EsExtra && c.Cancelada).ToLookup(ClaveDeClaseDia);
            foreach (ClaseDelCalendario extra in calendario.Clases.Where(c => c.EsExtra))
            {
                extra.EsResto = canceladas[ClaveDeClaseDia(extra)].Any(f => f.Inicio <= extra.Inicio && f.Fin >= extra.Fin);
            }
        }

        /// <summary>
        /// Una clase recortada con la clase ya empezada (AplicarExcepcion): sus
        /// alumnos registraron en la sesion de su franja, que quedo cancelada, y
        /// lo que le queda (un resto) salio vacio. Cuenta con las de su franja.
        /// </summary>
        internal static void ContarRecortadas(CalendarioBitacoras calendario)
        {
            ILookup<string, ClaseDelCalendario> canceladas = calendario.Clases.Where(c => !c.EsExtra && c.Cancelada && c.Bitacoras > 0).ToLookup(ClaveDeClaseDia);
            foreach (ClaseDelCalendario resto in calendario.Clases.Where(c => c.EsResto && c.SinBitacorasPropias).ToList())
            {
                ClaseDelCalendario franja = canceladas[ClaveDeClaseDia(resto)].FirstOrDefault(c => c.Inicio <= resto.Inicio && c.Fin >= resto.Fin);
                if (franja != null)
                {
                    resto.BitacorasEnOtra = franja.Bitacoras;
                    resto.OtraClase = franja;
                    resto.Estado = EstadoDe(resto, calendario);
                }
            }
        }

        /// <summary>
        /// Las clases que salieron vacias justo despues de otra del mismo profesor
        /// en el mismo laboratorio, cuya sesion si tiene bitacoras: con codigo
        /// fijo, quien llega en los 15 minutos antes entra en la sesion de la que
        /// ya empezo (ReglasCodigo), la anterior. Solo en los laboratorios con
        /// codigo fijo: en los demas el codigo (o la lista) dice la clase.
        /// </summary>
        internal static List<KeyValuePair<ClaseDelCalendario, ClaseDelCalendario>> ParesSeguidos(CalendarioBitacoras calendario)
        {
            TimeSpan margen = TimeSpan.FromMinutes(MargenAntesDelInicioMinutos);
            ILookup<string, ClaseDelCalendario> conBitacoras = calendario.Clases
                .Where(c => !c.Cancelada && c.SesionId.HasValue && c.Bitacoras > 0)
                .ToLookup(ClaveDeProfesorDia);
            List<KeyValuePair<ClaseDelCalendario, ClaseDelCalendario>> pares = new List<KeyValuePair<ClaseDelCalendario, ClaseDelCalendario>>();
            foreach (ClaseDelCalendario segunda in calendario.Clases.Where(c => c.SinBitacorasPropias && !calendario.LaboratoriosSinCodigoFijo.Contains(c.LaboratorioId)))
            {
                ClaseDelCalendario primera = conBitacoras[ClaveDeProfesorDia(segunda)]
                    .Where(c => c != segunda && c.Inicio < segunda.Inicio && c.Fin > segunda.Inicio - margen)
                    .OrderByDescending(c => c.Inicio)
                    .FirstOrDefault();
                if (primera != null)
                {
                    pares.Add(new KeyValuePair<ClaseDelCalendario, ClaseDelCalendario>(primera, segunda));
                }
            }

            return pares;
        }

        private static string ClaveDeProfesorDia(ClaseDelCalendario c)
        {
            return c.LaboratorioId + "|" + c.ProfesorId + "|" + c.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A cada par de ParesSeguidos, los alumnos de la sesion de la primera que
        /// registraron desde 15 minutos antes de que empezara la segunda hasta 10
        /// minutos despues: registrado_en es la hora en que se guardo la bitacora,
        /// no la del codigo, y llenarla lleva unos minutos (pasada la hora de la
        /// segunda, el codigo ya no entra en la primera).
        /// </summary>
        private static void ContarLlegadasAnticipadas(MySqlConnection conexion, CalendarioBitacoras calendario)
        {
            TimeSpan margen = TimeSpan.FromMinutes(MargenAntesDelInicioMinutos);
            TimeSpan llenado = TimeSpan.FromMinutes(10);
            List<KeyValuePair<ClaseDelCalendario, ClaseDelCalendario>> pares = ParesSeguidos(calendario);

            // De 100 en 100, cada tanda en una consulta: una tabla con los pares
            // (sesion de la anterior y la ventana) unida a las bitacoras.
            for (int desde = 0; desde < pares.Count; desde += 100)
            {
                List<KeyValuePair<ClaseDelCalendario, ClaseDelCalendario>> tanda = pares.Skip(desde).Take(100).ToList();
                using (MySqlCommand comando = new MySqlCommand { Connection = conexion })
                {
                    List<string> filas = new List<string>();
                    for (int i = 0; i < tanda.Count; i++)
                    {
                        DateTime inicio = tanda[i].Value.Fecha + tanda[i].Value.Inicio;
                        filas.Add("SELECT " + i + " AS i, @S" + i + " AS sesion, CAST(@D" + i + " AS DATETIME) AS desde, CAST(@H" + i + " AS DATETIME) AS hasta");
                        comando.Parameters.AddWithValue("@S" + i, tanda[i].Key.SesionId.Value);
                        comando.Parameters.AddWithValue("@D" + i, (inicio - margen).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                        comando.Parameters.AddWithValue("@H" + i, (inicio + llenado).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    }

                    comando.CommandText = @"
                        SELECT t.i, COUNT(DISTINCT rb.fk_usuario) AS alumnos
                        FROM (" + string.Join(" UNION ALL ", filas) + @") t
                        INNER JOIN registros_bitacoras rb
                                ON rb.fk_codigo_accesos = t.sesion AND rb.registrado_en >= t.desde AND rb.registrado_en < t.hasta
                        GROUP BY t.i";
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            KeyValuePair<ClaseDelCalendario, ClaseDelCalendario> par = tanda[Convert.ToInt32(reader["i"])];
                            par.Value.BitacorasEnOtra = Convert.ToInt32(reader["alumnos"]);
                            par.Value.OtraClase = par.Key;
                            par.Value.Estado = EstadoDe(par.Value, calendario);
                        }
                    }
                }
            }
        }

        private static ClaseDelCalendario LeerClaseDelCalendario(MySqlDataReader reader)
        {
            return new ClaseDelCalendario
            {
                Fecha = Convert.ToDateTime(reader["fecha"], CultureInfo.InvariantCulture).Date,
                ClaseId = Convert.ToInt32(reader["idclases"]),
                Codigo = Texto(reader, "codigo"),
                LaboratorioId = reader.IsDBNull(reader.GetOrdinal("lab_id")) ? 0 : Convert.ToInt32(reader["lab_id"]),
                Inicio = reader.GetTimeSpan("hora_inicio"),
                Fin = reader.GetTimeSpan("hora_fin"),
                ProfesorId = Convert.ToInt32(reader["profesor_id"]),
                Profesor = Texto(reader, "profesor"),
                ProfesorCorto = NombreCorto(Texto(reader, "profesor_nombre"), Texto(reader, "profesor_paterno")),
                Grupo = Texto(reader, "nombre_grupo"),
                Materia = Texto(reader, "nombre_materia"),
                MateriaId = Convert.ToInt32(reader["materia_id"]),
                GrupoId = Convert.ToInt32(reader["grupo_id"]),
                Utilizable = !reader.IsDBNull(reader.GetOrdinal("utilizable")) && Convert.ToInt32(reader["utilizable"]) == 1
            };
        }

        /// <summary>Una sesion leida para el calendario (codigos_accesos con sus alumnos).</summary>
        internal sealed class SesionDelCalendario
        {
            public int Id;
            public DateTime Fecha;
            public int LaboratorioId;
            public int? FranjaId;
            public int? ExcepcionId;
            public int ProfesorId;
            public string Profesor;
            public TimeSpan? Entrada;
            public TimeSpan? Salida;
            public int Alumnos;
            /// <summary>Las asistencias que dio su profesor en ella (db/22, las que cuentan).</summary>
            public int Dadas;
            public int? MateriaId;
            public int? GrupoId;
            /// <summary>Enlazada a una clase que termino antes de su dia (EsDeLaClaseDeAntes).</summary>
            public bool DeClaseTerminada;
        }

        /// <summary>
        /// A cada clase, su sesion: la de su franja ese dia o la de su extra; si no
        /// tiene (o la suya salio vacia), la de la clase de antes de su terna, aunque
        /// este en otro laboratorio (EsDeLaClaseDeAntes: solo con "Ahora mismo"); si
        /// no, una sin enlace del mismo laboratorio, dia y profesor cuyas horas se
        /// crucen (una sesion, una clase). Las sesiones que no son de ninguna clase
        /// van a Otras.
        /// </summary>
        internal static void EmparejarSesiones(CalendarioBitacoras calendario, List<SesionDelCalendario> sesiones)
        {
            Dictionary<string, SesionDelCalendario> porFranja = new Dictionary<string, SesionDelCalendario>();
            Dictionary<int, SesionDelCalendario> porExtra = new Dictionary<int, SesionDelCalendario>();
            foreach (SesionDelCalendario s in sesiones)
            {
                if (s.ExcepcionId.HasValue && !porExtra.ContainsKey(s.ExcepcionId.Value))
                {
                    porExtra[s.ExcepcionId.Value] = s;
                }
                else if (s.FranjaId.HasValue && !porFranja.ContainsKey(ClaveDeFranja(s.FranjaId.Value, s.Fecha)))
                {
                    porFranja[ClaveDeFranja(s.FranjaId.Value, s.Fecha)] = s;
                }
            }

            HashSet<int> usadas = new HashSet<int>();
            foreach (ClaseDelCalendario clase in calendario.Clases)
            {
                SesionDelCalendario s = null;
                if (clase.EsExtra && clase.ExcepcionId.HasValue)
                {
                    porExtra.TryGetValue(clase.ExcepcionId.Value, out s);
                }
                else if (!clase.EsExtra && clase.FranjaId.HasValue)
                {
                    porFranja.TryGetValue(ClaveDeFranja(clase.FranjaId.Value, clase.Fecha), out s);
                }

                if (s == null)
                {
                    continue;
                }

                clase.SesionId = s.Id;
                clase.Bitacoras = s.Alumnos;
                clase.AsistenciasDadas = s.Dadas;
                usadas.Add(s.Id);
            }

            // Lo registrado antes de aplicar con "Ahora mismo": a su clase nueva, antes
            // que a otra del mismo profesor en ese laboratorio. Su sesion propia, si
            // salio vacia, ya no cuenta.
            foreach (SesionDelCalendario s in sesiones.Where(x => x.DeClaseTerminada && !usadas.Contains(x.Id) && x.Entrada.HasValue && (x.Alumnos > 0 || x.Dadas > 0))
                .OrderBy(x => x.Fecha).ThenBy(x => x.Id))
            {
                ClaseDelCalendario clase = calendario.Clases
                    .Where(c => c.Fecha == s.Fecha && !c.Cancelada && c.Utilizable && (!c.SesionId.HasValue || (c.Bitacoras == 0 && c.AsistenciasDadas == 0))
                        && EsDeLaClaseDeAntes(s.ProfesorId, s.MateriaId, s.GrupoId, s.Entrada.Value, s.Salida, c.ProfesorId, c.MateriaId, c.GrupoId, c.Inicio, c.Fin))
                    .OrderBy(c => c.Inicio).ThenBy(c => c.LaboratorioId).FirstOrDefault();
                if (clase == null)
                {
                    continue;
                }

                clase.SesionId = s.Id;
                clase.Bitacoras = s.Alumnos;
                clase.AsistenciasDadas = s.Dadas;
                clase.SesionSinEnlace = true;
                usadas.Add(s.Id);
            }

            // Sin enlace (o con uno que ya no es de ninguna clase de ese dia): por
            // laboratorio, dia, profesor y horas que se crucen. Agrupadas antes: en
            // un rango largo hay miles de clases y de sesiones. La de una clase que ya
            // termino solo vale para su misma terna: sus bitacoras cuentan en su lista,
            // y otra clase del mismo docente a esa hora no se dio por ellas.
            ILookup<string, SesionDelCalendario> sueltas = sesiones.Where(x => !usadas.Contains(x.Id) && x.Entrada.HasValue)
                .ToLookup(x => x.LaboratorioId + "|" + x.ProfesorId + "|" + x.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            foreach (ClaseDelCalendario clase in calendario.Clases.Where(c => !c.SesionId.HasValue && !c.Cancelada).OrderBy(c => c.Fecha).ThenBy(c => c.Inicio))
            {
                SesionDelCalendario s = sueltas[ClaveDeProfesorDia(clase)].FirstOrDefault(x => !usadas.Contains(x.Id)
                    && (!x.DeClaseTerminada || (x.MateriaId == clase.MateriaId && x.GrupoId == clase.GrupoId))
                    && x.Entrada.Value < clase.Fin
                    && (x.Salida.HasValue && x.Salida.Value > x.Entrada.Value ? x.Salida.Value : x.Entrada.Value + TimeSpan.FromHours(1)) > clase.Inicio);
                if (s == null)
                {
                    continue;
                }

                clase.SesionId = s.Id;
                clase.Bitacoras = s.Alumnos;
                clase.AsistenciasDadas = s.Dadas;
                clase.SesionSinEnlace = true;
                usadas.Add(s.Id);
            }

            calendario.Otras = sesiones.Where(s => !usadas.Contains(s.Id)).Select(s => new SesionSuelta
            {
                Id = s.Id,
                Fecha = s.Fecha,
                LaboratorioId = s.LaboratorioId,
                Entrada = s.Entrada,
                Salida = s.Salida,
                ProfesorId = s.ProfesorId,
                Profesor = s.Profesor,
                Bitacoras = s.Alumnos,
                Dadas = s.Dadas
            }).OrderBy(s => s.LaboratorioId).ThenBy(s => s.Fecha).ThenBy(s => s.Entrada).ToList();
        }

        private static string ClaveDeFranja(int idFranja, DateTime fecha)
        {
            return idFranja + "|" + fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        /// <summary>El estado de una clase en el calendario, con el reloj del servidor al leer.</summary>
        internal static EstadoEnCalendario EstadoDe(ClaseDelCalendario clase, CalendarioBitacoras calendario)
        {
            if (clase.Cancelada)
            {
                return EstadoEnCalendario.Cancelada;
            }

            DateTime ahora = calendario.Ahora;
            if (clase.Fecha > ahora.Date || (clase.Fecha == ahora.Date && clase.Fin > ahora.TimeOfDay))
            {
                return EstadoEnCalendario.PorVenir;
            }

            if ((clase.SesionId.HasValue && clase.Bitacoras > 0) || clase.BitacorasEnOtra > 0)
            {
                return EstadoEnCalendario.ConBitacoras;
            }

            // No cuenta: no se podia dar, la extra se puso cuando ya habia pasado (un
            // resto no: es lo que le quedo a su clase al recortarla), el laboratorio
            // aun no registraba con DigLab, o estaba deshabilitado.
            DateTime empezo;
            bool extraTardia = clase.EsExtra && !clase.EsResto && clase.CreadaEn.HasValue && clase.CreadaEn.Value >= clase.Fecha + clase.Fin;
            bool antesDeDigLab = !calendario.ContarTodos
                && (!calendario.EmpezoEl.TryGetValue(clase.LaboratorioId, out empezo) || clase.Fecha < empezo);
            bool deshabilitado = LaboratoriosDeshabilitados.Cubre(calendario.PeriodosDeshabilitados, clase.LaboratorioId, clase.Fecha + clase.Inicio, clase.Fecha + clase.Fin);
            if (!clase.Utilizable || extraTardia || antesDeDigLab || deshabilitado)
            {
                return EstadoEnCalendario.NoContaba;
            }

            // Nadie registro, pero su profesor dio la asistencia a quien no pudo
            // (db/22): la clase se dio. En ambar: ni falta, ni alerta, ni "no dada".
            if (clase.SesionId.HasValue && clase.AsistenciasDadas > 0)
            {
                return EstadoEnCalendario.SinBitacorasConAsistencia;
            }

            return clase.SesionId.HasValue ? EstadoEnCalendario.SesionVacia : EstadoEnCalendario.SinSesion;
        }

        /// <summary>
        /// Para el panel del profesor (por su numero de empleado): su nombre, su
        /// codigo, sus clases de hoy con los conteos de bitacoras (lo mismo que ve
        /// el administrador) y su horario semanal. Hora del servidor. Solo lee:
        /// las clases con generacion de codigos las abre AbrirClasesDeHoy.
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
                            // El panel lo ensena tal cual; al abrir una seccion, con que hacer.
                            throw new InvalidOperationException("No se encontró tu cuenta de docente (número de empleado " + numeroEmpleado + ").");
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
            panorama.Modos = dia.Modos;
            panorama.CodigosOcultos = dia.CodigosSoloResponsables;
            // Sin las sesiones extra de una clase desactivada o fuera de vigencia:
            // no se pueden usar y el panel no debe ofrecerlas ni intentar abrirlas.
            panorama.Hoy = dia.Agenda.Where(s => s.ProfesorId == panorama.ProfesorId && s.Utilizable).OrderBy(s => s.Inicio).ToList();
            foreach (OpcionCombo laboratorio in dia.Laboratorios)
            {
                panorama.Laboratorios[laboratorio.Id] = laboratorio.Texto;
            }

            // Las de un laboratorio deshabilitado (no esta en la lista de arriba) no corren.
            List<FranjaInfo> franjas = ObtenerFranjasDeProfesor(panorama.ProfesorId);
            panorama.Franjas = franjas.Where(f => panorama.Laboratorios.ContainsKey(f.LaboratorioId)).ToList();
            panorama.SoloDeshabilitados = franjas.Count > 0 && panorama.Franjas.Count == 0;

            if (!panorama.CodigoPorProfesor)
            {
                panorama.Clases = ObtenerClasesDeProfesor(panorama.ProfesorId).Where(c => c.VigenteEn(dia.AhoraServidor)).ToList();
            }

            // Las otras dos columnas del panel: el ultimo dia antes de hoy en que
            // tuvo algo en su agenda (ayer, si lo tuvo), con cuantos registraron, y
            // el siguiente despues de hoy, hasta una semana a cada lado. Asi el
            // lunes la primera es la del viernes (o el sabado), no el domingo vacio.
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                DateTime hoy = dia.AhoraServidor.Date;
                List<DateTime> conAgenda = DiasConAgendaCerca(conexion, panorama.ProfesorId, hoy);
                DateTime fecha;
                panorama.Ayer = PrimerDiaConAgenda(conexion, panorama.ProfesorId,
                    conAgenda.Where(d => d < hoy).OrderByDescending(d => d), hoy.AddDays(-1), true, out fecha);
                panorama.FechaAyer = fecha;
                panorama.Manana = PrimerDiaConAgenda(conexion, panorama.ProfesorId,
                    conAgenda.Where(d => d > hoy).OrderBy(d => d), hoy.AddDays(1), false, out fecha);
                panorama.FechaManana = fecha;

                // Las asistencias que dio en sus clases de Ayer (las de hoy ya vienen
                // de PanoramaDelDia), antes de corregir como Faltas: una clase sin
                // bitacoras pero con ellas no sale en rojo. Sin db/22, nada; si falla,
                // el panel sigue sin ellas, como sin la 22. Si PanoramaDelDia ya
                // pregunto por la 22, no se vuelve a preguntar (sin ella, cada
                // pregunta va a information_schema, y esto se repite cada 30 s).
                try
                {
                    panorama.ConDadas = dia.ConDadas ?? Esquema.AsistenciaDada(conexion);
                    if (panorama.ConDadas)
                    {
                        AnotarDadas(conexion, panorama.Ayer);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Leer las asistencias que dio el profesor", ex);
                }

                // Que no salga en rojo lo que Faltas da por bueno. Si falla, el panel
                // sigue con los conteos tal cual (solo es una correccion).
                try
                {
                    CorregirComoFaltas(conexion, panorama);
                }
                catch (Exception ex)
                {
                    Log.Error("HorariosDatos.CorregirComoFaltas", ex);
                }

                // Las fallas que reporto el profesor (db/15) cuentan en "fallas
                // reportadas" de su panel. Sin la 15 no hay y no se ofrece reportar.
                try
                {
                    panorama.ReportesDisponibles = Esquema.ReportesDeProfesor(conexion);
                    List<SesionAgenda> conSesion = panorama.Hoy.Concat(panorama.Ayer).Where(s => s.SesionId.HasValue).ToList();
                    Dictionary<int, int> reportes = ReportesProfesorDatos.ContarPorSesion(conexion, conSesion.Select(s => s.SesionId.Value));
                    foreach (SesionAgenda s in conSesion)
                    {
                        int cuantos;
                        s.ReportesProfesor = reportes.TryGetValue(s.SesionId.Value, out cuantos) ? cuantos : 0;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Leer las fallas que reporto el profesor", ex);
                }

                // Lo que eligio para sus clases (db/19): el circulo y el color de cada
                // una en el panel. Si falla, PersonalLeido se queda en falso y el
                // panel sigue con lo de la lectura anterior (no se borran los circulos).
                try
                {
                    panorama.Personal = PersonalizacionClasesDatos.Leer(conexion, panorama.ProfesorId);
                    panorama.PersonalLeido = true;
                }
                catch (Exception ex)
                {
                    Log.Error("Leer lo que el profesor eligio para sus clases", ex);
                }
            }

            return panorama;
        }

        /// <summary>
        /// Las clases de un profesor un dia (agenda efectiva, con canceladas y
        /// extras; sin las que no se pueden usar) y, si se pide, cuantos registraron.
        /// </summary>
        private static List<SesionAgenda> AgendaDelProfesor(MySqlConnection conexion, int idProfesor, DateTime fecha, bool conConteos)
        {
            List<SesionAgenda> suyas = LeerAgenda(conexion, null, fecha)
                .Where(s => s.ProfesorId == idProfesor && s.Utilizable).OrderBy(s => s.Inicio).ToList();
            if (conConteos && suyas.Count > 0)
            {
                AnotarSesiones(conexion, suyas, fecha, null, true);
            }

            return suyas;
        }

        /// <summary>Hasta cuantos dias antes y despues de hoy buscan un dia con clases las columnas Ayer y Manana del panel del profesor.</summary>
        private const int DiasParaAyerYManana = 7;

        // DiasConAgenda fallo por algo que no es la red (una base que no entiende
        // la consulta): el panel sigue con ayer y manana. Se repetiria cada 30 s:
        // al registro, solo la primera vez.
        private static bool diasConAgendaFallo;

        /// <summary>
        /// Los dias de la semana de antes y la de despues de hoy en que el
        /// profesor tiene algo en su agenda (DiasConAgenda). Sin conexion lanza,
        /// como el resto de PanoramaDeProfesor; con otro fallo, ninguno: el panel
        /// se queda con ayer y manana, como antes.
        /// </summary>
        private static List<DateTime> DiasConAgendaCerca(MySqlConnection conexion, int idProfesor, DateTime hoy)
        {
            try
            {
                return DiasConAgenda(conexion, idProfesor, hoy.AddDays(-DiasParaAyerYManana), hoy.AddDays(DiasParaAyerYManana));
            }
            catch (Exception ex) when (ColaBitacoras.Clasificar(ex) != ClaseDeFallo.SinConexion)
            {
                if (!diasConAgendaFallo)
                {
                    diasConAgendaFallo = true;
                    Log.Error("HorariosDatos.DiasConAgenda: el panel del profesor sigue con ayer y manana", ex);
                }

                return new List<DateTime>();
            }
        }

        /// <summary>
        /// Los dias de desde a hasta en que AgendaDelProfesor no sale vacia: una
        /// franja de una clase suya que se da ese dia (VigenciaFranjas.SeDa;
        /// cancelada o no) o una sesion extra de una clase suya activa y vigente,
        /// y el profesor sin dar de baja (db/08): lo mismo que LeerAgenda marca
        /// como Utilizable. Una sola consulta para todo el rango, en vez de leer
        /// la agenda de todos los laboratorios dia por dia. Las fechas salen de
        /// una tabla de numeros, sin CTE (MariaDB 5.5).
        /// </summary>
        private static List<DateTime> DiasConAgenda(MySqlConnection conexion, int idProfesor, DateTime desde, DateTime hasta)
        {
            List<DateTime> fechas = new List<DateTime>();
            int dias = (int)(hasta.Date - desde.Date).TotalDays + 1;
            if (dias <= 0)
            {
                return fechas;
            }

            string profesorUtil = Esquema.TieneBajas(conexion) ? "u.activo = 1" : "1 = 1";
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
            string numeros = string.Join(" UNION ALL ", Enumerable.Range(0, dias).Select(n => "SELECT " + n.ToString(CultureInfo.InvariantCulture) + " AS n"));
            string franjaHabilitada = LaboratoriosDeshabilitados.SinDeshabilitados(conexion, "h.laboratorios_idlaboratorios");
            string extraHabilitada = LaboratoriosDeshabilitados.SinDeshabilitados(conexion, "x.laboratorios_idlaboratorios");
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT d.fecha
                FROM (SELECT DATE_ADD(DATE(@Desde), INTERVAL k.n DAY) AS fecha FROM (" + numeros + @") k) d
                WHERE EXISTS (SELECT 1 FROM horarios h
                              INNER JOIN clases c   ON c.idclases = h.clases_idclases
                              INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                              INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                              INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                              WHERE c.usuarios_idusuarios = @Profesor AND h.dia_semana = WEEKDAY(d.fecha) + 1
                                AND " + VigenciaFranjas.SeDa(conVigencia, "d.fecha") + " AND " + profesorUtil + franjaHabilitada + @")
                   OR EXISTS (SELECT 1 FROM horario_excepciones x
                              INNER JOIN clases c   ON c.idclases = x.clases_idclases
                              INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                              INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                              INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                              WHERE x.tipo = 'extra' AND x.fecha = d.fecha AND c.usuarios_idusuarios = @Profesor
                                AND c.activa = 1 AND d.fecha BETWEEN c.vigente_desde AND c.vigente_hasta AND " + profesorUtil + extraHabilitada + @")
                ORDER BY d.fecha", conexion))
            {
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Profesor", idProfesor);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        fechas.Add(Convert.ToDateTime(reader["fecha"], CultureInfo.InvariantCulture).Date);
                    }
                }
            }

            return fechas;
        }

        /// <summary>
        /// La agenda de la columna Ayer o de la de Manana del panel: la del primer
        /// candidato (ya en orden, del mas cercano a hoy al mas lejano) que no
        /// sale vacia, y su fecha; sin ninguno, la de porDefecto (ayer o manana).
        /// Cada candidato se comprueba con AgendaDelProfesor, que es lo que se
        /// ensena: si DiasConAgenda diera un dia sin nada, se pasa al siguiente.
        /// </summary>
        private static List<SesionAgenda> PrimerDiaConAgenda(MySqlConnection conexion, int idProfesor, IEnumerable<DateTime> candidatos,
            DateTime porDefecto, bool conConteos, out DateTime fecha)
        {
            foreach (DateTime candidato in candidatos)
            {
                List<SesionAgenda> agenda = AgendaDelProfesor(conexion, idProfesor, candidato, conConteos);
                if (agenda.Count > 0)
                {
                    fecha = candidato;
                    return agenda;
                }
            }

            fecha = porDefecto;
            return AgendaDelProfesor(conexion, idProfesor, porDefecto, conConteos);
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
        // El alumno entra a su clase: con codigo, o eligiendola (db/11)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Resuelve lo que teclea el alumno para ESTE laboratorio y este momento
        /// (reloj del servidor), segun el modo de acceso del laboratorio (db/11):
        /// con el codigo unico (y sin codigos, si llega uno de una pantalla vieja)
        /// es el codigo del profesor y la sesion de hoy se crea al primer uso;
        /// con la generacion de codigos es el de una sesion de hoy (la abre el
        /// panel del profesor o el administrador) o, si la clase abierta aun no
        /// tiene sesion, su codigo generado: entonces la abre el alumno con el.
        /// Si entra, devuelve la sesion de hoy como VentanaCodigo. La firma no
        /// cambia: deploy\probar_horarios.ps1 la busca por su nombre.
        /// </summary>
        /// <param name="matricula">Si se da, tambien cuenta como "ya registro" una bitacora suya que este en la cola de este equipo sin enviar.</param>
        public ResultadoCodigo ResolverCodigo(string codigo, int idLaboratorio, int idAlumno, string matricula = null)
        {
            codigo = (codigo ?? string.Empty).Trim();
            ModoCodigo modo;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                modo = ModosDeAcceso.Leer(conexion, idLaboratorio);
                if (LaboratoriosDeshabilitados.Esta(conexion, idLaboratorio))
                {
                    return RechazoDeshabilitado(modo);
                }
            }

            ResultadoCodigo resultado = modo == ModoCodigo.Renovable
                ? ResolverCodigoDeSesion(codigo, idLaboratorio, idAlumno, matricula, true)
                : ResolverCodigoFijo(codigo, idLaboratorio, idAlumno, matricula, modo);
            resultado.Modo = modo;
            return resultado;
        }

        private static ResultadoCodigo RechazoDeshabilitado(ModoCodigo modo)
        {
            ResultadoCodigo rechazo = Rechazo(MotivoCodigo.LaboratorioDeshabilitado, LaboratoriosDeshabilitados.Mensaje);
            rechazo.Modo = modo;
            return rechazo;
        }

        /// <summary>El codigo fijo del profesor (lo de siempre).</summary>
        private ResultadoCodigo ResolverCodigoFijo(string codigo, int idLaboratorio, int idAlumno, string matricula, ModoCodigo modo)
        {
            // 1) Las clases con ese codigo: una sola antes de la migracion 09;
            // despues, todas las del profesor.
            List<ClaseDelCodigo> clases = new List<ClaseDelCodigo>();
            DateTime ahora = DateTime.MinValue;
            DateTime hoy = DateTime.MinValue;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string profesorActivo = Esquema.TieneBajas(conexion) ? "u.activo" : "1";
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT c.idclases, c.usuarios_idusuarios, c.grupos_idgrupos, c.materias_id_materia, c.activa, c.codigo,
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
                                Codigo = reader.GetString("codigo"),
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
                // Puede ser el codigo de una sesion de hoy: el nuevo de una clase que
                // se abrio antes de que el laboratorio volviera al codigo fijo.
                return ResolverCodigoDeSesion(codigo, idLaboratorio, idAlumno, matricula, false)
                    ?? Rechazo(MotivoCodigo.NoExiste, ReglasCodigo.NoExisteElCodigo(codigo));
            }

            List<ClaseDelCodigo> activas = clases.Where(c => c.Activa).ToList();
            if (activas.Count == 0)
            {
                return Rechazo(MotivoCodigo.Inactiva, "Ese código ya no está activo. Pregúntale a tu docente.");
            }

            if (!activas.Any(c => hoy.Date >= c.Desde.Date && hoy.Date <= c.Hasta.Date))
            {
                // Las que se dieron de baja el dia que empezaron no tienen periodo que decir.
                List<ClaseDelCodigo> conDias = activas.Where(c => c.Hasta.Date >= c.Desde.Date).ToList();
                if (conDias.Count == 0)
                {
                    return Rechazo(MotivoCodigo.FueraDePeriodo, "Esa clase ya no está en el horario. Pregúntale a tu docente.");
                }

                DateTime desde = conDias.Min(c => c.Desde);
                DateTime hasta = conDias.Max(c => c.Hasta);
                return Rechazo(MotivoCodigo.FueraDePeriodo, "Esa clase no está en el periodo actual (vigente del "
                    + desde.ToString("dd/MM/yyyy") + " al " + hasta.ToString("dd/MM/yyyy") + "). Pregúntale a tu docente.");
            }

            // 2) Candidatas de hoy de esas clases, en todos los laboratorios, y la que toca aqui y ahora.
            List<Candidata> candidatas = LeerCandidatas(codigo);
            ResultadoCodigo rechazo;
            Candidata vigente = ReglasCodigo.Elegir(candidatas, idLaboratorio, ahora.TimeOfDay, false, out rechazo);
            if (vigente == null)
            {
                return rechazo;
            }

            // 3) La sesion de hoy (se crea al primer uso), con la materia y el grupo
            // de SU clase. Lleva el codigo de la clase tal como esta guardado (no
            // como se tecleo); fuera del modo fijo, uno aleatorio: el codigo fijo
            // nunca se escribe en una sesion de un laboratorio que no lo usa.
            ClaseDelCodigo clase = clases.First(c => c.Id == vigente.ClaseId);
            int idSesion = ObtenerOCrearSesion(modo == ModoCodigo.Fijo ? clase.Codigo : null, vigente, clase.MateriaId, clase.GrupoId, clase.ProfesorId);
            return Terminar(idSesion, idAlumno, matricula);
        }

        /// <summary>Una de las clases que comparten el codigo tecleado.</summary>
        private class ClaseDelCodigo
        {
            public int Id;
            public int ProfesorId;
            public int GrupoId;
            public int MateriaId;
            /// <summary>El codigo tal como esta guardado (el alumno lo teclea con cualquier mayuscula).</summary>
            public string Codigo;
            /// <summary>La clase esta activa y su profesor no esta dado de baja.</summary>
            public bool Activa;
            public DateTime Desde;
            public DateTime Hasta;
        }

        /// <summary>
        /// El codigo de una sesion de HOY (generacion de codigos): la busca y
        /// aplica las reglas de siempre (laboratorio, hora, cancelada). Con
        /// diagnosticar (SOLO en un laboratorio con generacion de codigos: aqui no
        /// se vuelve a mirar el modo), si ninguna entra aqui y ahora y es el codigo
        /// generado de una clase abierta de este laboratorio sin sesion, la abre
        /// con el (AbrirConCodigoGenerado); si no, explica por que (CodigoFijo,
        /// SinAbrir o NoExiste). Sin diagnosticar nunca crea nada y, sin ninguna
        /// sesion con ese codigo, devuelve null.
        /// </summary>
        private ResultadoCodigo ResolverCodigoDeSesion(string codigo, int idLaboratorio, int idAlumno, string matricula, bool diagnosticar)
        {
            List<Candidata> candidatas = new List<Candidata>();
            DateTime ahora = DateTime.MinValue;
            if (codigo.Length > 0)
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string profesorActivo = Esquema.TieneBajas(conexion) ? " AND u.activo = 1" : "";
                    // La clase activa y vigente hoy y, la de una franja, la franja
                    // tambien (db/20); en la de una extra h es NULL y su parte no cuenta.
                    bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                    using (MySqlCommand comando = new MySqlCommand(@"
                        SELECT ca.idcodigos_accesos, ca.horarios_idhorarios, ca.horario_excepciones_id, ca.hora_entrada, ca.hora_salida,
                               c.idclases, ca.laboratorios_idlaboratorios AS lab, l.nombre_laboratorio,
                               (xc.idhorario_excepciones IS NOT NULL) AS cancelada, NOW() AS ahora
                        FROM codigos_accesos ca
                        LEFT JOIN horarios h             ON h.idhorarios = ca.horarios_idhorarios
                        LEFT JOIN horario_excepciones x  ON x.idhorario_excepciones = ca.horario_excepciones_id
                        INNER JOIN clases c              ON c.idclases = COALESCE(h.clases_idclases, x.clases_idclases)
                        INNER JOIN usuarios u            ON u.idusuarios = c.usuarios_idusuarios
                        INNER JOIN laboratorios l        ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                        LEFT JOIN horario_excepciones xc ON xc.tipo = 'cancelada' AND xc.horarios_idhorarios = ca.horarios_idhorarios AND xc.fecha = ca.fecha
                        WHERE ca.codigo = @Codigo AND ca.fecha = CURDATE()
                          AND (ca.horarios_idhorarios IS NOT NULL OR ca.horario_excepciones_id IS NOT NULL)
                          AND " + VigenciaFranjas.SeDa(conVigencia, "CURDATE()") + profesorActivo, conexion))
                    {
                        comando.Parameters.AddWithValue("@Codigo", codigo);
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ahora = reader.GetDateTime("ahora");
                                // hora_salida es VARCHAR(45) en la base de la escuela: se lee como venga.
                                TimeSpan? inicio = LeerHora(reader["hora_entrada"]);
                                TimeSpan? fin = LeerHora(reader["hora_salida"]);
                                if (!inicio.HasValue || !fin.HasValue)
                                {
                                    continue;
                                }

                                bool esExtra = !reader.IsDBNull(reader.GetOrdinal("horario_excepciones_id"));
                                candidatas.Add(new Candidata
                                {
                                    ClaseId = reader.GetInt32("idclases"),
                                    EsExtra = esExtra,
                                    Id = esExtra ? Convert.ToInt32(reader["horario_excepciones_id"]) : Convert.ToInt32(reader["horarios_idhorarios"]),
                                    LaboratorioId = Convert.ToInt32(reader["lab"]),
                                    Laboratorio = reader.GetString("nombre_laboratorio"),
                                    Inicio = inicio.Value,
                                    Fin = fin.Value,
                                    Cancelada = Convert.ToInt32(reader["cancelada"]) == 1,
                                    SesionId = Convert.ToInt32(reader["idcodigos_accesos"])
                                });
                            }
                        }
                    }
                }
            }

            if (candidatas.Count == 0 && !diagnosticar)
            {
                return null;
            }

            ResultadoCodigo rechazo = null;
            Candidata vigente = candidatas.Count == 0 ? null : ReglasCodigo.Elegir(candidatas, idLaboratorio, ahora.TimeOfDay, false, out rechazo);
            if (vigente == null && diagnosticar)
            {
                // El codigo generado de una clase vale desde el lunes aunque su
                // profesor aun no la haya abierto (se lo pudo dar el administrador).
                // Tambien si hoy hay sesiones con ese mismo codigo que aqui no entran:
                // los generados solo no se repiten dentro de un laboratorio y dia, asi
                // que puede llevarlo una sesion de otro laboratorio (o una al azar).
                int? abierta = AbrirConCodigoGenerado(codigo, idLaboratorio);
                if (abierta.HasValue)
                {
                    return Terminar(abierta.Value, idAlumno, matricula);
                }

                if (candidatas.Count == 0)
                {
                    return DiagnosticarSinSesion(codigo, idLaboratorio);
                }
            }

            if (vigente == null)
            {
                // El codigo fijo del profesor es tambien el de las sesiones que abrio
                // hoy en un laboratorio con codigo fijo (o en este, antes de cambiarlo).
                // Si aqui no entra porque esa clase ya termino o es de otro
                // laboratorio, lo que hay que decir es que es el fijo; y si su clase de
                // otro laboratorio esta abierta ahora, tambien donde (puede que se
                // sentara en el equivocado). Una cancelada o que aun no empieza se
                // dice tal cual: ese rechazo es el bueno.
                if (diagnosticar && rechazo.Motivo != MotivoCodigo.Cancelada && rechazo.Motivo != MotivoCodigo.AunNoEmpieza)
                {
                    using (MySqlConnection conexion = mconexion.GetConexion())
                    {
                        if (EsCodigoFijo(conexion, codigo))
                        {
                            List<string> abiertaEn = candidatas
                                .Where(c => c.LaboratorioId != idLaboratorio && ReglasCodigo.Abierta(c.Inicio, c.Fin, c.Cancelada, ahora.TimeOfDay))
                                .Select(c => c.Laboratorio).Distinct().ToList();
                            bool soloResponsables = CodigosDeResponsables(conexion);
                            return abiertaEn.Count == 0 ? RechazoCodigoFijo(soloResponsables)
                                : Rechazo(MotivoCodigo.CodigoFijo, "Ese es el código fijo de tu docente: ahora sirve en " + string.Join(" / ", abiertaEn)
                                    + ". Si tu clase es en este laboratorio, aquí cada clase tiene su propio código: "
                                    + (soloResponsables ? "lo da la persona responsable del laboratorio." : "pídeselo a tu docente."));
                        }
                    }
                }

                return rechazo;
            }

            return Terminar(vigente.SesionId.Value, idAlumno, matricula);
        }

        /// <summary>Si es el codigo fijo de alguna clase o de algun profesor (db/09).</summary>
        private static bool EsCodigoFijo(MySqlConnection conexion, string codigo)
        {
            if (codigo.Length == 0)
            {
                return false;
            }

            string fijos = Esquema.CodigoPorProfesor(conexion) ? " + (SELECT COUNT(*) FROM usuarios WHERE codigo_clase = @Codigo)" : "";
            using (MySqlCommand comando = new MySqlCommand("SELECT (SELECT COUNT(*) FROM clases WHERE codigo = @Codigo)" + fijos, conexion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                return Convert.ToInt32(comando.ExecuteScalar()) > 0;
            }
        }

        /// <param name="soloResponsables">CodigosDeResponsables: su docente no ve el codigo de la clase.</param>
        private static ResultadoCodigo RechazoCodigoFijo(bool soloResponsables)
        {
            return Rechazo(MotivoCodigo.CodigoFijo, "Ese es el código fijo de tu docente. En este laboratorio cada clase tiene "
                + (soloResponsables ? "su propio código, que da la persona responsable del laboratorio: pídeselo."
                    : "su propio código, que tu docente ve al entrar a DigLab durante la clase: pídeselo."));
        }

        /// <summary>
        /// "Los docentes no ven los codigos" (CodigosSoloResponsables), leido ahora
        /// para lo que se le dice al alumno: con ella el codigo de la clase lo da la
        /// persona responsable del laboratorio (asi lo dice el panel del docente:
        /// CodigosSoloResponsables.PideElCodigo), no le aparece a su docente al
        /// entrar a DigLab. Sin poder leerlo, lo ultimo que se supo.
        /// </summary>
        private static bool CodigosDeResponsables(MySqlConnection conexion)
        {
            return ModosDeAcceso.LeerAjustes(conexion).SoloResponsables ?? CodigosSoloResponsables.Activa;
        }

        /// <summary>
        /// Generacion de codigos y ninguna sesion de hoy con ese codigo: si es el
        /// generado de una clase abierta de este laboratorio que aun no tiene
        /// sesion, la abre con el, como el panel de su profesor, y devuelve su id.
        /// Si no (o sin secreto), null.
        /// </summary>
        private int? AbrirConCodigoGenerado(string codigo, int idLaboratorio)
        {
            if (codigo.Length != CodigosDeSesion.Largo || !codigo.All(c => c >= '0' && c <= '9'))
            {
                return null;
            }

            SesionAgenda suya;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                DateTime ahora = Ahora(conexion);
                List<SesionAgenda> agenda = LeerAgenda(conexion, idLaboratorio, ahora.Date, true);
                AnotarSesiones(conexion, agenda, ahora.Date, idLaboratorio);
                List<SesionAgenda> sinSesion = ReglasCodigo.Abiertas(agenda, ahora.TimeOfDay).Where(s => !s.SesionId.HasValue && s.Utilizable).ToList();
                if (sinSesion.Count == 0)
                {
                    return null;
                }

                Dictionary<DateTime, Dictionary<string, string>> generados = CodigosGenerados.DeLaboratorio(conexion, idLaboratorio, ahora.Date, ahora.Date);
                if (generados == null)
                {
                    return null;
                }

                Dictionary<string, string> deHoy = generados[ahora.Date];
                suya = sinSesion.FirstOrDefault(s =>
                {
                    string suyo;
                    return deHoy.TryGetValue(CodigosGenerados.Clave(s.EsExtra, s.EsExtra ? s.ExcepcionId.GetValueOrDefault() : s.FranjaId.GetValueOrDefault()), out suyo)
                        && suyo == codigo;
                });
            }

            if (suya == null)
            {
                return null;
            }

            Candidata candidata = new Candidata
            {
                ClaseId = suya.ClaseId,
                EsExtra = suya.EsExtra,
                Id = suya.EsExtra ? suya.ExcepcionId.Value : suya.FranjaId.Value,
                LaboratorioId = idLaboratorio,
                Inicio = suya.Inicio,
                Fin = suya.Fin
            };
            int idSesion = ObtenerOCrearSesion(codigo, candidata, suya.MateriaId, suya.GrupoId, suya.ProfesorId);
            Log.Info("La clase " + suya.Descripcion + " se abrio con su codigo generado al teclearlo un alumno (sesion " + idSesion + ").");
            return idSesion;
        }

        /// <summary>
        /// Generacion de codigos y ninguna sesion de hoy con ese codigo: que
        /// tecleo el codigo fijo de su profesor, que el profesor aun no abre la
        /// clase de ahora, o que esta mal escrito.
        /// </summary>
        private ResultadoCodigo DiagnosticarSinSesion(string codigo, int idLaboratorio)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (EsCodigoFijo(conexion, codigo))
                {
                    return RechazoCodigoFijo(CodigosDeResponsables(conexion));
                }

                DateTime ahora = Ahora(conexion);
                List<SesionAgenda> agenda = LeerAgenda(conexion, idLaboratorio, ahora.Date, true);
                AnotarSesiones(conexion, agenda, ahora.Date, idLaboratorio);
                List<SesionAgenda> abiertas = ReglasCodigo.Abiertas(agenda, ahora.TimeOfDay);
                if (abiertas.Count == 0)
                {
                    // Ninguno sirve ahora: no es que lo haya escrito mal. Tambien tras dar de
                    // baja el horario, con un codigo dictado antes.
                    return Rechazo(MotivoCodigo.NoExiste, "Ahora no hay ninguna clase abierta en este laboratorio, así que ningún código sirve en este momento. Pregúntale a tu docente.");
                }

                SesionAgenda sinAbrir = abiertas.FirstOrDefault(s => !s.SesionId.HasValue);
                if (sinAbrir != null)
                {
                    // Con otra clase ya abierta a la vez (dos seguidas en el margen),
                    // puede que solo se equivocara al teclear el de la suya: las dos
                    // cosas. Si los docentes no ven los codigos, que el suyo aun no
                    // entre no cambia nada: el codigo lo da la persona responsable.
                    bool soloResponsables = CodigosDeResponsables(conexion);
                    return Rechazo(MotivoCodigo.SinAbrir, abiertas.Any(s => s.SesionId.HasValue)
                        ? "Ese no es el código de la clase que ya está abierta: revisa que esté bien escrito. Si vienes a la de las "
                          + Hora(sinAbrir.Inicio) + (soloResponsables ? ", su código lo da la persona responsable del laboratorio."
                              : ", tu docente todavía no entra a DigLab: el código le aparece al entrar.")
                        : "Ese no es el código de la clase de las " + Hora(sinAbrir.Inicio) + ": revisa que esté bien escrito. "
                          + (soloResponsables ? "El código de esa clase lo da la persona responsable del laboratorio: pídeselo."
                              : "Tu docente todavía no entra a DigLab para esa clase: el código le aparece al entrar. Pídeselo."));
                }
            }

            return Rechazo(MotivoCodigo.NoExiste, "Ese no es el código de esta clase. Revisa que esté bien escrito: el código cambia en cada clase.");
        }

        /// <summary>
        /// La sesion aceptada como VentanaCodigo, y si el alumno ya registro en
        /// ella (en la base, o en la cola de este equipo sin enviar: solo vale la primera).
        /// </summary>
        private ResultadoCodigo Terminar(int idSesion, int idAlumno, string matricula)
        {
            Consultas consultas = new Consultas();
            VentanaCodigo ventana = consultas.ObtenerVentanaSesion(idSesion);
            if (ventana == null)
            {
                throw new InvalidOperationException("La sesion " + idSesion + " no se pudo leer.");
            }

            // La sesion guarda las horas que tenia al crearse. Si despues se cambio la
            // franja de hoy y la sesion ya tenia bitacoras (ActualizarFranja solo
            // pone al dia las vacias), sus horas mandan: con ellas se guardara la
            // bitacora, asi que se responde con ellas y no con un Aceptado que
            // fallaria al guardar.
            if (ventana.Estado == EstadoCodigo.Expirado)
            {
                return Rechazo(MotivoCodigo.Expirado, "Tu clase terminó a las " + ventana.Fin.ToString("HH:mm")
                    + ". Pide a tu docente que avise a la administración de DigLab si necesitas una sesión extra.");
            }

            if (ventana.Estado == EstadoCodigo.AunNoEmpieza)
            {
                return Rechazo(MotivoCodigo.AunNoEmpieza, "Tu clase empieza a las " + ventana.Inicio.ToString("HH:mm") + ". Inténtalo más cerca de la hora.");
            }

            ResultadoCodigo resultado = new ResultadoCodigo { Motivo = MotivoCodigo.Aceptado, Ventana = ventana, Mensaje = "" };

            // Una bitacora suya puede estar aun en la cola de este equipo (se cayo
            // la red al guardar): tambien cuenta, solo vale la primera.
            bool enLaCola = !string.IsNullOrEmpty(matricula) && ColaBitacoras.YaRegistrada(SesionLocal.DesdeVentana(ventana).Clave, matricula);
            if (enLaCola || consultas.AlumnoYaRegistro(idSesion, idAlumno))
            {
                ventana.Estado = EstadoCodigo.YaRegistrado;
                resultado.Motivo = MotivoCodigo.YaRegistrado;
                resultado.Mensaje = enLaCola
                    ? "Ya registraste tu bitácora en esta clase (pendiente de enviar al servidor)."
                    : "Ya registraste tu bitácora en esta clase.";
                if (enLaCola)
                {
                    EnviadorBitacoras.Despertar();
                }
            }

            return resultado;
        }

        /// <summary>
        /// Sin codigo (db/11): el alumno eligio esta clase de la lista. Vuelve a
        /// comprobar con el servidor que el laboratorio sigue sin codigo y que la
        /// clase sigue abierta (con los mismos filtros con que se le ofrecio),
        /// crea la sesion de hoy si aun no existe (con un codigo aleatorio) y la
        /// devuelve como VentanaCodigo.
        /// </summary>
        public ResultadoCodigo ResolverClase(int idLaboratorio, int idAlumno, string matricula, int? franjaId, int? excepcionId)
        {
            ModoCodigo modo;
            DateTime ahora;
            List<SesionAgenda> agenda;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                modo = ModosDeAcceso.Leer(conexion, idLaboratorio);
                if (LaboratoriosDeshabilitados.Esta(conexion, idLaboratorio))
                {
                    return RechazoDeshabilitado(modo);
                }

                if (modo != ModoCodigo.SinCodigo)
                {
                    return new ResultadoCodigo
                    {
                        Motivo = MotivoCodigo.ModoCambiado,
                        Modo = modo,
                        Mensaje = modo == ModoCodigo.Renovable
                            ? "Ahora hace falta el código de la clase: " + (CodigosDeResponsables(conexion)
                                ? "pídeselo a la persona responsable del laboratorio." : "pídeselo a tu docente.")
                            : "Ahora hace falta el código de tu docente."
                    };
                }

                ahora = Ahora(conexion);
                agenda = LeerAgenda(conexion, idLaboratorio, ahora.Date, true);
            }

            List<SesionAgenda> abiertas = ReglasCodigo.Abiertas(agenda, ahora.TimeOfDay);
            SesionAgenda elegida = abiertas.FirstOrDefault(s => franjaId.HasValue
                ? !s.EsExtra && s.FranjaId == franjaId
                : s.EsExtra && excepcionId.HasValue && s.ExcepcionId == excepcionId);
            if (elegida == null)
            {
                if (abiertas.Count > 0)
                {
                    return new ResultadoCodigo
                    {
                        Motivo = MotivoCodigo.ElegirClase,
                        Modo = modo,
                        Opciones = abiertas,
                        Mensaje = "Esa clase ya no está abierta. Elige la tuya de la lista."
                    };
                }

                ResultadoCodigo rechazo;
                ReglasCodigo.Elegir(ReglasCodigo.DesdeAgenda(agenda, idLaboratorio, ""), idLaboratorio, ahora.TimeOfDay, false, out rechazo, false);
                rechazo = rechazo ?? Rechazo(MotivoCodigo.HoyNoHayClase, "Hoy no hay clases en este laboratorio.");
                rechazo.Modo = modo;
                return rechazo;
            }

            Candidata candidata = new Candidata
            {
                ClaseId = elegida.ClaseId,
                EsExtra = elegida.EsExtra,
                Id = elegida.EsExtra ? elegida.ExcepcionId.Value : elegida.FranjaId.Value,
                LaboratorioId = idLaboratorio,
                Inicio = elegida.Inicio,
                Fin = elegida.Fin
            };
            int idSesion = ObtenerOCrearSesion(null, candidata, elegida.MateriaId, elegida.GrupoId, elegida.ProfesorId);
            ResultadoCodigo resultado = Terminar(idSesion, idAlumno, matricula);
            resultado.Modo = modo;
            return resultado;
        }

        // ---------------------------------------------------------------------
        // Uso libre: sin codigos y sin clase (db/14)
        // ---------------------------------------------------------------------

        /// <summary>El codigo de las sesiones de uso libre: se ve en Bitacoras, nadie lo teclea.</summary>
        public const string CodigoUsoLibre = "LIBRE";

        /// <summary>
        /// Condicion SQL (sobre codigos_accesos ca): la sesion es de uso libre, sin
        /// profesor, franja ni extra. Las de antes de la fase 6 tambien van sin
        /// franja, pero con su profesor.
        /// </summary>
        internal const string CondicionUsoLibre = "(ca.usuarios_idusuarios IS NULL AND ca.horarios_idhorarios IS NULL AND ca.horario_excepciones_id IS NULL)";

        /// <summary>
        /// Sin codigos y sin ninguna clase abierta en el laboratorio (db/14): la
        /// bitacora queda como uso libre. Vuelve a comprobar con el servidor que el
        /// laboratorio sigue sin codigos y que ahora no hay clase (si la hay, el
        /// alumno se registra en ella: ElegirClase con las abiertas), busca o crea
        /// la sesion de uso libre de este tramo y la devuelve como VentanaCodigo.
        /// </summary>
        public ResultadoCodigo ResolverUsoLibre(int idLaboratorio, int idAlumno, string matricula)
        {
            ModoCodigo modo;
            int idSesion;
            DateTime inicioDelTramo;
            DateTime finDelTramo;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                modo = ModosDeAcceso.Leer(conexion, idLaboratorio);
                // Sin esto, con la agenda vacia todo el dia pareceria tiempo libre.
                if (LaboratoriosDeshabilitados.Esta(conexion, idLaboratorio))
                {
                    return RechazoDeshabilitado(modo);
                }

                if (modo != ModoCodigo.SinCodigo)
                {
                    return new ResultadoCodigo
                    {
                        Motivo = MotivoCodigo.ModoCambiado,
                        Modo = modo,
                        Mensaje = modo == ModoCodigo.Renovable
                            ? "Ahora hace falta el código de la clase: " + (CodigosDeResponsables(conexion)
                                ? "pídeselo a la persona responsable del laboratorio." : "pídeselo a tu docente.")
                            : "Ahora hace falta el código de tu docente."
                    };
                }

                if (!Esquema.UsoLibre(conexion))
                {
                    ResultadoCodigo sinUsoLibre = Rechazo(MotivoCodigo.HoyNoHayClase, "Ahora no hay ninguna clase en este laboratorio.");
                    sinUsoLibre.Modo = modo;
                    return sinUsoLibre;
                }

                DateTime ahora = Ahora(conexion);
                List<SesionAgenda> agenda = LeerAgenda(conexion, idLaboratorio, ahora.Date, true);
                List<SesionAgenda> abiertas = ReglasCodigo.Abiertas(agenda, ahora.TimeOfDay);
                TramoLibre tramo = ReglasCodigo.TramoLibreEn(agenda, ahora.TimeOfDay);
                if (abiertas.Count > 0 || tramo == null)
                {
                    // Empezo una clase mientras tanto: manda la clase.
                    return new ResultadoCodigo
                    {
                        Motivo = MotivoCodigo.ElegirClase,
                        Modo = modo,
                        Opciones = abiertas,
                        Mensaje = "Ya empezó una clase en este laboratorio: tu bitácora va en ella."
                    };
                }

                idSesion = ObtenerOCrearSesionLibre(conexion, idLaboratorio, ahora.Date, tramo, ahora.TimeOfDay, idAlumno);
                inicioDelTramo = ahora.Date + tramo.Inicio;
                finDelTramo = ahora.Date + tramo.Fin;
            }

            ResultadoCodigo resultado = Terminar(idSesion, idAlumno, matricula);
            resultado.Modo = modo;
            // La ventana es la del tramo de AHORA, no la de las horas que se guardaron
            // al crear la sesion (los equipos no las pueden corregir): si despues se
            // cancelo la clase siguiente, el equipo se libera hasta la nueva hora; si
            // se puso una clase antes, solo hasta que se abra.
            if (resultado.Ventana != null)
            {
                resultado.Ventana.Inicio = inicioDelTramo;
                resultado.Ventana.Fin = finDelTramo;
            }

            return resultado;
        }

        /// <summary>
        /// El tramo sin clase en que cae ese momento de ese dia, segun el horario de
        /// AHORA en el servidor (null si a esa hora hay una clase abierta). Para la
        /// cola: una bitacora de uso libre va a la sesion del tramo en que de verdad
        /// cae, aunque despues de registrarla se pusiera o se quitara una clase.
        /// </summary>
        internal static TramoLibre TramoLibreEn(MySqlConnection conexion, int idLaboratorio, DateTime fecha, TimeSpan momento)
        {
            return ReglasCodigo.TramoLibreEn(LeerAgenda(conexion, idLaboratorio, fecha.Date, true), momento);
        }

        /// <summary>
        /// La sesion de uso libre de ese laboratorio, dia y tramo: una que empieza
        /// dentro del tramo (la copia sin conexion tambien la reconoce por el inicio
        /// del tramo, SesionLocal.Clave), primero aquella en que ese alumno ya
        /// registro (si dos tramos se juntaron, no registra dos veces) y si no la mas
        /// antigua. Si no hay, la crea (sin profesor, materia ni grupo, db/14) desde
        /// el inicio del tramo hasta el FIN DEL DIA: su inicio dice de que tramo es,
        /// y donde acaba lo decide el horario de cada momento (la ventana que da
        /// ResolverUsoLibre). Guardar el fin del tramo la hacia vencer si despues se
        /// cancelaba la clase siguiente, y los equipos no pueden corregir las horas
        /// (no pueden cambiar codigos_accesos, db/07). Con un candado de MySQL
        /// (GET_LOCK, no pide permisos) para que dos equipos a la vez no creen dos.
        /// La usan el alumno y la cola.
        /// </summary>
        internal static int ObtenerOCrearSesionLibre(MySqlConnection conexion, int idLaboratorio, DateTime fecha, TramoLibre tramo,
            TimeSpan momento, int? idAlumno)
        {
            string candado = "diglab_libre_" + idLaboratorio.ToString(CultureInfo.InvariantCulture) + "_" + fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            bool tomado = false;
            try
            {
                using (MySqlCommand bloquear = new MySqlCommand("SELECT GET_LOCK(@Nombre, 10)", conexion))
                {
                    bloquear.Parameters.AddWithValue("@Nombre", candado);
                    object resultado = bloquear.ExecuteScalar();
                    // Sin candado (otro equipo lo tiene mas de 10 s) se sigue igual: a lo
                    // sumo saldrian dos sesiones del mismo tramo.
                    tomado = resultado != null && resultado != DBNull.Value && Convert.ToInt32(resultado) == 1;
                }

                int? existente = BuscarSesionLibre(conexion, idLaboratorio, fecha, tramo, momento, idAlumno);
                if (existente.HasValue)
                {
                    return existente.Value;
                }

                using (MySqlCommand insertar = new MySqlCommand(@"
                    INSERT INTO codigos_accesos
                        (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                         laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)
                    VALUES (@Codigo, NOW(), @Fecha, @Inicio, @Fin, NULL, NULL, @Lab, NULL, NULL, NULL);
                    SELECT LAST_INSERT_ID();", conexion))
                {
                    insertar.Parameters.AddWithValue("@Codigo", CodigoUsoLibre);
                    insertar.Parameters.AddWithValue("@Fecha", fecha.Date);
                    insertar.Parameters.AddWithValue("@Inicio", HoraParaGuardar(tramo.Inicio));
                    insertar.Parameters.AddWithValue("@Fin", HoraParaGuardar(ReglasCodigo.FinDelDia));
                    insertar.Parameters.AddWithValue("@Lab", idLaboratorio);
                    int id = Convert.ToInt32(insertar.ExecuteScalar());
                    Log.Info("Sesion " + id + " de uso libre en el laboratorio " + idLaboratorio + " el " + fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                        + " (tramo " + Hora(tramo.Inicio) + "-" + Hora(tramo.Fin) + ").");
                    return id;
                }
            }
            finally
            {
                if (tomado)
                {
                    using (MySqlCommand soltar = new MySqlCommand("SELECT RELEASE_LOCK(@Nombre)", conexion))
                    {
                        soltar.Parameters.AddWithValue("@Nombre", candado);
                        soltar.ExecuteScalar();
                    }
                }
            }
        }

        /// <summary>
        /// La sesion de uso libre (sin profesor, franja ni extra) de ese laboratorio
        /// y dia que empieza dentro del tramo: aquella en que ese alumno ya tiene su
        /// bitacora o, si no, la mas antigua; null si no hay. Una que ya vencio a
        /// esa hora no cuenta: las de antes de guardarse hasta el fin del dia (o las
        /// de un DigLab anterior) guardaban el fin del tramo, y elegirla dejaba a
        /// todos fuera como "expirado". Las horas se comparan aqui: en la base de la
        /// escuela hora_salida es texto.
        /// </summary>
        private static int? BuscarSesionLibre(MySqlConnection conexion, int idLaboratorio, DateTime fecha, TramoLibre tramo, TimeSpan momento, int? idAlumno)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.idcodigos_accesos, ca.hora_entrada, ca.hora_salida,
                       (SELECT COUNT(*) FROM registros_bitacoras rb
                         WHERE rb.fk_codigo_accesos = ca.idcodigos_accesos AND rb.fk_usuario = @Alumno) AS suyas
                FROM codigos_accesos ca
                WHERE ca.laboratorios_idlaboratorios = @Lab AND ca.fecha = @Fecha AND ca.usuarios_idusuarios IS NULL
                  AND ca.horarios_idhorarios IS NULL AND ca.horario_excepciones_id IS NULL
                ORDER BY ca.idcodigos_accesos", conexion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                comando.Parameters.AddWithValue("@Alumno", idAlumno ?? 0);
                int? primera = null;
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        TimeSpan? inicio = LeerHora(reader["hora_entrada"]);
                        TimeSpan? fin = LeerHora(reader["hora_salida"]);
                        if (!inicio.HasValue || inicio.Value < tramo.Inicio || inicio.Value > tramo.Fin || (fin.HasValue && fin.Value < momento))
                        {
                            continue;
                        }

                        int id = Convert.ToInt32(reader["idcodigos_accesos"]);
                        if (Convert.ToInt32(reader["suyas"]) > 0)
                        {
                            return id;
                        }

                        if (!primera.HasValue)
                        {
                            primera = id;
                        }
                    }
                }

                return primera;
            }
        }

        /// <summary>
        /// Lo que la pantalla del alumno necesita de su laboratorio ahora mismo, en
        /// una conexion: la hora del servidor, el modo, la agenda de hoy (lo que el
        /// servidor acepta) con la sesion de cada clase si ya existe, y si la base
        /// guarda los avisos de red (db/10). Con la generacion de codigos, tambien
        /// si los docentes ven los codigos (lo deja en CodigosSoloResponsables.Activa).
        /// </summary>
        public SituacionLaboratorio SituacionDelLaboratorio(int idLaboratorio)
        {
            SituacionLaboratorio situacion = new SituacionLaboratorio();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                situacion.AhoraServidor = Ahora(conexion);
                situacion.Modo = ModosDeAcceso.Leer(conexion, idLaboratorio);
                if (situacion.Modo == ModoCodigo.Renovable)
                {
                    // "Los docentes no ven los codigos": queda en CodigosSoloResponsables.Activa
                    // y la pantalla del alumno dice entonces quien da el codigo de la clase.
                    ModosDeAcceso.LeerAjustes(conexion);
                }

                situacion.Deshabilitado = LaboratoriosDeshabilitados.Esta(conexion, idLaboratorio);
                situacion.ConUsoLibre = !situacion.Deshabilitado && situacion.Modo == ModoCodigo.SinCodigo && Esquema.UsoLibre(conexion);
                situacion.ConAvisosDeRed = Esquema.EquiposSinInternet(conexion);
                if (situacion.ConAvisosDeRed)
                {
                    try
                    {
                        situacion.Equipos = Consultas.EquiposDelLaboratorio(conexion, idLaboratorio);
                    }
                    catch (Exception ex)
                    {
                        // Sin la lista, el alumno escribe el numero: no es motivo para no ensenarle su clase.
                        Log.Aviso("No se pudo leer la lista de equipos del paso 3: " + ex.Message);
                    }
                }

                situacion.Agenda = LeerAgenda(conexion, idLaboratorio, situacion.AhoraServidor.Date, true);
                AnotarSesiones(conexion, situacion.Agenda, situacion.AhoraServidor.Date, idLaboratorio);
            }

            return situacion;
        }

        /// <summary>
        /// Generacion de codigos (db/11): abre la sesion de hoy de esa clase con
        /// su codigo generado de la semana (CodigosGenerados; sin secreto, uno al
        /// azar), o devuelve la que ya existe con el suyo. Todo lo comprueba el
        /// servidor en el mismo INSERT ... SELECT: que el laboratorio este en ese
        /// modo, la hora (desde 15 minutos antes hasta el fin), que la clase este
        /// activa y vigente, que no este cancelada y, si la abre el profesor
        /// (idProfesor mayor que 0), que sea suya. Con 0 la abre el administrador.
        /// </summary>
        public AperturaSesion AbrirSesion(SesionAgenda entrada, int idProfesor)
        {
            Permisos.Exigir("Abrir una clase y ver su codigo");
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                AperturaSesion existente = LeerSesionDeHoy(conexion, entrada);
                if (existente != null)
                {
                    // Ya abierta: su codigo solo se le da a su profesor (o al administrador).
                    return idProfesor > 0 && existente.ProfesorId != idProfesor
                        ? new AperturaSesion { Motivo = "Esa clase no es tuya." }
                        : existente;
                }

                DateTime hoy = Ahora(conexion).Date;
                string codigo = CodigosGenerados.ParaSesion(conexion, entrada.EsExtra,
                        entrada.EsExtra ? entrada.ExcepcionId.GetValueOrDefault() : entrada.FranjaId.GetValueOrDefault(), hoy)
                    ?? CodigosDeSesion.NuevoLibre(conexion, hoy);
                string profesorActivo = Esquema.TieneBajas(conexion) ? " AND u.activo = 1" : "";
                string insertar = entrada.EsExtra
                    ? @"
                    INSERT INTO codigos_accesos
                        (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                         laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)
                    SELECT @Codigo, NOW(), CURDATE(), x.hora_inicio, x.hora_fin, c.materias_id_materia, c.grupos_idgrupos,
                           x.laboratorios_idlaboratorios, c.usuarios_idusuarios, NULL, x.idhorario_excepciones
                    FROM horario_excepciones x
                    INNER JOIN clases c       ON c.idclases = x.clases_idclases
                    INNER JOIN usuarios u     ON u.idusuarios = c.usuarios_idusuarios
                    INNER JOIN laboratorios l ON l.idlaboratorios = x.laboratorios_idlaboratorios
                    WHERE x.idhorario_excepciones = @Id AND x.tipo = 'extra' AND x.fecha = CURDATE()
                      AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta" + profesorActivo + @"
                      AND (@Profesor = 0 OR c.usuarios_idusuarios = @Profesor)
                      AND l.modo_codigo = 'renovable'
                      AND CURTIME() >= SUBTIME(x.hora_inicio, SEC_TO_TIME(@Margen * 60)) AND CURTIME() <= x.hora_fin"
                    : @"
                    INSERT INTO codigos_accesos
                        (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                         laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)
                    SELECT @Codigo, NOW(), CURDATE(), h.hora_inicio, h.hora_fin, c.materias_id_materia, c.grupos_idgrupos,
                           h.laboratorios_idlaboratorios, c.usuarios_idusuarios, h.idhorarios, NULL
                    FROM horarios h
                    INNER JOIN clases c       ON c.idclases = h.clases_idclases
                    INNER JOIN usuarios u     ON u.idusuarios = c.usuarios_idusuarios
                    INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                    WHERE h.idhorarios = @Id AND h.dia_semana = WEEKDAY(CURDATE()) + 1
                      AND " + VigenciaFranjas.SeDa(Esquema.VigenciaDeFranjas(conexion), "CURDATE()") + profesorActivo + @"
                      AND (@Profesor = 0 OR c.usuarios_idusuarios = @Profesor)
                      AND l.modo_codigo = 'renovable'
                      AND CURTIME() >= SUBTIME(h.hora_inicio, SEC_TO_TIME(@Margen * 60)) AND CURTIME() <= h.hora_fin
                      AND NOT EXISTS (SELECT 1 FROM horario_excepciones xc
                                      WHERE xc.tipo = 'cancelada' AND xc.horarios_idhorarios = h.idhorarios AND xc.fecha = CURDATE())";
                try
                {
                    using (MySqlCommand comando = new MySqlCommand(insertar, conexion))
                    {
                        comando.Parameters.AddWithValue("@Codigo", codigo);
                        comando.Parameters.AddWithValue("@Id", entrada.EsExtra ? entrada.ExcepcionId.GetValueOrDefault() : entrada.FranjaId.GetValueOrDefault());
                        comando.Parameters.AddWithValue("@Profesor", idProfesor);
                        comando.Parameters.AddWithValue("@Margen", MargenAntesDelInicioMinutos);
                        if (comando.ExecuteNonQuery() == 1)
                        {
                            int id = (int)comando.LastInsertedId;
                            Log.Info("Sesion " + id + " abierta con el codigo " + codigo + " por " + (idProfesor > 0 ? "el profesor " + idProfesor : "el administrador")
                                + " (" + (entrada.EsExtra ? "extra " + entrada.ExcepcionId : "franja " + entrada.FranjaId) + ").");
                            return new AperturaSesion { SesionId = id, Codigo = codigo };
                        }
                    }
                }
                catch (MySqlException ex) when (ex.Number == 1062)
                {
                    // La abrio a la vez otro panel, el administrador o un equipo: vale esa.
                    AperturaSesion otra = LeerSesionDeHoy(conexion, entrada);
                    if (otra != null)
                    {
                        return idProfesor > 0 && otra.ProfesorId != idProfesor ? new AperturaSesion { Motivo = "Esa clase no es tuya." } : otra;
                    }

                    throw;
                }

                return new AperturaSesion
                {
                    Motivo = "La clase de las " + Hora(entrada.Inicio) + " no se pudo abrir: ya terminó, está cancelada, aún falta para que empiece, "
                        + "la clase está desactivada o fuera de su periodo, o el laboratorio ya no usa la generación de códigos."
                };
            }
        }

        /// <summary>La sesion de hoy de esa clase (franja y fecha, o extra), con su codigo y su profesor, o null.</summary>
        private static AperturaSesion LeerSesionDeHoy(MySqlConnection conexion, SesionAgenda entrada)
        {
            using (MySqlCommand comando = new MySqlCommand(entrada.EsExtra
                ? "SELECT idcodigos_accesos, codigo, usuarios_idusuarios FROM codigos_accesos WHERE horario_excepciones_id = @Id ORDER BY idcodigos_accesos LIMIT 1"
                : "SELECT idcodigos_accesos, codigo, usuarios_idusuarios FROM codigos_accesos WHERE horarios_idhorarios = @Id AND fecha = CURDATE() ORDER BY idcodigos_accesos LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Id", entrada.EsExtra ? entrada.ExcepcionId.GetValueOrDefault() : entrada.FranjaId.GetValueOrDefault());
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new AperturaSesion { SesionId = reader.GetInt32(0), Codigo = reader.GetString(1), ProfesorId = reader.GetInt32(2) };
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Lo llama el panel del profesor al refrescarse: abre sus clases de hoy de
        /// laboratorios con generacion de codigos que ya estan en su ventana
        /// (desde 15 minutos antes) y aun no tienen sesion, y les pone su codigo.
        /// Lo que no se pueda abrir queda en AvisoApertura; nunca lanza.
        /// </summary>
        public void AbrirClasesDeHoy(PanoramaProfesor panorama)
        {
            TimeSpan ahora = panorama.AhoraServidor.TimeOfDay;
            List<string> avisos = new List<string>();
            // Sin las extras de una clase desactivada o fuera de vigencia: el
            // servidor no las deja abrir y se reintentarian en cada refresco.
            foreach (SesionAgenda clase in panorama.Hoy.Where(s => !s.Cancelada && !s.SesionId.HasValue && s.Utilizable
                && panorama.ModoDe(s.LaboratorioId) == ModoCodigo.Renovable
                && ReglasCodigo.Abierta(s.Inicio, s.Fin, false, ahora)).ToList())
            {
                try
                {
                    AperturaSesion apertura = AbrirSesion(clase, panorama.ProfesorId);
                    if (apertura.Abierta)
                    {
                        clase.SesionId = apertura.SesionId;
                        clase.CodigoSesion = apertura.Codigo;
                    }
                    else
                    {
                        avisos.Add(apertura.Motivo);
                    }
                }
                catch (Exception ex)
                {
                    // La causa y que hacer; el detalle tecnico, al registro. Se vuelve
                    // a intentar sola en cada refresco del panel.
                    Log.Error("Abrir la clase " + clase.Descripcion + " desde el panel del profesor", ex);
                    avisos.Add("No se pudo abrir tu clase de las " + Hora(clase.Inicio)
                        + (ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion
                            ? ": no hubo conexión con el servidor. DigLab lo vuelve a intentar solo en unos segundos."
                            : ": el servidor no la dejó abrir. DigLab lo vuelve a intentar solo en unos segundos; si sigue, avisa a la administración de DigLab."));
                }
            }

            panorama.AvisoApertura = avisos.Count == 0 ? null : string.Join(" ", avisos.Distinct());
        }

        private static DateTime Ahora(MySqlConnection conexion)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
            {
                return Convert.ToDateTime(comando.ExecuteScalar());
            }
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
                WHERE c.codigo = @Codigo AND " + VigenciaFranjas.SeDa(Esquema.VigenciaDeFranjas(conexion), "CURDATE()") + @"
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

        /// <summary>
        /// La sesion de hoy de esa clase; si aun no existe, se crea con ese codigo
        /// (null: uno aleatorio, fuera del codigo fijo). Si otro equipo la crea a
        /// la vez, gana la suya (los UNIQUE de codigos_accesos).
        /// </summary>
        private int ObtenerOCrearSesion(string codigoSiSeCrea, Candidata candidata, int idMateria, int idGrupo, int idProfesor)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                int? existente = LeerIdSesion(conexion, candidata);
                if (existente.HasValue)
                {
                    return existente.Value;
                }

                string codigo = codigoSiSeCrea ?? CodigosDeSesion.NuevoLibre(conexion, Ahora(conexion).Date);
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
                        insertar.Parameters.AddWithValue("@Inicio", HoraParaGuardar(candidata.Inicio));
                        insertar.Parameters.AddWithValue("@Fin", HoraParaGuardar(candidata.Fin));
                        insertar.Parameters.AddWithValue("@Materia", idMateria);
                        insertar.Parameters.AddWithValue("@Grupo", idGrupo);
                        insertar.Parameters.AddWithValue("@Lab", candidata.LaboratorioId);
                        insertar.Parameters.AddWithValue("@Profesor", idProfesor);
                        insertar.Parameters.AddWithValue("@Franja", candidata.EsExtra ? (object)DBNull.Value : candidata.Id);
                        insertar.Parameters.AddWithValue("@Excepcion", candidata.EsExtra ? candidata.Id : (object)DBNull.Value);
                        int id = Convert.ToInt32(insertar.ExecuteScalar());
                        Log.Info("Sesion " + id + " creada con el codigo " + codigo + " (" + (candidata.EsExtra ? "extra " : "franja ") + candidata.Id + ").");
                        return id;
                    }
                }
                catch (MySqlException ex) when (ex.Number == 1062)
                {
                    // Otro alumno la creo un instante antes: se lee la suya.
                }

                int? otra = LeerIdSesion(conexion, candidata);
                if (!otra.HasValue)
                {
                    throw new InvalidOperationException("No se pudo crear ni encontrar la sesion de hoy.");
                }

                return otra.Value;
            }
        }

        private static int? LeerIdSesion(MySqlConnection conexion, Candidata candidata)
        {
            using (MySqlCommand leer = new MySqlCommand(candidata.EsExtra
                ? "SELECT idcodigos_accesos FROM codigos_accesos WHERE horario_excepciones_id = @Id ORDER BY idcodigos_accesos LIMIT 1"
                : "SELECT idcodigos_accesos FROM codigos_accesos WHERE horarios_idhorarios = @Id AND fecha = CURDATE() ORDER BY idcodigos_accesos LIMIT 1", conexion))
            {
                leer.Parameters.AddWithValue("@Id", candidata.Id);
                object resultado = leer.ExecuteScalar();
                return resultado == null || resultado == DBNull.Value ? (int?)null : Convert.ToInt32(resultado);
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

        /// <summary>
        /// Una hora para guardar en codigos_accesos: "07:00:00". En la base de la
        /// escuela hora_salida es VARCHAR(45), y un TimeSpan como parametro llegaba
        /// como "0 23:59:59.000000": la lista de Bitacoras no lo entendia ("?") y el
        /// PDF lo imprimia tal cual. En una columna TIME se lee igual.
        /// </summary>
        public static string HoraParaGuardar(TimeSpan hora)
        {
            return hora.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Lee una hora de codigos_accesos venga como venga: TIME (TimeSpan), texto
        /// "15:00" o "07:00:00" (las sesiones de siempre), o "0 23:59:59.000000"
        /// (las que se guardaron mal antes de HoraParaGuardar). Null si no se entiende.
        /// </summary>
        public static TimeSpan? LeerHora(object valor)
        {
            if (valor is TimeSpan)
            {
                return (TimeSpan)valor;
            }

            if (valor == null || valor == DBNull.Value)
            {
                return null;
            }

            string texto = valor.ToString().Trim();
            int espacio = texto.IndexOf(' ');
            if (espacio > 0)
            {
                // "d hh:mm:ss.ffffff": el dia delante, separado por un espacio.
                texto = texto.Substring(espacio + 1);
            }

            TimeSpan hora;
            return TimeSpan.TryParse(texto, CultureInfo.InvariantCulture, out hora) ? hora : (TimeSpan?)null;
        }

        /// <summary>LeerHora para ensenarla: "07:00", o el texto tal cual si no se entiende.</summary>
        public static string HoraLegible(object valor)
        {
            TimeSpan? hora = LeerHora(valor);
            return hora.HasValue ? Hora(hora.Value) : (valor == null || valor == DBNull.Value ? "" : valor.ToString());
        }

        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

        /// <summary>
        /// "Marco Llinas": el primer nombre y el apellido paterno, en mayusculas y
        /// minusculas. Es lo que cabe en un cuadro de la cuadricula; el nombre
        /// entero va en su descripcion emergente.
        /// </summary>
        public static string NombreCorto(string nombre, string paterno)
        {
            string primero = (nombre ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            string corto = (primero + " " + (paterno ?? "").Trim()).Trim();
            return Espanol.TextInfo.ToTitleCase(corto.ToLower(Espanol));
        }

        private static string Texto(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? "" : valor.ToString();
        }

        /// <summary>Los grupos, cada uno con su carrera (puede no tener).</summary>
        public List<OpcionDeCarrera> GruposConCarrera()
        {
            return LeerOpcionesDeCarrera("SELECT idgrupos, nombre_grupo, Carrera_id FROM grupos ORDER BY nombre_grupo");
        }

        /// <summary>Las materias, cada una con su carrera (puede no tener).</summary>
        public List<OpcionDeCarrera> MateriasConCarrera()
        {
            return LeerOpcionesDeCarrera("SELECT id_materia, nombre_materia, idcarreras FROM materias ORDER BY nombre_materia");
        }

        private List<OpcionDeCarrera> LeerOpcionesDeCarrera(string consulta)
        {
            List<OpcionDeCarrera> opciones = new List<OpcionDeCarrera>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    opciones.Add(new OpcionDeCarrera
                    {
                        Id = reader.GetInt32(0),
                        Texto = reader.GetString(1),
                        CarreraId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)
                    });
                }
            }

            return opciones;
        }

        /// <summary>
        /// Lo que necesita ColoresProfesor: los profesores con alguna clase
        /// (activa o no), en el orden en que se dio de alta la primera, y los
        /// colores elegidos a mano. Un profesor nuevo entra al final y los demas
        /// conservan el suyo. Lo que si mueve el orden: pasar a otro profesor la
        /// PRIMERA clase de alguien (Horarios lo deja si no tiene bitacoras), o
        /// borrarla al reimportar el horario; ese profesor cambia de lugar y los
        /// que iban despues de el corren un turno (y de color automatico).
        /// </summary>
        public RepartoDeColores DatosDeColores()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return new RepartoDeColores
                {
                    Orden = LeerOrdenDeProfesores(conexion),
                    Elegidos = LeerColoresElegidos(conexion)
                };
            }
        }

        /// <summary>
        /// El modo de acceso de cada laboratorio, o null sin db/11 (todos con el
        /// codigo fijo). De paso pone al dia "Los docentes no ven los codigos"
        /// (CodigosSoloResponsables.Activa): Mi horario y Horarios lo leen aqui.
        /// </summary>
        public Dictionary<int, ModoCodigo> LeerModos()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                ModosDeAcceso.LeerAjustes(conexion);
                return Esquema.ModoDeAcceso(conexion) ? ModosDeAcceso.LeerTodos(conexion) : null;
            }
        }

        /// <summary>
        /// Generacion de codigos: los de las clases de ese laboratorio entre esas
        /// fechas, SOLO para el administrador. El de la sesion si ya se abrio (es
        /// el que vale) y, si no, el generado. Solo de las semanas que ya
        /// empezaron segun el servidor: los de una semana se generan el lunes. Si
        /// aun no hay secreto, aqui se crea (el usuario del administrador puede).
        /// A quien no los ve (Permisos.VeCodigosGenerados: el encargado de
        /// laboratorio), null sin ir a la base: ni los lee ni crea el secreto.
        /// </summary>
        public CodigosDeLaSemana CodigosGeneradosDe(int idLaboratorio, DateTime desde, DateTime hasta)
        {
            if (!Permisos.VeCodigosGenerados)
            {
                return null;
            }

            CodigosDeLaSemana codigos = new CodigosDeLaSemana();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                codigos.Hoy = Ahora(conexion).Date;
                DateTime ultimo = new[] { hasta.Date, CodigosGenerados.Lunes(codigos.Hoy).AddDays(6) }.Min();
                if (desde.Date > ultimo)
                {
                    return codigos;
                }

                Dictionary<DateTime, Dictionary<string, string>> generados = CodigosGenerados.DeLaboratorio(conexion, idLaboratorio, desde.Date, ultimo);
                codigos.SinSecreto = generados == null;
                if (generados != null)
                {
                    foreach (KeyValuePair<DateTime, Dictionary<string, string>> dia in generados)
                    {
                        foreach (KeyValuePair<string, string> clase in dia.Value)
                        {
                            codigos.Poner(clase.Key[0] == 'x', int.Parse(clase.Key.Substring(1), CultureInfo.InvariantCulture), dia.Key, clase.Value);
                        }
                    }
                }

                // Las sesiones ya abiertas mandan: puede que se abrieran con otro
                // codigo (antes del secreto, o al pasar del codigo fijo a este modo).
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT 0 AS es_extra, ca.horarios_idhorarios AS id, ca.fecha, ca.codigo
                    FROM horarios h
                    INNER JOIN codigos_accesos ca ON ca.horarios_idhorarios = h.idhorarios AND ca.fecha BETWEEN @Desde AND @Hasta
                    WHERE h.laboratorios_idlaboratorios = @Lab
                    UNION ALL
                    SELECT 1, ca.horario_excepciones_id, x.fecha, ca.codigo
                    FROM horario_excepciones x
                    INNER JOIN codigos_accesos ca ON ca.horario_excepciones_id = x.idhorario_excepciones
                    WHERE x.laboratorios_idlaboratorios = @Lab AND x.tipo = 'extra' AND x.fecha BETWEEN @Desde AND @Hasta", conexion))
                {
                    comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                    comando.Parameters.AddWithValue("@Desde", desde.Date);
                    comando.Parameters.AddWithValue("@Hasta", ultimo);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string codigo = reader.IsDBNull(reader.GetOrdinal("codigo")) ? "" : reader.GetString("codigo");
                            if (codigo.Length > 0)
                            {
                                codigos.Poner(Convert.ToInt32(reader["es_extra"]) == 1, Convert.ToInt32(reader["id"]), reader.GetDateTime("fecha"), codigo);
                            }
                        }
                    }
                }
            }

            return codigos;
        }

        /// <summary>El color elegido de cada profesor que tiene uno (db/12); sin la migracion, ninguno.</summary>
        private static Dictionary<int, string> LeerColoresElegidos(MySqlConnection conexion)
        {
            Dictionary<int, string> colores = new Dictionary<int, string>();
            if (!Esquema.ColorDeProfesor(conexion))
            {
                return colores;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idusuarios, color_horario FROM usuarios WHERE fk_tipo_usuario = 2 AND color_horario IS NOT NULL", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    colores[reader.GetInt32(0)] = reader.GetString(1);
                }
            }

            return colores;
        }

        private static List<int> LeerOrdenDeProfesores(MySqlConnection conexion)
        {
            List<int> ids = new List<int>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT usuarios_idusuarios FROM clases GROUP BY usuarios_idusuarios ORDER BY MIN(idclases)", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetInt32(0));
                }
            }

            return ids;
        }
    }
}
