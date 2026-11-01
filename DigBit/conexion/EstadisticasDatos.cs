using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    // =========================================================================
    // Estadisticas (paquete 2): lo que se lee de la base y lo que se calcula
    // para la ventana (VentanaEstadisticas). Leer va en segundo plano y guarda la
    // ultima lectura (10 minutos, misma cuenta y mismo dia del servidor); Calcular
    // no consulta nada: cambiar laboratorio, docente o grupo nunca vuelve a la
    // base. La asistencia se cuenta con el mismo codigo que Mis grupos
    // (AsistenciaDatos.LeerClasesEnLote, LeerElRestoEnLote, CalcularTodas y
    // ResumenAsistencia.De) y "Clases con bitacoras" como la pestana Faltas
    // (LeerCalendario y ResumenDia).
    // =========================================================================

    /// <summary>Los periodos del combo, en este orden (R2).</summary>
    internal enum TipoPeriodo
    {
        /// <summary>"Este ciclo": de ciclo_desde (clases vigentes hoy) a hoy; entre ciclos, el ultimo que termino.</summary>
        Ciclo,
        /// <summary>Un ciclo que ya termino (LecturaEstadisticas.CiclosPasados): del primer al ultimo dia, solo sus clases.</summary>
        CicloPasado = 1,
        Mes,
        Semana,
        Ultimos30,
        /// <summary>"Elegir fechas...": dos dias cualesquiera hasta hoy, con como mucho 400 entre ellos, o desde un dia hasta hoy (HastaHoy).</summary>
        Fechas,
        /// <summary>
        /// Una plantilla de horario aplicada que ya empezo (LecturaEstadisticas.Plantillas): solo los
        /// dias en que valio su horario, contados como unas fechas elegidas, con su nombre (2026-11-01).
        /// </summary>
        Plantilla
    }

    /// <summary>Las siete pestanas, en su orden.</summary>
    internal enum PestanaEstadisticas
    {
        Estudiantes,
        Clases,
        Grupos,
        Docentes,
        Laboratorios,
        Equipos,
        /// <summary>Las cuatro secciones con graficas y el reporte para auditoria (2026-10-31).</summary>
        Graficas
    }

    /// <summary>Como se agrupan en el tiempo las graficas y el reporte para auditoria.</summary>
    internal enum AgrupacionGraficas
    {
        /// <summary>De lunes a domingo; la primera y la ultima, recortadas al periodo.</summary>
        Semana,
        /// <summary>Del dia 1 al ultimo del mes; el primero y el ultimo, recortados al periodo.</summary>
        Mes
    }

    /// <summary>
    /// Una semana o un mes del periodo (las graficas de Graficas y el reporte para
    /// auditoria). Las clases se cuentan como Faltas y "Clases que se dieron" (con
    /// los mismos estados del calendario); la asistencia, dia por dia como C1 (los
    /// dias de cada lista que caen en el tramo). Sumados todos los tramos dan las
    /// cifras grandes del mismo filtro.
    /// </summary>
    internal sealed class TramoDelPeriodo
    {
        /// <summary>El primer y el ultimo dia del tramo dentro del periodo.</summary>
        public DateTime Desde;
        public DateTime Hasta;
        /// <summary>"05/10" (semana: su primer dia en el periodo) u "oct 2026".</summary>
        public string Etiqueta = "";
        /// <summary>"Del 05/10 al 11/10/2026" u "Octubre de 2026" (con sus dias si esta recortado).</summary>
        public string EtiquetaLarga = "";
        public int ConBitacoras;
        /// <summary>Sin bitacoras, con asistencias que dio su docente (ambar en Faltas): se dieron.</summary>
        public int ConDadas;
        /// <summary>Ni bitacoras ni asistencias dadas (rojo en Faltas).</summary>
        public int SinRegistro;
        public int Canceladas;
        /// <summary>Las horas de esas clases: las que contaban, las que se dieron y las canceladas.</summary>
        public double HorasDelHorario;
        public double HorasUsadas;
        public double HorasCanceladas;
        /// <summary>Asistencia de los estudiantes: entraron X de N veces en los dias del tramo.</summary>
        public int Entraron;
        public int Veces;

        public int QueContaban
        {
            get { return ConBitacoras + ConDadas + SinRegistro; }
        }

        public int SeDieron
        {
            get { return ConBitacoras + ConDadas; }
        }

        /// <summary>Horas usadas de las del horario (0 a 100), o null sin horas del horario.</summary>
        public double? Ocupacion
        {
            get { return HorasDelHorario <= 0 ? (double?)null : 100.0 * HorasUsadas / HorasDelHorario; }
        }

        public double? Asistencia
        {
            get { return Veces == 0 ? (double?)null : 100.0 * Entraron / Veces; }
        }

        /// <summary>Una clase-dia del calendario, con el mismo reparto que ResumenDia.Contar.</summary>
        internal void Contar(EstadoEnCalendario estado, double horas)
        {
            switch (estado)
            {
                case EstadoEnCalendario.ConBitacoras:
                    ConBitacoras++;
                    HorasDelHorario += horas;
                    HorasUsadas += horas;
                    break;
                case EstadoEnCalendario.SinBitacorasConAsistencia:
                    ConDadas++;
                    HorasDelHorario += horas;
                    HorasUsadas += horas;
                    break;
                case EstadoEnCalendario.SinSesion:
                case EstadoEnCalendario.SesionVacia:
                    SinRegistro++;
                    HorasDelHorario += horas;
                    break;
                case EstadoEnCalendario.Cancelada:
                    Canceladas++;
                    HorasCanceladas += horas;
                    break;
            }
        }
    }

    /// <summary>
    /// El uso del horario de un laboratorio en el periodo y los filtros (seccion 1 de
    /// Graficas): sus clases como la pestana Laboratorios (Se dieron X de N) y sus
    /// horas. Sale del mismo acumulado que FilaLaboratorio.
    /// </summary>
    internal sealed class UsoDeLaboratorio
    {
        /// <summary>0 en el total (todos los laboratorios del filtro).</summary>
        public int LaboratorioId;
        public string Laboratorio = "";
        /// <summary>Sin EmpezoEl: aun no registra sus clases con DigBit (sus clases sin bitacoras no cuentan, como en Faltas).</summary>
        public bool AunNo;
        public int ConBitacoras;
        public int ConDadas;
        public int SinRegistro;
        public int Canceladas;
        public double HorasDelHorario;
        public double HorasUsadas;
        public double HorasCanceladas;

        public int QueContaban
        {
            get { return ConBitacoras + ConDadas + SinRegistro; }
        }

        public int SeDieron
        {
            get { return ConBitacoras + ConDadas; }
        }

        public double? Ocupacion
        {
            get { return HorasDelHorario <= 0 ? (double?)null : 100.0 * HorasUsadas / HorasDelHorario; }
        }
    }

    /// <summary>Lo que solo usan las graficas (pestana Graficas y el reporte para auditoria).</summary>
    internal sealed class GraficasEstadisticas
    {
        /// <summary>Por semana hasta 16 semanas de periodo; mas largo, por mes.</summary>
        public AgrupacionGraficas Sugerida;
        public List<TramoDelPeriodo> PorSemana = new List<TramoDelPeriodo>();
        public List<TramoDelPeriodo> PorMes = new List<TramoDelPeriodo>();
        /// <summary>Los laboratorios con clases del horario en el periodo (con el filtro de laboratorio, solo ese), de mas a menos horas.</summary>
        public List<UsoDeLaboratorio> Laboratorios = new List<UsoDeLaboratorio>();
        /// <summary>Los mismos sumados: el de C2 (todos o el laboratorio elegido).</summary>
        public UsoDeLaboratorio Total = new UsoDeLaboratorio();
        /// <summary>Activos que aun no registran sus clases con DigBit (con el filtro, solo ese): sus clases no cuentan.</summary>
        public List<string> SinDigBit = new List<string>();

        public List<TramoDelPeriodo> Tramos(AgrupacionGraficas agrupacion)
        {
            return agrupacion == AgrupacionGraficas.Mes ? PorMes : PorSemana;
        }
    }

    /// <summary>
    /// Un ciclo escolar (EstadisticasDatos.ArmarCiclos, sin base): uno que ya termino
    /// (LecturaEstadisticas.CiclosPasados) o el que esta en curso (CicloEnCurso). Sale
    /// de las fechas de las clases; de los ciclos aplicados en Horarios > Aplicacion
    /// solo se toma el nombre.
    /// </summary>
    internal sealed class CicloEscolar
    {
        /// <summary>El que se le puso en Horarios > Aplicacion (NombreAplicado) o el de sus meses ("Septiembre 2026-febrero 2027", CicloPreliminarDatos.NombrePropuesto).</summary>
        public string Nombre = "";
        /// <summary>El nombre es el del ciclo aplicado con ese primer dia (db/16 y db/23), no el de sus meses.</summary>
        public bool NombreAplicado;
        /// <summary>Otro ciclo pasado se llama igual (sin distinguir mayusculas): el combo le pone "(desde el ...)".</summary>
        public bool NombreRepetido;
        /// <summary>El primer dia: el de su primera clase con horario.</summary>
        public DateTime Desde;
        /// <summary>El ultimo dia: el vigente_hasta que mas se repite en sus clases con horario (con empate, el mas tardio).</summary>
        public DateTime Hasta;
        /// <summary>El ultimo dia de sus clases (con SiguenDespues, despues de Hasta).</summary>
        public DateTime HastaDeSusClases;
        /// <summary>Sus clases que siguieron despues de Hasta: esos dias no entran en ningun ciclo.</summary>
        public int SiguenDespues;
        /// <summary>Solo en "Este ciclo" (CicloEnCursoDe): sus clases que siguen hoy (vigente_hasta >= hoy); 0 en los pasados.</summary>
        public int SiguenHoy;
        /// <summary>Sus clases (idclases): las que empezaron desde Desde y antes del ciclo siguiente.</summary>
        public HashSet<int> Clases = new HashSet<int>();
    }

    /// <summary>
    /// Una plantilla de horario aplicada en Horarios > Aplicacion (ciclos_preliminares con
    /// estado 'publicado', db/16 y db/23) que ya empezo, como periodo propio (2026-11-01): al
    /// sustituir la plantilla entera todo empieza de cero (clases nuevas). Sus dias son solo
    /// los de esa plantilla (EstadisticasDatos.ArmarPlantillas, sin base): de su desde al
    /// menor de su hasta, el fin_anterior de la aplicada siguiente (el dia en que dejo de
    /// valer porque la sustituyeron) y hoy. Una que se deshizo vuelve a 'borrador': no cuenta.
    /// </summary>
    internal sealed class PlantillaAplicada
    {
        /// <summary>ciclos_preliminares.idciclo: la identifica en una lectura nueva (el nombre se puede cambiar).</summary>
        public int Id;
        /// <summary>El nombre con que quedo en el registro (unico entre las aplicadas).</summary>
        public string Nombre = "";
        /// <summary>Su primer dia (desde).</summary>
        public DateTime Desde;
        /// <summary>El ultimo dia que cuenta: UltimoDia, o hoy si aun no llega.</summary>
        public DateTime Hasta;
        /// <summary>El ultimo dia de su horario: su hasta o, si otra la sustituyo antes, el fin_anterior de esa (puede ser despues de hoy).</summary>
        public DateTime UltimoDia;
        /// <summary>El hasta de sus clases al aplicarla.</summary>
        public DateTime HastaDelHorario;
        /// <summary>La aplicada que la sustituyo antes de su hasta (tambien una que aun no empieza); "" si nadie la corto.</summary>
        public string SustituidaPor = "";

        /// <summary>Su horario sigue valiendo hoy (UltimoDia >= hoy): el periodo llega a hoy y avanza solo.</summary>
        public bool EnCurso;
    }

    /// <summary>
    /// El periodo que se pide. No cambia despues de crearlo (De, EntreFechas,
    /// DesdeHastaHoy, DelCiclo o DePlantilla). Con Fechas lleva sus dias; con un ciclo
    /// pasado, Desde identifica al ciclo en una lectura nueva y Nombre es el texto del
    /// combo mientras se lee; con una plantilla, la identifica Plantilla (su idciclo) y sus
    /// dias salen de la lectura; con los demas, los dias salen del reloj del servidor y de
    /// E0 al leer (EstadisticasDatos.Resolver).
    /// </summary>
    internal sealed class PeriodoElegido
    {
        public readonly TipoPeriodo Tipo;
        /// <summary>Con Fechas, el primero y el ultimo dia, incluidos (con HastaHoy, Hasta = Desde: el ultimo es hoy). Con CicloPasado o Plantilla, los suyos al elegirlo.</summary>
        public readonly DateTime Desde;
        public readonly DateTime Hasta;
        /// <summary>Con Fechas: el ultimo dia es siempre hoy (avanza solo cada dia).</summary>
        public readonly bool HastaHoy;
        /// <summary>Con CicloPasado o Plantilla: el texto del combo al elegirlo (TextoDeCiclo o TextoDePlantilla); "" en los demas.</summary>
        public readonly string Nombre;
        /// <summary>Con Plantilla: su idciclo (ciclos_preliminares); 0 en los demas.</summary>
        public readonly int Plantilla;

        private PeriodoElegido(TipoPeriodo tipo, DateTime desde, DateTime hasta, bool hastaHoy, string nombre)
            : this(tipo, desde, hasta, hastaHoy, nombre, 0)
        {
        }

        private PeriodoElegido(TipoPeriodo tipo, DateTime desde, DateTime hasta, bool hastaHoy, string nombre, int plantilla)
        {
            Tipo = tipo;
            Desde = desde.Date;
            Hasta = hasta.Date;
            HastaHoy = hastaHoy;
            Nombre = nombre ?? "";
            Plantilla = plantilla;
        }

        public static PeriodoElegido De(TipoPeriodo tipo)
        {
            return new PeriodoElegido(tipo, DateTime.MinValue, DateTime.MinValue, false, "");
        }

        public static PeriodoElegido EntreFechas(DateTime desde, DateTime hasta)
        {
            return new PeriodoElegido(TipoPeriodo.Fechas, desde, hasta, false, "");
        }

        /// <summary>"Del 01/10/2026 a hoy": desde ese dia y hasta hoy, que avanza solo.</summary>
        public static PeriodoElegido DesdeHastaHoy(DateTime desde)
        {
            return new PeriodoElegido(TipoPeriodo.Fechas, desde, desde, true, "");
        }

        /// <summary>Un ciclo que ya termino, del primer al ultimo dia y solo con sus clases.</summary>
        public static PeriodoElegido DelCiclo(CicloEscolar ciclo)
        {
            if (ciclo == null)
            {
                throw new ArgumentNullException("ciclo");
            }

            return new PeriodoElegido(TipoPeriodo.CicloPasado, ciclo.Desde, ciclo.Hasta, false, EstadisticasDatos.TextoDeCiclo(ciclo));
        }

        /// <summary>Una plantilla aplicada: los dias en que valio su horario, contados como unas fechas elegidas.</summary>
        public static PeriodoElegido DePlantilla(PlantillaAplicada plantilla)
        {
            if (plantilla == null)
            {
                throw new ArgumentNullException("plantilla");
            }

            return new PeriodoElegido(TipoPeriodo.Plantilla, plantilla.Desde, plantilla.Hasta, false, EstadisticasDatos.TextoDePlantilla(plantilla), plantilla.Id);
        }
    }

    /// <summary>
    /// El periodo y los filtros con que se calcula (EstadisticasDatos.Resolver). Desde
    /// es el del periodo sin recortar: con un periodo de mas de 400 dias los dias que
    /// cuentan empiezan en su ultimo dia - 399 (Calculo). Las clases de un ciclo se
    /// eligen con el (revision de E1, punto 5); las de unas fechas, con el primer dia
    /// que cuenta (EstadisticasDatos.DesdeDeLasClases).
    /// </summary>
    internal sealed class FiltroEstadisticas
    {
        /// <summary>El que quedo: "Este ciclo" sin clases vigentes hoy pasa al ultimo ciclo que termino o a "Ultimos 30 dias".</summary>
        public TipoPeriodo Tipo;
        public DateTime Desde;
        public DateTime Hasta;
        /// <summary>"Este ciclo" o un ciclo pasado: solo son del periodo las clases del ciclo (R2).</summary>
        public bool EsCiclo;
        /// <summary>Solo con CicloPasado: el ciclo (sus clases son SoloEstas); null en lo demas.</summary>
        public CicloEscolar Ciclo;
        /// <summary>Solo con Plantilla: la plantilla aplicada (sus dias son Desde y Hasta; se calcula como unas fechas); null en lo demas.</summary>
        public PlantillaAplicada Plantilla;
        /// <summary>Con Fechas: el ultimo dia es siempre hoy.</summary>
        public bool HastaHoy;
        /// <summary>0 = todos.</summary>
        public int Laboratorio;
        public int Docente;
        public int Grupo;
        /// <summary>La persona responsable del laboratorio (Permisos.SoloConsulta): cambia algun texto y lo que se guarda en CSV.</summary>
        public bool SoloConsulta;
        /// <summary>El docente o el grupo elegido no tiene clases en el periodo y se volvio a "Todos": lo que dice lblResumen, o null.</summary>
        public string Aviso;
        /// <summary>
        /// Lo que dice lblResumen del periodo, antes del Aviso: que se ve otro ("Hoy no hay
        /// ningun ciclo en curso...", "Ese ciclo ya no esta en la lista...") o que unas fechas
        /// son las de un ciclo; o null.
        /// </summary>
        public string AvisoDelPeriodo;

        /// <summary>Las clases del ciclo pasado, o null: con "Este ciclo" las del ciclo salen por fechas (R2).</summary>
        public ISet<int> SoloEstas
        {
            get { return Ciclo == null ? null : Ciclo.Clases; }
        }
    }

    /// <summary>Un laboratorio (E5) y desde cuando registra sus clases con DigBit (LeerCalendario.EmpezoEl).</summary>
    internal sealed class LaboratorioLeido
    {
        public int Id;
        public string Nombre = "";
        /// <summary>Sin db/12 (laboratorios desactivables), siempre true.</summary>
        public bool Activo = true;
        /// <summary>Primera clase del horario con bitacoras en DigBit (el uso libre no cuenta), o null.</summary>
        public DateTime? EmpezoEl;
    }

    /// <summary>Un reporte de falla de un docente (E8, db/15).</summary>
    internal sealed class ReporteDeDocente
    {
        public int Id;
        public int SesionId;
        /// <summary>reportes_profesor.fk_usuario: el docente que lo reporto.</summary>
        public int DocenteId;
        /// <summary>El equipo que nombra, sin espacios a los lados; null si es de todo el laboratorio.</summary>
        public string Equipo;
        /// <summary>red, hardware, software u otro.</summary>
        public string Area = "";
        public string Falla = "";
        public DateTime? RegistradoEn;
        public int? LaboratorioId;
        /// <summary>El dia de su sesion (VigenciaFranjas.DiaDeSesion).</summary>
        public DateTime Dia;
    }

    /// <summary>Una bitacora con falla o con aviso de equipos sin internet (E10).</summary>
    internal sealed class DetalleDeReporte
    {
        /// <summary>registros_bitacoras.idregistros_bitacoras (el mismo de E6).</summary>
        public int Id;
        public int SesionId;
        public string Equipo = "";
        public bool ConFalla;
        /// <summary>La lista de equipos que aviso sin internet ("PC-09,PC-13"), o null (db/10).</summary>
        public string SinInternet;
        /// <summary>Cuando se registro (registrado_en; recibido_en si falta), o null.</summary>
        public DateTime? Cuando;
        /// <summary>"Red: Sin cable de red | Hardware: No sirve el mouse" (Consultas.DescribirFallas sin el aviso), o null sin falla.</summary>
        public string QueReporto;
    }

    /// <summary>
    /// Todo lo leido para Estadisticas, en una conexion (EstadisticasDatos.Leer). No
    /// depende del periodo elegido: sirve para cualquier periodo que cubra
    /// (EstadisticasDatos.Cubre). Nadie la cambia despues de leerla.
    /// </summary>
    internal sealed class LecturaEstadisticas
    {
        /// <summary>Esquema.CuentaDe(conexion): la cache no se reutiliza con otra cuenta.</summary>
        public string Cuenta = "";
        /// <summary>La misma cuenta vista sin abrir la conexion (EstadisticasDatos.Leer: reutilizarla sin ir a la base); null si no se supo.</summary>
        internal string CuentaSinConexion;
        /// <summary>E1 a E4, E6, E7, E11 y los modos (paquete 1).</summary>
        public LecturaDeAsistencia Asistencia;
        /// <summary>CalcularTodas, con los tramos de hoy de cada clase (TramosDeHoy).</summary>
        public Dictionary<int, AsistenciaDeClase> Listas = new Dictionary<int, AsistenciaDeClase>();
        /// <summary>
        /// LeerCalendario(conexion, desde, hasta, false), como Faltas con la casilla sin marcar:
        /// con los periodos que llegan a hoy, de max(DesdeLectura, hoy - 399) a hoy; con uno que
        /// termino antes, hasta HastaLectura (o su ultimo dia), con 400 dias como mucho.
        /// </summary>
        public CalendarioBitacoras Calendario;
        /// <summary>E5 con EmpezoEl.</summary>
        public Dictionary<int, LaboratorioLeido> Laboratorios = new Dictionary<int, LaboratorioLeido>();
        /// <summary>E8 (vacia sin db/15).</summary>
        public List<ReporteDeDocente> ReportesDocentes = new List<ReporteDeDocente>();
        /// <summary>E10 por id de bitacora.</summary>
        public Dictionary<int, DetalleDeReporte> Detalles = new Dictionary<int, DetalleDeReporte>();
        /// <summary>E0: el primer y el ultimo dia de las clases activas vigentes hoy ("Este ciclo"); null entre ciclos.</summary>
        public DateTime? CicloDesde;
        public DateTime? CicloHasta;
        /// <summary>Los ciclos que ya terminaron (EstadisticasDatos.ArmarCiclos), del mas reciente al mas viejo.</summary>
        public List<CicloEscolar> CiclosPasados = new List<CicloEscolar>();
        /// <summary>"Este ciclo" (EstadisticasDatos.CicloEnCursoDe), o null sin ciclo hoy.</summary>
        public CicloEscolar CicloEnCurso;
        /// <summary>
        /// Las plantillas aplicadas que ya empezaron (EstadisticasDatos.ArmarPlantillas), de la mas
        /// reciente a la mas vieja; vacia sin db/16 y db/23 o si la cuenta no puede leerlas.
        /// </summary>
        public List<PlantillaAplicada> Plantillas = new List<PlantillaAplicada>();
        /// <summary>Esquema.ReportesDeProfesor (db/15) y Esquema.EquiposSinInternet (db/10).</summary>
        public bool ConReportesDocentes;
        public bool ConAvisos;
        /// <summary>Cuando se leyo (reloj de este equipo, en UTC): la cache dura 10 minutos.</summary>
        public DateTime LeidaUtc;
        /// <summary>Lo que tardo la lectura entera.</summary>
        public TimeSpan Duracion;

        /// <summary>El reloj del servidor al leer (R1).</summary>
        public DateTime AhoraServidor
        {
            get { return Asistencia.AhoraServidor; }
        }

        public DateTime Hoy
        {
            get { return Asistencia.AhoraServidor.Date; }
        }

        /// <summary>Esquema.AsistenciaDada (db/22).</summary>
        public bool ConDadas
        {
            get { return Asistencia.ConDadas; }
        }

        /// <summary>Lo leido empieza aqui (R3); acaba en HastaLectura.</summary>
        public DateTime DesdeLectura
        {
            get { return Asistencia.DesdeLectura; }
        }

        /// <summary>Lo leido acaba aqui: hoy, o el ultimo dia de las clases de un periodo que termino antes (AsistenciaDatos.HastaLecturaDe).</summary>
        public DateTime HastaLectura
        {
            get { return Asistencia.HastaLectura; }
        }
    }

    /// <summary>Color del numero de una cifra (CifraGrande.ColorVerde, ColorRojo, ColorNaranja o ColorGris).</summary>
    internal enum TonoCifra
    {
        Verde,
        Rojo,
        Naranja,
        Gris
    }

    /// <summary>Una de las cinco cifras grandes, ya en texto (CifraGrande.Mostrar).</summary>
    internal sealed class CifraEstadistica
    {
        /// <summary>"84 %", "128 de 140", "1,284" o "-".</summary>
        public string Valor = EstadisticasDatos.SinDato;
        /// <summary>En minusculas: "asistencia de los estudiantes".</summary>
        public string Etiqueta = "";
        /// <summary>Un renglon que cabe a 1366 (unos 238 px a 8.5 pt); vacio con "-".</summary>
        public string Detalle = "";
        /// <summary>El tramo del detalle que va en rojo, o null.</summary>
        public string ParteRoja;
        public TonoCifra Tono = TonoCifra.Gris;
        /// <summary>El ToolTip: como se cuenta, con las cifras enteras.</summary>
        public string Ayuda = "";
        /// <summary>La pestana que elige al pulsarla (sec. 3.1).</summary>
        public PestanaEstadisticas Pestana;

        public bool SinDatoQueMostrar
        {
            get { return Valor == EstadisticasDatos.SinDato; }
        }
    }

    /// <summary>Las cinco cifras (sec. 3.1), con sus numeros sueltos para quien los necesite.</summary>
    internal sealed class CifrasEstadisticas
    {
        public CifraEstadistica Asistencia = new CifraEstadistica();
        public CifraEstadistica ClasesConBitacoras = new CifraEstadistica();
        public CifraEstadistica Bitacoras = new CifraEstadistica();
        public CifraEstadistica Fallas = new CifraEstadistica();
        public CifraEstadistica SinInternet = new CifraEstadistica();
        /// <summary>
        /// La de "Clases que se dieron" (pedido del usuario, 2026-10-22), lista para
        /// ponerla en lugar de C2 o junto a ella. No esta en Todas.
        /// </summary>
        public CifraEstadistica ClasesQueSeDieronCifra = new CifraEstadistica();

        // C1: entraron X de N veces; estudiantes distintos bajo el 80 %; asistencias dadas que cuentan.
        public int Entraron;
        public int Veces;
        public int EstudiantesBajo;
        public int Dadas;
        // C2, como Faltas.
        public int ClasesConBitacorasX;
        public int ClasesQueContaban;
        public int ClasesSinBitacoras;
        public int ClasesConDadas;
        public int Canceladas;
        public int PorVenir;
        public int NoContaban;

        /// <summary>
        /// "Clases que se dieron" (pedido del usuario, 2026-10-22: cuantas veces asisten
        /// los docentes, X de X clases): con bitacoras o, sin ellas, con asistencias que
        /// dio su docente (las ambar de Faltas). De ClasesQueContaban. Ayuda: EstadisticasDatos.AyudaClasesQueSeDieron.
        /// </summary>
        public int ClasesQueSeDieron
        {
            get { return ClasesConBitacorasX + ClasesConDadas; }
        }

        /// <summary>Ni bitacoras ni asistencias dadas (en rojo): puede que no se diera o que nadie registrara. Es ClasesSinBitacoras.</summary>
        public int ClasesSinRegistro
        {
            get { return ClasesSinBitacoras; }
        }

        /// <summary>ClasesQueSeDieron de ClasesQueContaban, de 0 a 1 (para la barra), o null sin clases que contaban.</summary>
        public double? FraccionClasesQueSeDieron
        {
            get { return ClasesQueContaban == 0 ? (double?)null : (double)ClasesQueSeDieron / ClasesQueContaban; }
        }

        // C3.
        public int BitacorasRegistradas;
        public int EstudiantesDistintos;
        // C4.
        public int FallasReportadas;
        public int FallasDeEstudiantes;
        public int FallasDeDocentes;
        public int Hardware;
        public int Software;
        public int Red;
        // C5 (null sin db/10).
        public int? EquiposSinInternet;
        public int AvisosSinInternet;

        /// <summary>Las cinco, de izquierda a derecha.</summary>
        public CifraEstadistica[] Todas
        {
            get { return new[] { Asistencia, ClasesConBitacoras, Bitacoras, Fallas, SinInternet }; }
        }
    }

    /// <summary>
    /// Un estudiante en una clase, en el filtro: una fila de "Sus clases" (con N > 0)
    /// o de "Estudiantes de la clase" (toda la lista, tambien con N = 0).
    /// </summary>
    internal sealed class ParDeAsistencia
    {
        public int ClaseId;
        public int AlumnoId;
        public string Matricula = "";
        /// <summary>"Apellidos Nombre", como Mis grupos.</summary>
        public string Nombre = "";
        /// <summary>False si su cuenta esta de baja (db/08).</summary>
        public bool Activo = true;
        public string Materia = "";
        public string Grupo = "";
        /// <summary>"Nombre Apellidos".</summary>
        public string Docente = "";
        /// <summary>Entro X de N veces (ResumenAsistencia.De con los dias del filtro).</summary>
        public int Entro;
        public int Veces;
        /// <summary>Asistencias quitadas por el docente en el filtro (cuentan como falta, db/17).</summary>
        public int Retiradas;
        /// <summary>Asistencias que le dio su docente sin bitacora en el filtro (db/22).</summary>
        public int Dadas;
        /// <summary>Si entro a la lista despues del primer dia del filtro: ese dia (en las de antes cuenta con falta). Si no, null.</summary>
        public DateTime? EntroALaLista;

        public double? Porcentaje
        {
            get { return Veces == 0 ? (double?)null : 100.0 * Entro / Veces; }
        }

        /// <summary>Con N > 0 y por debajo del minimo (80 %). Con N = 0 nunca.</summary>
        public bool Bajo
        {
            get { return Veces > 0 && !ResumenAsistencia.TieneDerecho(100.0 * Entro / Veces); }
        }

        public int Faltas
        {
            get { return Veces - Entro; }
        }

        /// <summary>"Con derecho"/"Sin derecho" con el ciclo entero; "Si"/"No" con otro periodo o un laboratorio; "-" con N = 0.</summary>
        public string TextoDerecho(bool cicloEntero)
        {
            if (Veces == 0)
            {
                return EstadisticasDatos.SinDato;
            }

            return cicloEntero ? (Bajo ? "Sin derecho" : "Con derecho") : (Bajo ? "No" : "Sí");
        }

        /// <summary>Con 4 clases o menos, la ayuda de la celda del 80 %, como el tablero de Mis grupos; si no, null.</summary>
        public string NotaPocasClases
        {
            get
            {
                if (Veces == 0 || Veces > 4)
                {
                    return null;
                }

                return Veces == 1
                    ? "Con 1 clase, quien faltó lleva 0 %."
                    : "Con " + Veces + " clases, una falta ya deja " + ResumenAsistencia.PorcentajeMostrado(100.0 * (Veces - 1) / Veces) + " %.";
            }
        }

        /// <summary>"Entro a la lista el dd/MM: en las clases de antes cuenta como falta.", o null.</summary>
        public string AyudaDeEntrada
        {
            get
            {
                return EntroALaLista.HasValue
                    ? "Entró a la lista el " + EntroALaLista.Value.ToString("dd/MM", CultureInfo.InvariantCulture) + ": en las clases de antes cuenta como falta."
                    : null;
            }
        }
    }

    /// <summary>Una fila de la pestana Estudiantes (sec. 3.2).</summary>
    internal sealed class FilaEstudiante
    {
        public int Id;
        public string Matricula = "";
        public string Nombre = "";
        public bool Activo = true;
        /// <summary>El grupo guardado en su cuenta (E7), o "".</summary>
        public string Grupo = "";
        /// <summary>Clases suyas con N > 0 en el filtro.</summary>
        public int Clases;
        public int Entro;
        public int Veces;
        /// <summary>Clases suyas bajo el 80 % en el filtro.</summary>
        public int ClasesBajo;
        /// <summary>Todas sus bitacoras del periodo y los filtros (tambien uso libre y fuera del horario).</summary>
        public int Bitacoras;
        public int Fallas;
        public DateTime? UltimaBitacora;
        public int Retiradas;
        public int Dadas;
        /// <summary>Sus bitacoras del periodo que no cuentan en ningun dia de ninguna lista (uso libre, fuera de sus clases).</summary>
        public int FueraDeSusClases;
        /// <summary>Sus clases con N > 0, por materia.</summary>
        public List<ParDeAsistencia> SusClases = new List<ParDeAsistencia>();

        /// <summary>Solo tiene bitacoras fuera de sus clases: Entro "-", al final.</summary>
        public bool SoloFuera
        {
            get { return Veces == 0; }
        }

        public double? Porcentaje
        {
            get { return Veces == 0 ? (double?)null : 100.0 * Entro / Veces; }
        }

        public int Faltas
        {
            get { return Veces - Entro; }
        }

        /// <summary>Debajo de "Sus clases", solo lo que aplica.</summary>
        public List<string> Notas
        {
            get
            {
                List<string> notas = new List<string>();
                if (Retiradas > 0)
                {
                    notas.Add("Le quitaron la asistencia " + EstadisticasDatos.Veces(Retiradas) + ": registró sin estar en el salón.");
                }

                if (Dadas > 0)
                {
                    notas.Add("Su docente le dio la asistencia " + EstadisticasDatos.Veces(Dadas) + " sin bitácora.");
                }

                if (FueraDeSusClases > 0)
                {
                    notas.Add(FueraDeSusClases == 1
                        ? "1 bitácora que no cuenta en ninguna de sus clases (uso libre o sesiones fuera de las fechas de sus clases)."
                        : EstadisticasDatos.Miles(FueraDeSusClases) + " bitácoras que no cuentan en ninguna de sus clases (uso libre o sesiones fuera de las fechas de sus clases).");
                }

                return notas;
            }
        }
    }

    /// <summary>Una fila de la pestana Clases (sec. 3.3).</summary>
    internal sealed class FilaClase
    {
        public int ClaseId;
        public int DocenteId;
        public int GrupoId;
        public string Materia = "";
        public string Grupo = "";
        public string Docente = "";
        /// <summary>Los laboratorios de sus franjas y extras: "Laboratorio de Computo 1 y MULTIDICIPLINARIO".</summary>
        public string Laboratorios = "";
        /// <summary>
        /// Los mismos, cada uno aparte y por nombre (para juntar los de varias clases sin repetir: el PDF
        /// de la ficha del docente). Interno: no va en los volcados ni en los CSV, que siguen igual.
        /// </summary>
        internal List<string> NombresDeLaboratorios = new List<string>();
        /// <summary>Clases con bitacoras X de N (C2 de la clase), sin bitacoras, con asistencia dada y canceladas.</summary>
        public int ConBitacoras;
        public int QueContaban;
        public int SinBitacoras;
        public int ConDadas;
        public int Canceladas;
        /// <summary>El tamano de su lista.</summary>
        public int Estudiantes;
        public int Entro;
        public int Veces;
        public int Bajo;
        public int Fallas;
        /// <summary>Dias que cuentan en el filtro, sin el de hoy si sigue en curso.</summary>
        public int DiasQueCuentan;
        /// <summary>Toda la lista, por apellido, como la hoja.</summary>
        public List<ParDeAsistencia> Lista = new List<ParDeAsistencia>();

        /// <summary>Asistencia del grupo en el filtro, o null con N = 0.</summary>
        public double? Asistencia
        {
            get { return Veces == 0 ? (double?)null : 100.0 * Entro / Veces; }
        }

        /// <summary>Clases que se dieron X de QueContaban: con bitacoras o con asistencias que dio su docente (ver CifrasEstadisticas.ClasesQueSeDieron).</summary>
        public int SeDieron
        {
            get { return ConBitacoras + ConDadas; }
        }

        /// <summary>Ni bitacoras ni asistencias dadas (en rojo). Es SinBitacoras.</summary>
        public int SinRegistro
        {
            get { return SinBitacoras; }
        }

        /// <summary>SeDieron de QueContaban, de 0 a 1 (para la barra), o null sin clases que contaban.</summary>
        public double? FraccionSeDieron
        {
            get { return QueContaban == 0 ? (double?)null : (double)SeDieron / QueContaban; }
        }

        /// <summary>Las dos N en 0: va al final.</summary>
        public bool SinNada
        {
            get { return Veces == 0 && QueContaban == 0; }
        }

        /// <summary>"Lista de 24 estudiantes; 9 dias de clase en el periodo (por dia, como en Mis grupos)."</summary>
        public string LineaDeLaLista
        {
            get
            {
                return "Lista de " + EstadisticasDatos.Contar(Estudiantes, "estudiante", "estudiantes") + "; "
                    + EstadisticasDatos.Contar(DiasQueCuentan, "día", "días") + " de clase en el periodo (por día, como en Mis grupos).";
            }
        }
    }

    /// <summary>Una fila de la pestana Grupos (sec. 3.7): el resumen por grupo de RF141.</summary>
    internal sealed class FilaGrupo
    {
        public int GrupoId;
        public string Grupo = "";
        public int Clases;
        /// <summary>Las materias de sus clases, para la ayuda de la celda.</summary>
        public string Materias = "";
        /// <summary>Estudiantes distintos con N > 0 en alguna de sus clases.</summary>
        public int Estudiantes;
        public int Entro;
        public int Veces;
        /// <summary>Estudiantes distintos con alguna clase del grupo bajo el 80 %.</summary>
        public int Bajo;
        public int ConBitacoras;
        public int QueContaban;
        public int SinBitacoras;
        public int ConDadas;

        public double? Asistencia
        {
            get { return Veces == 0 ? (double?)null : 100.0 * Entro / Veces; }
        }

        public bool SinNada
        {
            get { return Veces == 0 && QueContaban == 0; }
        }
    }

    /// <summary>
    /// Una fila de la pestana Docentes (sec. 3.4, con la decision del usuario del
    /// 2026-10-22): SOLO la asistencia del docente a sus clases, sin datos de
    /// estudiantes (la asistencia de los grupos va en Clases y Grupos). Columnas:
    /// Docente, Materias, Se dieron (SeDieron de QueContaban), Sin registro,
    /// Canceladas, Asistencias que dio (db/22) y Sus reportes (db/15).
    /// </summary>
    internal sealed class FilaDocente
    {
        /// <summary>usuarios.idusuarios (para su cuadrito de color, ColoresProfesor).</summary>
        public int DocenteId;
        public string Docente = "";
        /// <summary>Sus clases (materia y grupo) del periodo en el filtro.</summary>
        public int Materias;
        /// <summary>"Redes (5A), Bases de datos (5B)", para la ayuda de la celda.</summary>
        public string NombresDeMaterias = "";
        /// <summary>Como Faltas: con bitacoras, que contaban (las que ya pasaron), sin registro (rojas) y en ambar (sin bitacoras, con asistencias dadas).</summary>
        public int ConBitacoras;
        public int QueContaban;
        public int SinBitacoras;
        public int ConDadas;
        public int Canceladas;
        /// <summary>Sus filas de reportes_profesor (solo con db/15).</summary>
        public int SusReportes;
        /// <summary>Asistencias que dio, activas, en el periodo (solo con db/22).</summary>
        public int AsistenciasQueDio;
        /// <summary>
        /// El detalle al elegirlo: cada clase-dia del calendario de sus clases en el
        /// periodo y los filtros (un bloque, como en Horarios, pestana Faltas), de la
        /// mas reciente a la mas vieja. Sus SeDio, SinRegistro y Cancelada suman las
        /// columnas de la fila.
        /// </summary>
        public List<ClaseDiaDocente> SusClasesDia = new List<ClaseDiaDocente>();

        /// <summary>
        /// Clases que se dieron X de QueContaban (cuantas veces asiste el docente, pedido
        /// del usuario): con bitacoras o con asistencias que dio (ver CifrasEstadisticas.ClasesQueSeDieron).
        /// </summary>
        public int SeDieron
        {
            get { return ConBitacoras + ConDadas; }
        }

        /// <summary>Ni bitacoras ni asistencias dadas (en rojo); el orden por omision de la pestana. Es SinBitacoras.</summary>
        public int SinRegistro
        {
            get { return SinBitacoras; }
        }

        /// <summary>SeDieron de QueContaban, de 0 a 1 (para la barra), o null sin clases que contaban.</summary>
        public double? FraccionSeDieron
        {
            get { return QueContaban == 0 ? (double?)null : (double)SeDieron / QueContaban; }
        }

        /// <summary>Ninguna clase que contara en el periodo: va al final, con "-".</summary>
        public bool SinNada
        {
            get { return QueContaban == 0; }
        }
    }

    /// <summary>
    /// Una clase-dia del calendario de un docente (el detalle de Docentes): un bloque
    /// del horario o una sesion extra de un dia, con su estado como en Horarios,
    /// pestana Faltas (LeerCalendario).
    /// </summary>
    internal sealed class ClaseDiaDocente
    {
        public int ClaseId;
        public DateTime Fecha;
        /// <summary>"Lunes 05/10/2026".</summary>
        public string FechaTexto = "";
        public string Materia = "";
        public string Grupo = "";
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public bool EsExtra;
        /// <summary>"08:00-10:00" o "08:00-10:00 (sesion extra)".</summary>
        public string Horario = "";
        public int LaboratorioId;
        public string Laboratorio = "";
        public EstadoEnCalendario Estado;
        /// <summary>
        /// "Se dio?": "Si (16 bitacoras)", "Si (2 asistencias dadas, sin bitacoras)",
        /// "Sin registro", "Cancelada" (": motivo" si lo hay), "Aun no pasa" o "No cuenta".
        /// </summary>
        public string SeDioTexto = "";
        /// <summary>Por que (sobre todo en "No cuenta" y "Sin registro"), para la ayuda de la celda; o null.</summary>
        public string Ayuda;
        /// <summary>Su sesion (codigos_accesos), o null si nadie la abrio: con ella la ventana abre la bitacora de la clase.</summary>
        public int? SesionId;
        /// <summary>La sesion donde estan sus bitacoras: la suya o, si sus estudiantes registraron en otra (llegaron antes a la clase anterior, o antes de un recorte), esa. Null sin bitacoras.</summary>
        public int? SesionDeSusBitacoras;
        /// <summary>Estudiantes que registraron (en su sesion o, si no, en la otra) y asistencias que dio su docente (db/22).</summary>
        public int Bitacoras;
        public int AsistenciasDadas;
        /// <summary>
        /// La clase-dia del calendario de la que sale (Faltas): el motivo de la cancelacion
        /// (Calendario.Motivo) y la sesion que se abre (PestanaCalendario.SesionQueAbrir). La
        /// usa la ficha del docente; interna, para que no cuente como dato de la fila.
        /// </summary>
        internal ClaseDelCalendario Calendario;

        /// <summary>Se dio: con bitacoras o, sin ellas, con asistencias que dio su docente (ambar en Faltas).</summary>
        public bool SeDio
        {
            get { return Estado == EstadoEnCalendario.ConBitacoras || Estado == EstadoEnCalendario.SinBitacorasConAsistencia; }
        }

        /// <summary>Ya paso, contaba y no tiene ni bitacoras ni asistencias dadas (rojo en Faltas).</summary>
        public bool SinRegistro
        {
            get { return Estado == EstadoEnCalendario.SinSesion || Estado == EstadoEnCalendario.SesionVacia; }
        }

        public bool Cancelada
        {
            get { return Estado == EstadoEnCalendario.Cancelada; }
        }
    }

    /// <summary>
    /// Un reporte de falla de un docente desde su panel (db/15) en una sesion del filtro, con
    /// su clase: la ficha del docente ("Lo que reporto desde su panel"). Se llena donde se
    /// cuenta SusReportes (Calculo.ContarBitacorasYReportes): la lista y su cuenta cuadran.
    /// </summary>
    internal sealed class FilaReporteDocente
    {
        public int Id;
        public int SesionId;
        /// <summary>Quien lo reporto (reportes_profesor.fk_usuario).</summary>
        public int DocenteId;
        /// <summary>El dia de su sesion.</summary>
        public DateTime Dia;
        public DateTime? RegistradoEn;
        public int LaboratorioId;
        public string Laboratorio = "";
        /// <summary>La clase de su sesion (misma terna y vigencia), o null fuera del horario.</summary>
        public int? ClaseId;
        /// <summary>"Redes (5A)" o "Fuera del horario".</summary>
        public string Clase = "";
        /// <summary>El equipo que nombra, o null: todo el laboratorio.</summary>
        public string Equipo;
        /// <summary>red, hardware, software u otro.</summary>
        public string Area = "";
        public string Falla = "";
    }

    /// <summary>
    /// Una asistencia que dio un docente sin bitacora (db/22), activa, en sus clases del
    /// filtro y en sus dias: la ficha del docente ("Asistencias que dio"). Se llena donde se
    /// cuenta AsistenciasQueDio (Calculo.ArmarDocentes). Las anuladas no estan.
    /// </summary>
    internal sealed class FilaAsistenciaDada
    {
        public int Id;
        /// <summary>Quien la dio (asistencias_dadas.fk_profesor).</summary>
        public int DocenteId;
        public int ClaseId;
        /// <summary>"Redes (5A)".</summary>
        public string Clase = "";
        /// <summary>El dia en que cuenta.</summary>
        public DateTime Fecha;
        /// <summary>Cuando la dio (reloj del servidor).</summary>
        public DateTime DadaEn;
        public int AlumnoId;
        public string Matricula = "";
        /// <summary>"Apellidos Nombre" (E7).</summary>
        public string Nombre = "";
        public string Motivo = "";
        public string Equipo;
        public string Comentario;
        public int SesionId;
        public int LaboratorioId;
    }

    /// <summary>Una fila de la pestana Laboratorios (sec. 3.5). La primera es "Todos los laboratorios".</summary>
    internal sealed class FilaLaboratorio
    {
        /// <summary>0 en "Todos los laboratorios" (y en "Sin laboratorio": sesiones sin laboratorio, con EsTodos en false).</summary>
        public int LaboratorioId;
        public bool EsTodos;
        public string Laboratorio = "";
        public bool Activo = true;
        public DateTime? RegistraDesde;
        /// <summary>"05/08/2026", "Solo uso libre" o "Aun no".</summary>
        public string RegistraDesdeTexto = "";
        /// <summary>Sin EmpezoEl ni bitacoras: la fila va en gris.</summary>
        public bool AunNo;
        public int ConBitacoras;
        public int QueContaban;
        public int SinBitacoras;
        public int ConDadas;
        public int Bitacoras;
        /// <summary>En "Todos", su propia cuenta (no la suma).</summary>
        public int Estudiantes;
        public int Fallas;
        public int Hardware;
        public int Software;
        public int Red;
        public int DeDocentes;
        /// <summary>Null sin db/10.</summary>
        public int? EquiposSinInternet;
        public int AvisosSinInternet;
    }

    internal enum TipoFilaEquipo
    {
        Equipo,
        /// <summary>Reportes de docentes sin equipo (numero_computadora NULL).</summary>
        TodoElLaboratorio,
        /// <summary>Bitacoras con el nombre del equipo vacio.</summary>
        SinEquipo
    }

    /// <summary>Una fila de la pestana Equipos (sec. 3.6): un equipo de un laboratorio con fallas o avisos.</summary>
    internal sealed class FilaEquipo
    {
        public int LaboratorioId;
        public string Laboratorio = "";
        public TipoFilaEquipo Tipo;
        /// <summary>La escritura mas usada en sus bitacoras (o la del aviso o reporte mas reciente), "Todo el laboratorio" o "Sin equipo".</summary>
        public string Equipo = "";
        /// <summary>DESKTOP-..., LAPTOP-..., WIN-... (Consultas.EsNombreDeWindows).</summary>
        public bool EsNombreDeWindows;
        /// <summary>Bitacoras con falla registradas desde el + reportes de docentes que lo nombran.</summary>
        public int Fallas;
        /// <summary>Fallo en X de N sesiones (X dentro de N).</summary>
        public int SesionesConFalla;
        public int SesionesUsado;
        /// <summary>Alguien registro desde el (si no, "No registro").</summary>
        public bool Registro;
        public int Hardware;
        public int Software;
        public int Red;
        public int DeDocentes;
        public int Avisos;
        public int DiasConReporte;
        /// <summary>El dia (de la sesion) y la hora (del registro) del ultimo reporte; sin hora, null.</summary>
        public DateTime? UltimoDia;
        public TimeSpan? UltimaHora;
        /// <summary>"lun 05/10 10:12" o "dom 20/09".</summary>
        public string UltimoReporte = "";
        /// <summary>"Hardware: No sirve el mouse", "Docente, red: ..." o "Sin internet (lo avisaron 2 estudiantes)".</summary>
        public string LoUltimo = "";

        /// <summary>Con reportes en 2 dias o mas: la fila va en (255,240,214).</summary>
        public bool Repetido
        {
            get { return DiasConReporte >= 2; }
        }

        /// <summary>"6 de 9 sesiones", "No registro" o "-" (todo el laboratorio).</summary>
        public string FalloEnTexto
        {
            get
            {
                if (Tipo == TipoFilaEquipo.TodoElLaboratorio)
                {
                    return EstadisticasDatos.SinDato;
                }

                return Registro && SesionesUsado > 0
                    ? EstadisticasDatos.Miles(SesionesConFalla) + " de " + EstadisticasDatos.Contar(SesionesUsado, "sesión", "sesiones")
                    : "No registró";
            }
        }
    }

    /// <summary>Una nota de la linea naranja (NotaFija, sec. 6.5): la ventana ensena la primera que aplica.</summary>
    internal sealed class NotaEstadisticas
    {
        /// <summary>1 = la mas importante.</summary>
        public int Prioridad;
        /// <summary>La que cabe siempre (unos 820 px a 9.5 pt a 1366).</summary>
        public string Texto = "";
        /// <summary>Con mas detalle, si cabe (medirla con TextRenderer); null si no hay.</summary>
        public string TextoLargo;
        /// <summary>Solo mientras se ve la pestana Equipos.</summary>
        public bool SoloEnEquipos;
    }

    /// <summary>Lo que ensena la ventana con un periodo y unos filtros (EstadisticasDatos.Calcular).</summary>
    internal sealed class ResultadoEstadisticas
    {
        public FiltroEstadisticas Filtro;
        /// <summary>Los dias que de verdad cuentan (los del Calculo: hasta hoy y con 400 como mucho).</summary>
        public DateTime Desde;
        public DateTime Hasta;
        /// <summary>El ciclo: el pasado con CicloPasado, "Este ciclo" (LecturaEstadisticas.CicloEnCurso) con Ciclo; null en lo demas.</summary>
        public CicloEscolar Ciclo;
        /// <summary>Un ciclo que ya termino (CicloPasado): se calcula con los datos de hoy, no es un cierre congelado.</summary>
        public bool CicloCerrado;
        /// <summary>Con Plantilla: la plantilla aplicada (su nombre en el combo, el PDF, el CSV y Graficas); null en lo demas.</summary>
        public PlantillaAplicada Plantilla;
        /// <summary>Las clases del periodo con todos los docentes y grupos (R2; con un ciclo pasado, las suyas que tocan sus dias).</summary>
        public int ClasesDelPeriodo;
        /// <summary>
        /// El periodo pasa de 400 dias: los dias que cuentan empiezan en Desde (su ultimo dia
        /// - 399), despues de su primer dia (Filtro.Desde). Lo dicen lblResumen, la ayuda del
        /// combo, el PDF y el CSV, que entonces no dicen "completo".
        /// </summary>
        public bool Recortado;
        public CifrasEstadisticas Cifras = new CifrasEstadisticas();
        public List<FilaEstudiante> Estudiantes = new List<FilaEstudiante>();
        public List<FilaClase> Clases = new List<FilaClase>();
        public List<FilaGrupo> Grupos = new List<FilaGrupo>();
        public List<FilaDocente> Docentes = new List<FilaDocente>();
        public List<FilaLaboratorio> Laboratorios = new List<FilaLaboratorio>();
        public List<FilaEquipo> Equipos = new List<FilaEquipo>();
        /// <summary>Los reportes de docentes de las sesiones del filtro (db/15; vacia sin ella), con su clase: los que cuentan en SusReportes.</summary>
        public List<FilaReporteDocente> ReportesDocentes = new List<FilaReporteDocente>();
        /// <summary>Las asistencias que dieron los docentes en las clases del filtro (db/22; vacia sin ella): las que cuentan en AsistenciasQueDio.</summary>
        public List<FilaAsistenciaDada> AsistenciasDadas = new List<FilaAsistenciaDada>();
        /// <summary>Semanas, meses y horas de cada laboratorio (pestana Graficas).</summary>
        public GraficasEstadisticas Graficas = new GraficasEstadisticas();
        /// <summary>Por prioridad (sec. 6.5).</summary>
        public List<NotaEstadisticas> Notas = new List<NotaEstadisticas>();

        /// <summary>lblResumen: "Del 30/09/2026 al 21/10/2026. Leido a las 10:42 (hora del servidor)." (o el aviso del filtro).</summary>
        public string LineaResumen = "";
        /// <summary>Las lineas de arriba de cada pestana (sec. 6.5).</summary>
        public string LineaEstudiantes = "";
        public string LineaClases = "";
        public string LineaGrupos = "";
        public string LineaDocentes = "";
        public string LineaLaboratorios = "";
        public string LineaEquipos = "";
        /// <summary>"Aun no hay bitacoras en este periodo." o "Este laboratorio aun no registra con DigBit."; null si hay algo.</summary>
        public string TextoVacio;

        /// <summary>Los combos: el primero de cada uno es "Todos" (Id 0).</summary>
        public List<OpcionCombo> OpcionesLaboratorio = new List<OpcionCombo>();
        public List<OpcionCombo> OpcionesDocente = new List<OpcionCombo>();
        public List<OpcionCombo> OpcionesGrupo = new List<OpcionCombo>();

        /// <summary>"Este ciclo" o un ciclo pasado, con todos los laboratorios: la columna del 80 % es el derecho a evaluacion.</summary>
        public bool CicloEntero;
        /// <summary>"Derecho a evaluacion (80 %)" o "80 % o mas en el periodo", y sus cortos ("Derecho (80 %)" o "80 % o mas").</summary>
        public string TituloDerecho = "";
        public string TituloDerechoCorto = "";
        /// <summary>Hasta antes de hoy: quien entro a la lista despues cuenta con falta (lo dicen la ayuda de C1 y la linea de Estudiantes).</summary>
        public bool PeriodoPasado;
        /// <summary>Con db/15, db/10 y db/22 (columnas que solo se crean con ellas).</summary>
        public bool ConReportesDocentes;
        public bool ConAvisos;
        public bool ConDadas;
        /// <summary>Lo que tardo Calcular (si pasa de 300 ms, la ventana dice "Calculando...").</summary>
        public TimeSpan Duracion;
    }

    /// <summary>Lectura, cache y calculo de Estadisticas (sec. 4 y sec. 5.2).</summary>
    internal static class EstadisticasDatos
    {
        public const string SinDato = "—";

        /// <summary>En solo consulta, por que no se guarda en CSV esta pestana (sec. 7).</summary>
        public const string SinCsvParaSoloConsulta = "Solo la administración de DigBit guarda en un archivo los datos de estudiantes y docentes. Si los necesitas, pídeselos.";

        /// <summary>La ayuda de "Clases que se dieron" y "Sin registro" (Docentes, Clases y C2), con el texto del usuario.</summary>
        public const string AyudaClasesQueSeDieron = "Una clase se dio si alguien registró su bitácora o su docente dio la asistencia a quien no pudo registrar. "
            + "Sin registro no siempre es falta del docente: puede que nadie registrara. Las canceladas no cuentan.";

        private const int DiasLeidos = HorariosDatos.DiasMaximosCalendario;
        private static readonly TimeSpan VidaDeLaLectura = TimeSpan.FromMinutes(10);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly StringComparer PorNombre = StringComparer.Create(CultureInfo.GetCultureInfo("es-MX"), true);
        private static readonly string[] DiasCortos = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };

        // Las cuentas de los ciclos (ArmarCiclos, sec. 2 de la especificacion de periodos):
        // un dia a menos de 14 del inicio del ciclo no abre otro; un bloque nuevo que termina
        // 28 dias o mas despues del fin abre otro si es grande (3 clases o mas y al menos la
        // mitad de las que seguian) o si empieza en las ultimas 3 semanas del ciclo.
        private const int VentanaDeInicio = 14;
        private const int TerminaDespues = 28;
        private const int UltimasSemanas = 21;
        private const int MinimoDeUnBloque = 3;
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

        private static readonly object candado = new object();
        // La ultima lectura que llega a hoy y la ultima de un periodo que termino antes (un
        // ciclo pasado o unas fechas viejas): se va y se vuelve entre las dos sin leer otra vez.
        private static LecturaEstadisticas guardada;
        private static LecturaEstadisticas guardadaPasada;
        // Sin permiso sobre ciclos_preliminares (o sin db/16 y db/23), el aviso del log, una vez.
        private static bool avisadoSinNombres;

        // Sube con cada Olvidar. Una lectura que empezo antes (y acaba despues de
        // cerrar el panel o de "Actualizar") no se guarda: si no, volvia a dejar en
        // memoria los datos que Olvidar acababa de soltar.
        private static int vecesOlvidada;

        // =====================================================================
        // Lectura y cache
        // =====================================================================

        /// <summary>
        /// Lo que hace falta para el periodo, en una conexion: E0; LeerClasesEnLote
        /// (E1 y E3); los ciclos (ArmarCiclos, con los nombres de los aplicados);
        /// LeerElRestoEnLote (E2, E4, modos, E6, E7 y E11 de [DesdeLectura,
        /// HastaLectura]); LeerCalendario; CalcularTodas con los tramos de hoy; E5, E8 y
        /// E10. Reutiliza una de las dos ultimas lecturas (la que llega a hoy y la de un
        /// periodo que termino antes) si es de la misma cuenta, del mismo dia del
        /// servidor, de hace menos de 10 minutos y cubre el periodo (Cubre); entonces ni
        /// abre la conexion. Va en segundo plano (LeerEnSegundoPlano): no toca la
        /// pantalla. Lanza si tiene que ir a la base y no hay conexion: quien llama lo
        /// ensena y sigue con la lectura de antes (y con su periodo: Calcular no acepta
        /// un periodo que esa lectura no cubre).
        /// </summary>
        /// <param name="forzar">"Actualizar" (F5): olvida las lecturas guardadas.</param>
        public static LecturaEstadisticas Leer(PeriodoElegido periodo, bool forzar)
        {
            if (periodo == null)
            {
                throw new ArgumentNullException("periodo");
            }

            if (forzar)
            {
                Olvidar();
            }

            // Si una guardada sirve, sin abrir la conexion: con la base remota son 3
            // viajes menos por cada cambio de periodo, y sin red se sigue pudiendo
            // cambiar a un periodo que ya esta leido. La cuenta se sabe sin abrirla.
            string sinAbrir = CuentaSinConexion();
            LecturaEstadisticas[] previas;
            int olvidadaAlEmpezar;
            lock (candado)
            {
                previas = new[] { guardada, guardadaPasada };
                olvidadaAlEmpezar = vecesOlvidada;
            }

            foreach (LecturaEstadisticas previa in previas)
            {
                if (previa != null && sinAbrir != null && previa.CuentaSinConexion == sinAbrir && Vigente(previa) && Cubre(previa, periodo))
                {
                    return previa;
                }
            }

            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                string cuenta = Esquema.CuentaDe(conexion);
                foreach (LecturaEstadisticas previa in previas)
                {
                    if (previa != null && previa.Cuenta == cuenta && Vigente(previa) && Cubre(previa, periodo))
                    {
                        return previa;
                    }
                }

                LecturaEstadisticas leida = LeerDeLaBase(conexion, cuenta, periodo);
                leida.CuentaSinConexion = sinAbrir;
                lock (candado)
                {
                    // Si mientras se leia alguien llamo a Olvidar, se devuelve a quien
                    // la pidio pero no se guarda.
                    if (vecesOlvidada == olvidadaAlEmpezar)
                    {
                        if (leida.HastaLectura == leida.Hoy && leida.Calendario.Hasta == leida.Hoy)
                        {
                            guardada = leida;
                        }
                        else
                        {
                            guardadaPasada = leida;
                        }
                    }
                }

                return leida;
            }
        }

        /// <summary>
        /// La cuenta con que se leeria ahora, sin abrir la conexion: la del equipo o,
        /// mientras dura la sesion de una persona responsable del laboratorio, la de
        /// solo consulta (Conexion.ConCuentaDeSesion, mismo servidor y misma base).
        /// Null si la cadena no se entiende (entonces se mira con la conexion abierta).
        /// </summary>
        private static string CuentaSinConexion()
        {
            try
            {
                using (MySqlConnection sinAbrir = new MySqlConnection(Conexion.CadenaDelEquipo()))
                {
                    return (Conexion.ConCuentaDeSesion ? "solo consulta|" : "equipo|") + Esquema.CuentaDe(sinAbrir);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Leer en Task.Run (la ventana nunca lee en el hilo de la pantalla).</summary>
        public static Task<LecturaEstadisticas> LeerEnSegundoPlano(PeriodoElegido periodo, bool forzar)
        {
            return Task.Run(() => Leer(periodo, forzar));
        }

        /// <summary>
        /// Olvida las lecturas guardadas: "Actualizar" y al cerrar el panel de la
        /// administracion (otra cuenta no las reutiliza y no quedan datos de
        /// estudiantes en memoria). Las lecturas que ya estaban en camino tampoco
        /// se guardan al terminar.
        /// </summary>
        public static void Olvidar()
        {
            lock (candado)
            {
                guardada = null;
                guardadaPasada = null;
                vecesOlvidada++;
            }
        }

        /// <summary>De hace menos de 10 minutos y, con el reloj del servidor estimado, del mismo dia.</summary>
        private static bool Vigente(LecturaEstadisticas lectura)
        {
            TimeSpan transcurrido = DateTime.UtcNow - lectura.LeidaUtc;
            // AhoraServidor es de cuando empezo a leer y LeidaUtc de cuando acabo:
            // con lo que tardo, la medianoche no se nota unos segundos tarde.
            return transcurrido >= TimeSpan.Zero && transcurrido < VidaDeLaLectura
                && (lectura.AhoraServidor + transcurrido + lectura.Duracion).Date == lectura.Hoy;
        }

        /// <summary>
        /// La lectura sirve para ese periodo (resuelto con ella: ResolverPeriodo): la lista
        /// de cada clase del periodo sale entera de lo leido y el calendario tiene sus dias
        /// (CubreElPeriodo). Las clases del periodo se eligen con el del periodo nuevo.
        /// </summary>
        public static bool Cubre(LecturaEstadisticas lectura, PeriodoElegido periodo)
        {
            PeriodoResuelto p = ResolverPeriodo(periodo, lectura);
            return CubreElPeriodo(lectura, p.Desde, p.Hasta, p.EsCiclo, p.SoloEstas);
        }

        /// <summary>
        /// La comprobacion de cobertura de la cache (Cubre) y de Calcular: lo leido empieza
        /// antes que la lista de cada clase del periodo (DesdeLecturaDe) y acaba despues
        /// (HastaLecturaDe), y el calendario va del primer dia que cuenta (400 como mucho,
        /// desde el ultimo) al ultimo que cuenta. Con los periodos que llegan a hoy da lo
        /// mismo que antes; una lectura de un periodo que termino antes nunca cubre "Este
        /// ciclo" ni otro que llegue a hoy.
        /// </summary>
        internal static bool CubreElPeriodo(LecturaEstadisticas l, DateTime desde, DateTime hasta, bool esCiclo, ISet<int> soloEstas)
        {
            DateTime hoy = l.Hoy;
            DateTime hastaVisto = hasta.Date < hoy ? hasta.Date : hoy;
            DateTime primero = PrimerDiaQueCuenta(desde, hasta, hoy);
            DateTime desdeClases = DesdeDeLasClases(desde, hasta, esCiclo, hoy);
            return AsistenciaDatos.DesdeLecturaDe(l.Asistencia.Clases, desdeClases, hasta, esCiclo, soloEstas) >= l.Asistencia.DesdeLectura
                && AsistenciaDatos.HastaLecturaDe(l.Asistencia.Clases, desdeClases, hasta, esCiclo, soloEstas, hoy) <= l.Asistencia.HastaLectura
                && l.Calendario.Desde <= primero
                && l.Calendario.Hasta >= hastaVisto;
        }

        /// <summary>El primer dia que cuenta: el del periodo o, si pasa de 400 dias, su ultimo dia (hoy como mucho) - 399 (Calculo).</summary>
        private static DateTime PrimerDiaQueCuenta(DateTime desde, DateTime hasta, DateTime hoy)
        {
            DateTime hastaVisto = hasta.Date < hoy.Date ? hasta.Date : hoy.Date;
            DateTime primero = hastaVisto.AddDays(-(DiasLeidos - 1));
            return desde.Date < primero ? primero : desde.Date;
        }

        /// <summary>
        /// El primer dia con que se eligen las clases del periodo (DelPeriodo, DesdeLecturaDe y
        /// HastaLecturaDe): con un ciclo, el suyo (sus clases son las que empezaron en el,
        /// aunque pase de 400 dias); con lo demas, el primero que cuenta (PrimerDiaQueCuenta),
        /// para que unas fechas de mas de 400 dias (un "Del ... a hoy" que crecio con DigBit
        /// abierto) no traigan clases ni docentes de dias que no cuentan.
        /// </summary>
        private static DateTime DesdeDeLasClases(DateTime desde, DateTime hasta, bool esCiclo, DateTime hoy)
        {
            return esCiclo ? desde.Date : PrimerDiaQueCuenta(desde, hasta, hoy);
        }

        private static LecturaEstadisticas LeerDeLaBase(MySqlConnection conexion, string cuenta, PeriodoElegido periodo)
        {
            Stopwatch reloj = Stopwatch.StartNew();
            // Dos vueltas como mucho: si entre E0 y el calendario cambia el dia del
            // servidor (medianoche), el periodo se calculo con el dia de ayer.
            for (int vuelta = 0; ; vuelta++)
            {
                LecturaEstadisticas lectura = new LecturaEstadisticas { Cuenta = cuenta };
                DateTime ahoraE0 = LeerReloj(conexion, lectura);
                lectura.Asistencia = AsistenciaDatos.LeerClasesEnLote(conexion);
                DateTime hoy = lectura.Hoy;

                // Los ciclos, de E1 y E3 (las clases con franjas) y del nombre de los aplicados; las
                // plantillas aplicadas que ya empezaron, de la misma consulta.
                HashSet<int> conHorario = new HashSet<int>(lectura.Asistencia.FranjasPorDocente.Values.SelectMany(f => f).Select(f => f.ClaseId));
                List<CicloPreliminar> aplicadas = LeerAplicadas(conexion);
                Dictionary<DateTime, string> nombres = NombresAplicados(aplicadas);
                lectura.CiclosPasados = ArmarCiclos(lectura.Asistencia.Clases, conHorario, lectura.CicloDesde, hoy, nombres);
                lectura.CicloEnCurso = lectura.CicloDesde.HasValue
                    ? CicloEnCursoDe(lectura.Asistencia.Clases, conHorario, lectura.CicloDesde.Value, lectura.CicloHasta, hoy, nombres)
                    : null;
                lectura.Plantillas = ArmarPlantillas(aplicadas, hoy);

                // Lo demas, solo de los dias que hacen falta para ese periodo.
                PeriodoResuelto p = ResolverPeriodo(periodo, lectura);
                DateTime desdeClases = DesdeDeLasClases(p.Desde, p.Hasta, p.EsCiclo, hoy);
                DateTime desdeLectura = AsistenciaDatos.DesdeLecturaDe(lectura.Asistencia.Clases, desdeClases, p.Hasta, p.EsCiclo, p.SoloEstas);
                DateTime hastaLectura = AsistenciaDatos.HastaLecturaDe(lectura.Asistencia.Clases, desdeClases, p.Hasta, p.EsCiclo, p.SoloEstas, hoy);
                AsistenciaDatos.LeerElRestoEnLote(conexion, lectura.Asistencia, desdeLectura, hastaLectura);

                // El calendario: los dias que cuentan (400 como mucho, desde el ultimo) y, si
                // cabe, hasta HastaLectura (con los periodos de hoy, de max(DesdeLectura, hoy - 399) a hoy).
                DateTime hastaVisto = p.Hasta < hoy ? p.Hasta : hoy;
                DateTime primero = PrimerDiaQueCuenta(p.Desde, p.Hasta, hoy);
                DateTime calHasta = (hastaLectura - primero).TotalDays < DiasLeidos ? hastaLectura : hastaVisto;
                DateTime calDesde = desdeLectura > calHasta.AddDays(-(DiasLeidos - 1)) ? desdeLectura : calHasta.AddDays(-(DiasLeidos - 1));
                lectura.Calendario = HorariosDatos.LeerCalendario(conexion, calDesde, calHasta, false);
                if (vuelta == 0 && (ahoraE0.Date != hoy || lectura.Calendario.Ahora.Date != hoy))
                {
                    continue;
                }

                // Si el calendario no llega a hoy, los tramos de hoy salen vacios (ninguna clase del periodo es de hoy).
                lectura.Listas = AsistenciaDatos.CalcularTodas(lectura.Asistencia, TramosDeHoy(lectura.Calendario, hoy));

                // Cada Esquema.* una vez por lectura (lo que falta se vuelve a preguntar cada vez).
                lectura.ConAvisos = Esquema.EquiposSinInternet(conexion);
                bool conLlegada = lectura.ConAvisos && Esquema.HoraDeLlegada(conexion);
                lectura.ConReportesDocentes = Esquema.ReportesDeProfesor(conexion);
                LeerLaboratorios(conexion, lectura, Esquema.LaboratoriosDesactivables(conexion));
                if (lectura.ConReportesDocentes)
                {
                    LeerReportesDocentes(conexion, lectura);
                }

                LeerDetalles(conexion, lectura, conLlegada);
                lectura.LeidaUtc = DateTime.UtcNow;
                lectura.Duracion = reloj.Elapsed;
                Log.Info("Estadisticas: leido de " + lectura.DesdeLectura.ToString("yyyy-MM-dd", Inv) + " a " + lectura.HastaLectura.ToString("yyyy-MM-dd", Inv)
                    + " (calendario de " + lectura.Calendario.Desde.ToString("yyyy-MM-dd", Inv) + " a " + lectura.Calendario.Hasta.ToString("yyyy-MM-dd", Inv) + ")"
                    + " en " + (int)reloj.ElapsedMilliseconds + " ms: " + lectura.Asistencia.Clases.Count + " clases, "
                    + lectura.Asistencia.SesionPorId.Count + " sesiones, " + lectura.Asistencia.BitacorasPorSesion.Values.Sum(l => l.Count) + " bitacoras, "
                    + lectura.CiclosPasados.Count + " ciclos pasados, " + lectura.Plantillas.Count + " plantillas aplicadas.");
                return lectura;
            }
        }

        /// <summary>E0: el reloj del servidor y "Este ciclo" (e0_reloj_ciclo_rev.sql). Devuelve NOW().</summary>
        private static DateTime LeerReloj(MySqlConnection conexion, LecturaEstadisticas lectura)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT NOW() AS ahora,
                       (SELECT MIN(c.vigente_desde) FROM clases c
                         WHERE c.activa = 1 AND c.vigente_desde <= CURDATE() AND c.vigente_hasta >= CURDATE()) AS ciclo_desde,
                       (SELECT MAX(c.vigente_hasta) FROM clases c
                         WHERE c.activa = 1 AND c.vigente_desde <= CURDATE() AND c.vigente_hasta >= CURDATE()) AS ciclo_hasta", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                reader.Read();
                lectura.CicloDesde = FechaONula(reader["ciclo_desde"]);
                lectura.CicloHasta = FechaONula(reader["ciclo_hasta"]);
                return Convert.ToDateTime(reader["ahora"], Inv);
            }
        }

        /// <summary>
        /// Las plantillas aplicadas en Horarios > Aplicacion (db/16 y db/23: ciclos_preliminares
        /// con estado 'publicado' y su primer dia), en el orden en que se aplicaron, con su
        /// nombre, su vigencia y el ultimo dia que dieron al horario de antes (fin_anterior). Una
        /// que se deshizo vuelve a 'borrador' y una guardada es 'guardado': no salen. Vacia sin
        /// esas migraciones o si la cuenta no puede leerlas (digbit_equipo nunca; digbit_consulta
        /// solo con db/23): entonces los ciclos se llaman por sus meses y Periodo no ensena
        /// plantillas, sin ningun error.
        /// </summary>
        private static List<CicloPreliminar> LeerAplicadas(MySqlConnection conexion)
        {
            List<CicloPreliminar> aplicadas = new List<CicloPreliminar>();
            if (!Esquema.CicloPreliminar(conexion) || !Esquema.HorariosGuardados(conexion))
            {
                return aplicadas;
            }

            try
            {
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT cp.idciclo, cp.nombre, DATE_FORMAT(cp.desde, '%Y-%m-%d') AS desde, DATE_FORMAT(cp.hasta, '%Y-%m-%d') AS hasta,
                           DATE_FORMAT(cp.fin_anterior, '%Y-%m-%d') AS fin_anterior
                    FROM ciclos_preliminares cp
                    WHERE cp.estado = 'publicado' AND cp.desde IS NOT NULL
                    ORDER BY cp.publicado_en, cp.idciclo", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime? desde = DiaONulo(reader["desde"]);
                        if (!desde.HasValue || reader.IsDBNull(1))
                        {
                            continue;
                        }

                        aplicadas.Add(new CicloPreliminar
                        {
                            Id = Convert.ToInt32(reader["idciclo"], Inv),
                            Nombre = Convert.ToString(reader["nombre"], Inv).Trim(),
                            Estado = EstadoConfiguracion.Aplicada,
                            Desde = desde,
                            Hasta = DiaONulo(reader["hasta"]),
                            FinAnterior = DiaONulo(reader["fin_anterior"])
                        });
                    }
                }
            }
            catch (MySqlException ex)
            {
                bool avisar;
                lock (candado)
                {
                    avisar = !avisadoSinNombres;
                    avisadoSinNombres = true;
                }

                if (avisar)
                {
                    Log.Aviso("Estadisticas: no se pudieron leer las plantillas aplicadas (" + ex.Number.ToString(Inv) + "): los ciclos se llaman por sus meses y Periodo no las ensena.");
                }

                aplicadas.Clear();
            }

            return aplicadas;
        }

        /// <summary>"2026-10-11" (DATE_FORMAT) como dia, o null.</summary>
        private static DateTime? DiaONulo(object valor)
        {
            DateTime dia;
            return valor != null && valor != DBNull.Value
                && DateTime.TryParseExact(Convert.ToString(valor, Inv), "yyyy-MM-dd", Inv, DateTimeStyles.None, out dia)
                ? dia.Date : (DateTime?)null;
        }

        /// <summary>
        /// Los nombres de las plantillas aplicadas por su primer dia (el vigente_desde de sus
        /// clases al aplicarlas), para los ciclos (ArmarCiclos y CicloEnCursoDe): si dos tienen el
        /// mismo, gana la ultima aplicada.
        /// </summary>
        private static Dictionary<DateTime, string> NombresAplicados(IEnumerable<CicloPreliminar> aplicadas)
        {
            Dictionary<DateTime, string> nombres = new Dictionary<DateTime, string>();
            foreach (CicloPreliminar aplicada in aplicadas)
            {
                if (aplicada.Desde.HasValue && aplicada.Nombre.Length > 0)
                {
                    nombres[aplicada.Desde.Value.Date] = aplicada.Nombre;
                }
            }

            return nombres;
        }

        private static DateTime? FechaONula(object valor)
        {
            return valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor, Inv).Date;
        }

        /// <summary>
        /// Los tramos de hoy de cada clase para CalcularTodas: de sus clases-dia de hoy
        /// en el calendario, no canceladas y utilizables (los mismos bloques que Mis
        /// grupos saca de la agenda; los restos de una recortada son extras), del
        /// primer inicio al ultimo fin.
        /// </summary>
        internal static Dictionary<int, TramoHorario> TramosDeHoy(CalendarioBitacoras calendario, DateTime hoy)
        {
            Dictionary<int, TramoHorario> tramos = new Dictionary<int, TramoHorario>();
            foreach (ClaseDelCalendario clase in calendario.Clases.Where(c => c.Fecha == hoy.Date && !c.Cancelada && c.Utilizable))
            {
                TramoHorario tramo;
                if (!tramos.TryGetValue(clase.ClaseId, out tramo))
                {
                    tramos[clase.ClaseId] = new TramoHorario(clase.Inicio, clase.Fin);
                    continue;
                }

                if (clase.Inicio < tramo.Inicio)
                {
                    tramo.Inicio = clase.Inicio;
                }

                if (clase.Fin > tramo.Fin)
                {
                    tramo.Fin = clase.Fin;
                }
            }

            return tramos;
        }

        /// <summary>E5, con EmpezoEl del calendario.</summary>
        private static void LeerLaboratorios(MySqlConnection conexion, LecturaEstadisticas lectura, bool desactivables)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idlaboratorios, nombre_laboratorio, " + (desactivables ? "IF(activo = 0, 0, 1)" : "1") + " AS activo FROM laboratorios", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    LaboratorioLeido laboratorio = new LaboratorioLeido
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Activo = Convert.ToInt32(reader[2]) == 1
                    };
                    DateTime empezo;
                    if (lectura.Calendario.EmpezoEl.TryGetValue(laboratorio.Id, out empezo))
                    {
                        laboratorio.EmpezoEl = empezo;
                    }

                    lectura.Laboratorios[laboratorio.Id] = laboratorio;
                }
            }
        }

        /// <summary>E8: los reportes de docentes de [DesdeLectura, HastaLectura] (db/15).</summary>
        private static void LeerReportesDocentes(MySqlConnection conexion, LecturaEstadisticas lectura)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT rp.idreportes_profesor, rp.fk_codigo_accesos, rp.fk_usuario,
                       NULLIF(TRIM(COALESCE(rp.numero_computadora, '')), '') AS equipo, rp.area, rp.falla,
                       DATE_FORMAT(rp.registrado_en, '%Y-%m-%d %H:%i:%s') AS registrado_en,
                       ca.laboratorios_idlaboratorios AS lab, " + VigenciaFranjas.DiaDeSesion("ca") + @" AS dia
                FROM reportes_profesor rp
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rp.fk_codigo_accesos
                WHERE " + VigenciaFranjas.DiaDeSesion("ca") + " BETWEEN @Desde AND @Hasta", conexion))
            {
                comando.CommandTimeout = 60;
                comando.Parameters.AddWithValue("@Desde", lectura.DesdeLectura.Date);
                comando.Parameters.AddWithValue("@Hasta", lectura.HastaLectura.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(8))
                        {
                            continue;
                        }

                        lectura.ReportesDocentes.Add(new ReporteDeDocente
                        {
                            Id = reader.GetInt32(0),
                            SesionId = reader.GetInt32(1),
                            DocenteId = reader.GetInt32(2),
                            Equipo = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Area = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Falla = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            RegistradoEn = FechaYHora(reader[6]),
                            LaboratorioId = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
                            Dia = Convert.ToDateTime(reader[8], Inv).Date
                        });
                    }
                }
            }
        }

        /// <summary>
        /// E10: solo las bitacoras con falla o con aviso de equipos sin internet, con
        /// los seis textos (para "Lo ultimo que reportaron"), la lista y cuando. Sin
        /// db/10, sin la lista ni el OR, y la hora es solo registrado_en.
        /// </summary>
        private static void LeerDetalles(MySqlConnection conexion, LecturaEstadisticas lectura, bool conLlegada)
        {
            string falla = "(" + Consultas.CondicionConFalla + ")";
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT rb.idregistros_bitacoras AS id, rb.fk_codigo_accesos AS sesion, TRIM(COALESCE(rb.numero_computadora, '')) AS equipo, "
                + "CASE WHEN " + falla + " THEN 1 ELSE 0 END AS con_falla, "
                + "rb.falla_red, rb.comentarios_red, rb.falla_hardware, rb.comentarios_hardware, rb.falla_software, rb.comentarios_software, "
                + (lectura.ConAvisos ? "NULLIF(TRIM(COALESCE(rb.equipos_sin_internet, '')), '')" : "NULL") + " AS sin_internet, "
                + "DATE_FORMAT(" + (conLlegada ? "COALESCE(rb.registrado_en, rb.recibido_en)" : "rb.registrado_en") + ", '%Y-%m-%d %H:%i:%s') AS cuando "
                + "FROM registros_bitacoras rb "
                + "INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos "
                + "WHERE " + VigenciaFranjas.DiaDeSesion("ca") + " BETWEEN @Desde AND @Hasta "
                + "AND (" + falla + (lectura.ConAvisos ? " OR TRIM(COALESCE(rb.equipos_sin_internet, '')) <> ''" : "") + ")", conexion))
            {
                comando.CommandTimeout = 60;
                comando.Parameters.AddWithValue("@Desde", lectura.DesdeLectura.Date);
                comando.Parameters.AddWithValue("@Hasta", lectura.HastaLectura.Date);
                DataTable tabla = new DataTable();
                using (MySqlDataAdapter adaptador = new MySqlDataAdapter(comando))
                {
                    adaptador.Fill(tabla);
                }

                foreach (DataRow fila in tabla.Rows)
                {
                    bool conFalla = Convert.ToInt32(fila["con_falla"], Inv) == 1;
                    string describe = conFalla ? Consultas.DescribirFallas(fila) : null;
                    DetalleDeReporte detalle = new DetalleDeReporte
                    {
                        Id = Convert.ToInt32(fila["id"], Inv),
                        SesionId = Convert.ToInt32(fila["sesion"], Inv),
                        Equipo = Convert.ToString(fila["equipo"], Inv) ?? "",
                        ConFalla = conFalla,
                        SinInternet = fila["sin_internet"] == DBNull.Value ? null : Convert.ToString(fila["sin_internet"], Inv),
                        Cuando = FechaYHora(fila["cuando"]),
                        QueReporto = describe == null || describe == "Sin novedad" ? null : describe
                    };
                    lectura.Detalles[detalle.Id] = detalle;
                }
            }
        }

        /// <summary>"yyyy-MM-dd HH:mm:ss" (DATE_FORMAT: una fecha en ceros no rompe la lectura), o null.</summary>
        private static DateTime? FechaYHora(object valor)
        {
            DateTime leida;
            return valor != null && valor != DBNull.Value
                && DateTime.TryParseExact(Convert.ToString(valor, Inv), "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out leida)
                ? leida : (DateTime?)null;
        }

        // =====================================================================
        // Periodo y filtros
        // =====================================================================

        /// <summary>
        /// El periodo resuelto con una lectura (ResolverPeriodo): el que quedo, los dias que
        /// pide (sin recortar a 400: eso es de Calculo) y, con un ciclo pasado, sus clases.
        /// </summary>
        private sealed class PeriodoResuelto
        {
            public TipoPeriodo Tipo;
            public DateTime Desde;
            public DateTime Hasta;
            public bool EsCiclo;
            /// <summary>Solo con CicloPasado; null en lo demas.</summary>
            public CicloEscolar Ciclo;
            /// <summary>Solo con Plantilla; null en lo demas.</summary>
            public PlantillaAplicada Plantilla;
            public bool HastaHoy;
            /// <summary>Que se ve otro periodo que el pedido (FiltroEstadisticas.AvisoDelPeriodo), o null.</summary>
            public string Aviso;

            /// <summary>Las clases del ciclo pasado, o null.</summary>
            public ISet<int> SoloEstas
            {
                get { return Ciclo == null ? null : Ciclo.Clases; }
            }
        }

        /// <summary>
        /// Los dias del periodo con el reloj del servidor y lo leido (R2). Un ciclo pasado
        /// se busca por su primer dia y, si cambiaron las fechas de sus clases, por el ciclo
        /// que contiene ese dia; si ya no esta, "Este ciclo". "Este ciclo" sin clases
        /// vigentes hoy pasa al ultimo ciclo que termino y, sin ninguno, a "Ultimos 30
        /// dias". Con Fechas, el ultimo dia llega a hoy como mucho (con HastaHoy, siempre
        /// hoy) y el primero no se recorta: los 400 dias se cuentan desde el ultimo (Calculo).
        /// Una plantilla aplicada se busca por su idciclo y da sus dias (los de la lectura), que
        /// se cuentan como unas fechas elegidas (EsCiclo en false); si ya no esta, "Este ciclo".
        /// </summary>
        private static PeriodoResuelto ResolverPeriodo(PeriodoElegido periodo, LecturaEstadisticas lectura)
        {
            DateTime hoy = lectura.Hoy;
            PeriodoResuelto p = new PeriodoResuelto { Tipo = periodo.Tipo, Hasta = hoy };
            CicloEscolar ciclo = null;
            PlantillaAplicada plantilla = null;
            // Lo pedido ya no esta en la lista: el principio del aviso, o null.
            string yaNoEsta = null;
            if (p.Tipo == TipoPeriodo.CicloPasado)
            {
                ciclo = lectura.CiclosPasados.FirstOrDefault(c => c.Desde == periodo.Desde)
                    ?? lectura.CiclosPasados.FirstOrDefault(c => c.Desde <= periodo.Desde && periodo.Desde <= c.Hasta);
                if (ciclo == null)
                {
                    p.Tipo = TipoPeriodo.Ciclo;
                    yaNoEsta = "Ese ciclo ya no está en la lista (cambiaron las fechas de sus clases): ";
                }
            }
            else if (p.Tipo == TipoPeriodo.Plantilla)
            {
                plantilla = lectura.Plantillas.FirstOrDefault(x => x.Id == periodo.Plantilla);
                if (plantilla == null)
                {
                    p.Tipo = TipoPeriodo.Ciclo;
                    yaNoEsta = "Ese horario aplicado ya no está en la lista: ";
                }
            }

            if (p.Tipo == TipoPeriodo.Ciclo && !lectura.CicloDesde.HasValue)
            {
                if (lectura.CiclosPasados.Count > 0)
                {
                    // Entre ciclos: el ultimo que termino (y se sigue pidiendo "Este ciclo").
                    p.Tipo = TipoPeriodo.CicloPasado;
                    ciclo = lectura.CiclosPasados[0];
                    p.Aviso = yaNoEsta != null ? yaNoEsta + "se ve el último que terminó." : "Hoy no hay ningún ciclo en curso: se ve el último que terminó.";
                }
                else
                {
                    p.Tipo = TipoPeriodo.Ultimos30;
                    p.Aviso = yaNoEsta != null ? yaNoEsta + "se ven los últimos 30 días." : null;
                }
            }
            else if (yaNoEsta != null)
            {
                p.Aviso = yaNoEsta + "se ve este ciclo.";
            }

            switch (p.Tipo)
            {
                case TipoPeriodo.Ciclo:
                    p.Desde = lectura.CicloDesde.Value;
                    break;
                case TipoPeriodo.CicloPasado:
                    p.Ciclo = ciclo;
                    p.Desde = ciclo.Desde;
                    p.Hasta = ciclo.Hasta < hoy ? ciclo.Hasta : hoy;
                    break;
                case TipoPeriodo.Mes:
                    p.Desde = new DateTime(hoy.Year, hoy.Month, 1);
                    break;
                case TipoPeriodo.Semana:
                    p.Desde = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
                    break;
                case TipoPeriodo.Fechas:
                    p.HastaHoy = periodo.HastaHoy;
                    p.Hasta = periodo.HastaHoy || periodo.Hasta > hoy ? hoy : periodo.Hasta;
                    p.Desde = periodo.Desde > p.Hasta ? p.Hasta : periodo.Desde;
                    break;
                case TipoPeriodo.Plantilla:
                    // Como EntreFechas con sus dias (ya llegan a hoy como mucho).
                    p.Plantilla = plantilla;
                    p.Hasta = plantilla.Hasta < hoy ? plantilla.Hasta : hoy;
                    p.Desde = plantilla.Desde > p.Hasta ? p.Hasta : plantilla.Desde;
                    break;
                default:
                    p.Desde = hoy.AddDays(-29);
                    break;
            }

            p.EsCiclo = p.Tipo == TipoPeriodo.Ciclo || p.Tipo == TipoPeriodo.CicloPasado;
            return p;
        }

        /// <summary>
        /// El filtro con que se calcula: los dias del periodo (con el reloj, E0 y los ciclos
        /// de la lectura) y los combos. Si el docente o el grupo elegido no tiene clases en
        /// el periodo, vuelve a "Todos" y Aviso lo dice (sec. 6.5). Un laboratorio que ya
        /// no existe, tambien a "Todos". AvisoDelPeriodo dice si se ve otro periodo o si unas
        /// fechas son las de un ciclo (para el derecho a evaluacion hay que elegir el ciclo).
        /// </summary>
        public static FiltroEstadisticas Resolver(PeriodoElegido periodo, LecturaEstadisticas lectura, int laboratorio, int docente, int grupo)
        {
            return Resolver(periodo, lectura, laboratorio, docente, grupo, false);
        }

        /// <summary>
        /// Como Resolver; con docenteFijo (la ficha del docente) el docente nunca vuelve a
        /// "Todo el personal docente" aunque no tenga clases en el periodo: se calcula solo lo
        /// suyo (sus sesiones, sin clases), sin Aviso. Estadisticas no lo usa.
        /// </summary>
        public static FiltroEstadisticas Resolver(PeriodoElegido periodo, LecturaEstadisticas lectura, int laboratorio, int docente, int grupo, bool docenteFijo)
        {
            PeriodoResuelto p = ResolverPeriodo(periodo, lectura);
            FiltroEstadisticas filtro = new FiltroEstadisticas
            {
                Tipo = p.Tipo,
                Desde = p.Desde,
                Hasta = p.Hasta,
                EsCiclo = p.EsCiclo,
                Ciclo = p.Ciclo,
                Plantilla = p.Plantilla,
                HastaHoy = p.HastaHoy,
                AvisoDelPeriodo = p.Aviso ?? AvisoDeFechasDeUnCiclo(p, lectura),
                Laboratorio = laboratorio,
                Docente = docente,
                Grupo = grupo,
                SoloConsulta = Permisos.SoloConsulta
            };
            if (filtro.Laboratorio != 0 && !lectura.Laboratorios.ContainsKey(filtro.Laboratorio))
            {
                filtro.Laboratorio = 0;
            }

            DateTime desdeClases = DesdeDeLasClases(filtro.Desde, filtro.Hasta, filtro.EsCiclo, lectura.Hoy);
            List<ClaseLeida> delPeriodo = lectura.Asistencia.Clases
                .Where(c => AsistenciaDatos.DelPeriodo(c, desdeClases, filtro.Hasta, filtro.EsCiclo, filtro.SoloEstas)).ToList();
            bool sinDocente = !docenteFijo && filtro.Docente != 0 && !delPeriodo.Any(c => c.ProfesorId == filtro.Docente);
            bool sinGrupo = filtro.Grupo != 0 && !delPeriodo.Any(c => c.GrupoId == filtro.Grupo);
            if (sinDocente)
            {
                filtro.Docente = 0;
            }

            if (sinGrupo)
            {
                filtro.Grupo = 0;
            }

            string enElPeriodo = filtro.EsCiclo ? "en este ciclo" : "en estas fechas";
            filtro.Aviso = sinDocente && sinGrupo ? "Ese docente y ese grupo no tienen clases " + enElPeriodo + ": se ve todo."
                : sinDocente ? "Ese docente no tiene clases " + enElPeriodo + ": se ve todo el personal."
                : sinGrupo ? "Ese grupo no tiene clases " + enElPeriodo + ": se ven todos."
                : null;
            return filtro;
        }

        /// <summary>Con Fechas iguales a las de "Este ciclo" (hasta hoy) o a las de un ciclo pasado, que el derecho a evaluacion sale eligiendo el ciclo; o null.</summary>
        private static string AvisoDeFechasDeUnCiclo(PeriodoResuelto p, LecturaEstadisticas lectura)
        {
            const string ParaElDerecho = ": para el derecho a evaluación (80 %), elígelo en Periodo.";
            if (p.Tipo != TipoPeriodo.Fechas)
            {
                return null;
            }

            if (lectura.CicloDesde.HasValue && p.Desde == lectura.CicloDesde.Value && p.Hasta == lectura.Hoy)
            {
                return "Son las fechas de «" + NombreDeEsteCiclo(lectura.CicloEnCurso) + "»" + ParaElDerecho;
            }

            CicloEscolar igual = lectura.CiclosPasados.FirstOrDefault(c => c.Desde == p.Desde && c.Hasta == p.Hasta);
            return igual == null ? null : "Son las fechas del " + CicloEnLaFrase(TextoDeCiclo(igual)) + ParaElDerecho;
        }

        /// <summary>
        /// TextoDeCiclo dentro de una frase ("Son las fechas del ..."): "Ciclo abril-septiembre
        /// 2026" pasa a "ciclo abril-septiembre 2026"; un nombre aplicado escrito de otra forma
        /// ("CICLO ESCOLAR 2026-1"), tal cual.
        /// </summary>
        private static string CicloEnLaFrase(string texto)
        {
            return texto.StartsWith("Ciclo ", StringComparison.Ordinal) ? MinusculaInicial(texto) : texto;
        }

        /// <summary>
        /// Las opciones del combo Periodo, en orden: "Este ciclo" (sin lectura, o si hoy vale
        /// alguna clase), un renglon por cada ciclo que ya termino (del mas reciente al mas
        /// viejo), un renglon por cada plantilla aplicada que ya empezo (de la mas reciente a
        /// la mas vieja; sin db/16 y db/23, ninguno), "Este mes", "Esta semana" y "Ultimos 30
        /// dias". Las fechas elegidas y "Elegir fechas..." las pone SelectorDePeriodo.Llenar.
        /// </summary>
        public static List<PeriodoElegido> OpcionesDePeriodo(LecturaEstadisticas lectura)
        {
            List<PeriodoElegido> opciones = new List<PeriodoElegido>();
            if (lectura == null || lectura.CicloDesde.HasValue)
            {
                opciones.Add(PeriodoElegido.De(TipoPeriodo.Ciclo));
            }

            if (lectura != null)
            {
                opciones.AddRange(lectura.CiclosPasados.Select(PeriodoElegido.DelCiclo));
                opciones.AddRange(lectura.Plantillas.Select(PeriodoElegido.DePlantilla));
            }

            opciones.Add(PeriodoElegido.De(TipoPeriodo.Mes));
            opciones.Add(PeriodoElegido.De(TipoPeriodo.Semana));
            opciones.Add(PeriodoElegido.De(TipoPeriodo.Ultimos30));
            return opciones;
        }

        /// <summary>
        /// El texto del combo: "Este ciclo (desde el 30/09/2026)" (con el nombre del ciclo
        /// aplicado, "Este ciclo: 2027-1 (desde el 30/09/2026)"), "Ciclo abril-septiembre
        /// 2026", "Horario «2026-2»: desde el 11/10/2026 (en curso)", "Este mes", "Esta
        /// semana", "Ultimos 30 dias", "Del 01/10/2026 al 15/10/2026" o "Del 01/10/2026 a hoy".
        /// Sin lectura todavia, "Este ciclo" (y un ciclo pasado o una plantilla con su nombre de
        /// cuando se eligio).
        /// </summary>
        public static string TextoDePeriodo(PeriodoElegido p, LecturaEstadisticas lectura)
        {
            switch (p.Tipo)
            {
                case TipoPeriodo.Ciclo:
                    return lectura != null && lectura.CicloDesde.HasValue
                        ? NombreDeEsteCiclo(lectura.CicloEnCurso) + " (desde el " + Fecha(lectura.CicloDesde.Value) + ")"
                        : "Este ciclo";
                case TipoPeriodo.CicloPasado:
                    {
                        CicloEscolar ciclo = lectura == null ? null : lectura.CiclosPasados.FirstOrDefault(c => c.Desde == p.Desde);
                        return ciclo != null ? TextoDeCiclo(ciclo) : p.Nombre;
                    }

                case TipoPeriodo.Plantilla:
                    {
                        PlantillaAplicada plantilla = lectura == null ? null : lectura.Plantillas.FirstOrDefault(x => x.Id == p.Plantilla);
                        return plantilla != null ? TextoDePlantilla(plantilla) : p.Nombre;
                    }

                case TipoPeriodo.Mes:
                    return "Este mes";
                case TipoPeriodo.Semana:
                    return "Esta semana";
                case TipoPeriodo.Ultimos30:
                    return "Últimos 30 días";
                default:
                    return p.HastaHoy ? DelAHoy(p.Desde) : DelAl(p.Desde, p.Hasta);
            }
        }

        /// <summary>"Del 05/10/2026 al 11/10/2026" (el periodo completo, para la ayuda del combo y lblResumen).</summary>
        public static string DelAl(DateTime desde, DateTime hasta)
        {
            return "Del " + Fecha(desde) + " al " + Fecha(hasta);
        }

        /// <summary>"Del 01/10/2026 a hoy" (unas fechas con "Hasta hoy").</summary>
        public static string DelAHoy(DateTime desde)
        {
            return "Del " + Fecha(desde) + " a hoy";
        }

        /// <summary>
        /// El renglon de una plantilla aplicada en el combo: "Horario «2026-1»: del 01/09/2026 al
        /// 02/11/2026" o, si su horario sigue valiendo, "Horario «2026-2»: desde el 03/11/2026 (en curso)".
        /// </summary>
        internal static string TextoDePlantilla(PlantillaAplicada p)
        {
            return NombreDePlantilla(p) + ": " + (p.EnCurso ? "desde el " + Fecha(p.Desde) + " (en curso)" : "del " + Fecha(p.Desde) + " al " + Fecha(p.Hasta));
        }

        /// <summary>"Horario «2026-1»" (null: "Horario aplicado").</summary>
        internal static string NombreDePlantilla(PlantillaAplicada p)
        {
            return p == null ? "Horario aplicado" : "Horario «" + p.Nombre + "»";
        }

        /// <summary>
        /// Por que termina una plantilla que ya no sigue (sin parentesis ni punto): "despues se
        /// aplico «2026-2»" si otra la sustituyo, o "completo" si su horario acabo solo; "" en curso.
        /// </summary>
        private static string FinDePlantilla(PlantillaAplicada p)
        {
            return p == null || p.EnCurso ? "" : p.SustituidaPor.Length > 0 ? "después se aplicó «" + p.SustituidaPor + "»" : "completo";
        }

        /// <summary>"Este ciclo" o, si se aplico con una plantilla de Horarios > Aplicacion, "Este ciclo: 2027-1".</summary>
        private static string NombreDeEsteCiclo(CicloEscolar enCurso)
        {
            return enCurso != null && enCurso.NombreAplicado ? "Este ciclo: " + NombreAplicadoComoSeLee(enCurso.Nombre) : "Este ciclo";
        }

        /// <summary>
        /// El fin de "Este ciclo" (la ayuda del combo y el PDF), sin punto final: "el ciclo
        /// termina el 28/02/2027" o, si ese dia ya paso porque alguna clase se alargo, "la
        /// mayoria de sus clases terminaron el 20/10/2026 y 1 clase sigue hasta el 01/02/2027".
        /// </summary>
        internal static string FinDeEsteCiclo(CicloEscolar c, DateTime hoy)
        {
            if (c.Hasta >= hoy.Date)
            {
                return "el ciclo termina el " + Fecha(c.Hasta);
            }

            return "la mayoría de sus clases terminaron el " + Fecha(c.Hasta) + " y "
                + (c.SiguenHoy > 1 ? Miles(c.SiguenHoy) + " clases siguen (la última, hasta el " + Fecha(c.HastaDeSusClases) + ")"
                    : c.SiguenHoy == 1 ? "1 clase sigue hasta el " + Fecha(c.HastaDeSusClases)
                    : "otras siguieron hasta el " + Fecha(c.HastaDeSusClases));
        }

        /// <summary>
        /// La ayuda del combo Periodo (sec. 1.2): "Del 30/09/2026 al 31/10/2026 (hoy): todo
        /// este ciclo hasta hoy. El ciclo termina el 28/02/2027.", "Ciclo septiembre
        /// 2026-febrero 2027: del 30/09/2026 al 28/02/2027, completo (68 clases).", "Del ...
        /// al ... (hoy): el ultimo dia avanza solo." o "Del ... al ... (hoy).". Con los dias
        /// que de verdad cuentan; null sin nada calculado.
        /// </summary>
        public static string AyudaDelPeriodo(ResultadoEstadisticas r, LecturaEstadisticas l)
        {
            if (r == null || l == null)
            {
                return null;
            }

            string dias = DelAl(r.Desde, r.Hasta);
            // Mas de 400 dias: solo cuentan los ultimos 400 (lo dice tambien lblResumen).
            string empezo = " Empezó el " + Fecha(r.Filtro.Desde) + ", pero DigBit cuenta como mucho " + DiasLeidos + " días seguidos.";
            switch (r.Filtro.Tipo)
            {
                case TipoPeriodo.Ciclo:
                    return (r.Ciclo != null && r.Ciclo.NombreAplicado
                            ? NombreDeEsteCiclo(r.Ciclo) + " (el nombre que se le puso en Horarios > Aplicación). " : "")
                        + dias + (r.Recortado ? " (hoy): los últimos " + DiasLeidos + " días de este ciclo." + empezo : " (hoy): todo este ciclo hasta hoy.")
                        + (r.Ciclo != null ? " " + MayusculaInicial(FinDeEsteCiclo(r.Ciclo, l.Hoy)) + "." : "")
                        + (l.CiclosPasados.Count == 0 ? " Los ciclos que terminen saldrán en esta lista." : "");
                case TipoPeriodo.CicloPasado:
                    {
                        CicloEscolar c = r.Ciclo;
                        // Con filtros (la ficha del docente: Docente = el), las clases son las suyas y no
                        // las del ciclo en toda la escuela, como en las definiciones del PDF (ClasesDeLaDefinicion).
                        // Estadisticas la pide con lo general: ahi no cambia.
                        bool conFiltros = r.Filtro.Laboratorio != 0 || r.Filtro.Docente != 0 || r.Filtro.Grupo != 0;
                        bool deUnDocente = r.Filtro.Docente != 0 && r.Filtro.Laboratorio == 0 && r.Filtro.Grupo == 0;
                        string cuantas = !conFiltros ? Contar(r.ClasesDelPeriodo, "clase", "clases")
                            : deUnDocente ? Contar(r.Clases.Count, "clase suya", "clases suyas") + " de " + Miles(r.ClasesDelPeriodo) + " del ciclo"
                            : Contar(r.Clases.Count, "clase", "clases") + " con estos filtros, de " + Miles(r.ClasesDelPeriodo);
                        string ayuda = TextoDeCiclo(c) + ": del " + Fecha(r.Desde) + " al " + Fecha(r.Hasta)
                            + (r.Recortado ? ", sus últimos " + DiasLeidos + " días (" : ", completo (")
                            + cuantas + ")."
                            + (r.Recortado ? empezo : "");
                        int siguen = c.SiguenDespues;
                        DateTime hastaSiguen = c.HastaDeSusClases;
                        if (conFiltros)
                        {
                            // Las de esos filtros que siguieron despues del fin del ciclo.
                            HashSet<int> filtradas = new HashSet<int>(r.Clases.Select(x => x.ClaseId));
                            List<ClaseLeida> siguieron = l.Asistencia.Clases.Where(x => filtradas.Contains(x.Id) && x.VigenteHasta > c.Hasta).ToList();
                            siguen = siguieron.Count;
                            hastaSiguen = siguen == 0 ? c.HastaDeSusClases : siguieron.Max(x => x.VigenteHasta);
                        }

                        if (siguen > 0)
                        {
                            ayuda += " " + (siguen == 1 ? (deUnDocente ? "1 clase suya siguió" : "1 clase siguió")
                                    : Miles(siguen) + (deUnDocente ? " clases suyas siguieron" : " clases siguieron"))
                                + " hasta el " + Fecha(hastaSiguen) + ": esos días no entran; para verlos, elige fechas.";
                        }

                        return ayuda;
                    }

                case TipoPeriodo.Plantilla:
                    {
                        PlantillaAplicada p = r.Plantilla;
                        string delAl = "del " + Fecha(r.Desde) + " al " + Fecha(r.Hasta);
                        string cuando = p == null ? delAl + "."
                            : p.EnCurso ? delAl + " (hoy), los días en que vale hasta hoy"
                                + (p.UltimoDia == DateTime.MaxValue.Date ? "."
                                    : "; su horario termina el " + Fecha(p.UltimoDia) + (p.SustituidaPor.Length > 0 ? ", cuando lo sustituye «" + p.SustituidaPor + "»." : "."))
                            : p.SustituidaPor.Length > 0 ? delAl + ", los días en que valió: después se aplicó «" + p.SustituidaPor + "»."
                            : delAl + (r.Recortado ? "." : ", completo.");
                        return NombreDePlantilla(p) + ", aplicado en Horarios > Aplicación: " + cuando
                            + " Se cuenta como unas fechas elegidas: todas las clases del horario que tocan esos días, y el 80 % es el de esos días, no el derecho a evaluación de un ciclo."
                            + (r.Recortado ? empezo : "");
                    }

                case TipoPeriodo.Fechas:
                    {
                        // Un "Del ... a hoy" que crecio con DigBit abierto puede pasar de 400 dias.
                        string recorte = r.Recortado ? " Elegiste desde el " + Fecha(r.Filtro.Desde) + ", pero DigBit cuenta como mucho " + DiasLeidos + " días seguidos." : "";
                        return r.Filtro.HastaHoy ? dias + " (hoy): el último día avanza solo." + recorte
                            : dias + (r.Hasta == l.Hoy ? " (hoy)." : ".") + recorte;
                    }
            }

            return dias + (r.Hasta == l.Hoy ? " (hoy)." : ".");
        }

        /// <summary>
        /// El nombre del periodo sin sus fechas (el renglon Periodo del reporte y del CSV):
        /// "Este ciclo (septiembre 2026-febrero 2027), en curso hasta hoy" (con una plantilla
        /// aplicada, "Este ciclo: 2027-1, en curso hasta hoy"), "Ciclo septiembre
        /// 2026-febrero 2027, completo", "Horario «2026-2», en curso hasta hoy" (o "Horario
        /// «2026-1», hasta que se aplico «2026-2»", o ", completo"), "Este mes, hasta hoy", "Esta
        /// semana, hasta hoy", "Ultimos 30 dias, hasta hoy", "Fechas elegidas" o "Fechas elegidas, hasta hoy". Si
        /// pasa de 400 dias (Recortado), en vez de "completo" o "en curso hasta hoy", "sus
        /// ultimos 400 dias" ("Este ciclo (...), en curso, sus ultimos 400 dias hasta hoy").
        /// </summary>
        public static string NombreDelPeriodo(ResultadoEstadisticas r)
        {
            // Mas de 400 dias: no es "completo" ni todo el ciclo, solo sus ultimos 400 dias.
            string ultimos = "sus últimos " + DiasLeidos + " días";
            switch (r.Filtro.Tipo)
            {
                case TipoPeriodo.Ciclo:
                    {
                        string enCurso = r.Recortado ? ", en curso, " + ultimos + " hasta hoy" : ", en curso hasta hoy";
                        if (r.Ciclo == null)
                        {
                            return "Este ciclo" + enCurso;
                        }

                        return r.Ciclo.NombreAplicado
                            ? NombreDeEsteCiclo(r.Ciclo) + enCurso
                            : "Este ciclo (" + MinusculaInicial(r.Ciclo.Nombre) + ")" + enCurso;
                    }

                case TipoPeriodo.CicloPasado:
                    return (r.Ciclo == null ? "Ciclo" : TextoDeCiclo(r.Ciclo)) + (r.Recortado ? ", " + ultimos : ", completo");
                case TipoPeriodo.Plantilla:
                    {
                        // "Horario «2026-2», en curso hasta hoy", "Horario «2026-1», hasta que se aplicó «2026-2»" o "Horario «2026-1», completo".
                        PlantillaAplicada p = r.Plantilla;
                        bool enCurso = p != null && p.EnCurso;
                        bool sustituida = p != null && !enCurso && p.SustituidaPor.Length > 0;
                        return NombreDePlantilla(p)
                            + (r.Recortado ? ", " + ultimos + (enCurso ? " hasta hoy" : "")
                                : enCurso ? ", en curso hasta hoy"
                                : sustituida ? ", hasta que se aplicó «" + p.SustituidaPor + "»"
                                : ", completo");
                    }

                case TipoPeriodo.Mes:
                    return "Este mes, hasta hoy";
                case TipoPeriodo.Semana:
                    return "Esta semana, hasta hoy";
                case TipoPeriodo.Ultimos30:
                    return "Últimos 30 días, hasta hoy";
                default:
                    return (r.Filtro.HastaHoy ? "Fechas elegidas, hasta hoy" : "Fechas elegidas") + (r.Recortado ? ", " + ultimos : "");
            }
        }

        /// <summary>"Este ciclo (en curso)" (o "Este ciclo: 2027-1 (en curso)"), el de un ciclo pasado ("Ciclo septiembre 2026-febrero 2027"), el de una plantilla ("Horario «2026-2» (en curso)") o "".</summary>
        public static string NombreCortoDelPeriodo(ResultadoEstadisticas r)
        {
            switch (r.Filtro.Tipo)
            {
                case TipoPeriodo.Ciclo:
                    return NombreDeEsteCiclo(r.Ciclo) + " (en curso)";
                case TipoPeriodo.CicloPasado:
                    return r.Ciclo == null ? "" : TextoDeCiclo(r.Ciclo);
                case TipoPeriodo.Plantilla:
                    return NombreDePlantilla(r.Plantilla) + (r.Plantilla != null && r.Plantilla.EnCurso ? " (en curso)" : "");
                default:
                    return "";
            }
        }

        /// <summary>"Ciclo septiembre 2026-febrero 2027, del 30/09/2026 al 28/02/2027", "Este ciclo (en curso), del ..." o "Del ... al ...": la linea de Graficas.</summary>
        public static string CabezaDelPeriodo(ResultadoEstadisticas r)
        {
            string corto = NombreCortoDelPeriodo(r);
            return corto.Length > 0 ? corto + ", del " + Fecha(r.Desde) + " al " + Fecha(r.Hasta) : DelAl(r.Desde, r.Hasta);
        }

        /// <summary>Con un ciclo pasado, lo que va al final del renglon "Generado el" del reporte; null en lo demas.</summary>
        public static string NotaDelPeriodoParaElReporte(ResultadoEstadisticas r)
        {
            return r != null && r.CicloCerrado
                ? " Es un cálculo de hoy, no el cierre oficial del ciclo: si se corrigen datos de esas fechas, las cifras cambian."
                : null;
        }

        /// <summary>Con un ciclo pasado, la nota del membrete del CSV (despues de "Nota: ").</summary>
        public const string NotaDeCicloCerrado = "se calcula con los datos de hoy (bajas, nombres y el grupo de cada estudiante como están ahora); "
            + "no es un cierre congelado del ciclo.";

        /// <summary>
        /// Lo que dice el dialogo "Elegir fechas" si no valen (sec. 1.5), o null: cualquier dia
        /// hasta hoy, con como mucho 400 dias entre los dos (como Faltas: LeerCalendario lee
        /// 400 como mucho).
        /// </summary>
        public static string ValidarFechas(DateTime desde, DateTime hasta, DateTime hoy)
        {
            if (desde.Date > hasta.Date)
            {
                return "El primer día no puede ser después del último.";
            }

            if (hasta.Date > hoy.Date)
            {
                return "El último día no puede ser después de hoy.";
            }

            if ((hasta.Date - desde.Date).TotalDays >= HorariosDatos.DiasMaximosCalendario)
            {
                return "Elige como mucho " + HorariosDatos.DiasMaximosCalendario + " días (un poco más de un año). Si hace falta más, hazlo en varias veces.";
            }

            return null;
        }

        // =====================================================================
        // Ciclos (sin base)
        // =====================================================================

        /// <summary>
        /// Los ciclos que ya terminaron, del mas reciente al mas viejo, de las clases activas
        /// que ya empezaron (E1), las que tienen franjas (E3) y los nombres de los aplicados
        /// (sec. 2 de la especificacion de periodos). Las clases pasadas son las que empezaron
        /// antes de "Este ciclo" (cicloDesde); solo las que tienen horario abren un ciclo. Un
        /// dia de inicio d de una de ellas, a 14 dias o mas del ultimo inicio, abre un ciclo
        /// si es despues del fin del ciclo (el vigente_hasta que mas se repite en sus clases
        /// con horario) o si las que empiezan en [d, d + 14) terminan 28 dias o mas despues
        /// de ese fin y, ademas, son 3 o mas y al menos la mitad de las que seguian, o d cae
        /// en las ultimas 3 semanas del ciclo. Cada clase es del ciclo en que empezo; una sin
        /// horario de antes del primer inicio no es de ninguno (solo sale con fechas).
        /// </summary>
        internal static List<CicloEscolar> ArmarCiclos(IList<ClaseLeida> clases, ISet<int> conHorario, DateTime? cicloDesde, DateTime hoy,
            IDictionary<DateTime, string> nombresAplicados)
        {
            hoy = hoy.Date;
            List<ClaseLeida> pasadas = clases
                .Where(c => c.VigenteDesde <= hoy && (!cicloDesde.HasValue || c.VigenteDesde < cicloDesde.Value.Date))
                .OrderBy(c => c.VigenteDesde).ThenBy(c => c.Id)
                .ToList();
            List<ClaseLeida> fuertes = pasadas.Where(c => conHorario.Contains(c.Id)).ToList();

            List<DateTime> inicios = new List<DateTime>();
            foreach (DateTime d in fuertes.Select(c => c.VigenteDesde).Distinct().OrderBy(x => x))
            {
                if (inicios.Count == 0)
                {
                    inicios.Add(d);
                    continue;
                }

                DateTime inicio = inicios[inicios.Count - 1];
                if (d < inicio.AddDays(VentanaDeInicio))
                {
                    continue;
                }

                // El ciclo hasta d (sus clases con horario que empezaron en [inicio, d)) y el bloque que empieza en d.
                DateTime fin = FinMasComun(fuertes.Where(c => c.VigenteDesde >= inicio && c.VigenteDesde < d)).Value;
                List<ClaseLeida> nuevas = fuertes.Where(c => c.VigenteDesde >= d && c.VigenteDesde < d.AddDays(VentanaDeInicio)).ToList();
                int seguian = fuertes.Count(c => c.VigenteDesde < d && c.VigenteHasta >= d.AddDays(-1));
                bool despues = d > fin;
                bool masLargo = FinMasComun(nuevas).Value >= fin.AddDays(TerminaDespues);
                bool grande = nuevas.Count >= MinimoDeUnBloque && 2 * nuevas.Count >= seguian;
                bool alFinal = d >= fin.AddDays(-UltimasSemanas);
                if (despues || (masLargo && (grande || alFinal)))
                {
                    inicios.Add(d);
                }
            }

            List<CicloEscolar> ciclos = new List<CicloEscolar>();
            for (int i = 0; i < inicios.Count; i++)
            {
                DateTime desde = inicios[i];
                DateTime? siguiente = i + 1 < inicios.Count ? inicios[i + 1] : (DateTime?)null;
                List<ClaseLeida> suyas = pasadas.Where(c => c.VigenteDesde >= desde && (!siguiente.HasValue || c.VigenteDesde < siguiente.Value)).ToList();
                DateTime hasta = FinMasComun(suyas.Where(c => conHorario.Contains(c.Id))).Value;
                CicloEscolar ciclo = new CicloEscolar
                {
                    Desde = desde,
                    Hasta = hasta,
                    HastaDeSusClases = suyas.Max(c => c.VigenteHasta),
                    SiguenDespues = suyas.Count(c => c.VigenteHasta > hasta),
                    Clases = new HashSet<int>(suyas.Select(c => c.Id))
                };
                PonerNombre(ciclo, nombresAplicados);
                ciclos.Add(ciclo);
            }

            ciclos.Reverse();
            foreach (IGrouping<string, CicloEscolar> mismos in ciclos.GroupBy(TextoBaseDeCiclo, PorNombre))
            {
                if (mismos.Count() > 1)
                {
                    foreach (CicloEscolar ciclo in mismos)
                    {
                        ciclo.NombreRepetido = true;
                    }
                }
            }

            return ciclos;
        }

        /// <summary>
        /// "Este ciclo" como ciclo (para su nombre, su fin y sus clases): las clases que
        /// empezaron de cicloDesde a hoy; su fin, el vigente_hasta que mas se repite en las
        /// que tienen horario (sin ninguna, ciclo_hasta de E0). El nombre, como el de los pasados.
        /// </summary>
        internal static CicloEscolar CicloEnCursoDe(IList<ClaseLeida> clases, ISet<int> conHorario, DateTime cicloDesde, DateTime? cicloHasta, DateTime hoy,
            IDictionary<DateTime, string> nombresAplicados)
        {
            List<ClaseLeida> suyas = clases.Where(c => c.VigenteDesde >= cicloDesde.Date && c.VigenteDesde <= hoy.Date).ToList();
            DateTime hasta = FinMasComun(suyas.Where(c => conHorario.Contains(c.Id))) ?? (cicloHasta ?? hoy).Date;
            CicloEscolar ciclo = new CicloEscolar
            {
                Desde = cicloDesde.Date,
                Hasta = hasta,
                HastaDeSusClases = suyas.Count == 0 ? hasta : suyas.Max(c => c.VigenteHasta),
                SiguenDespues = suyas.Count(c => c.VigenteHasta > hasta),
                SiguenHoy = suyas.Count(c => c.VigenteHasta >= hoy.Date),
                Clases = new HashSet<int>(suyas.Select(c => c.Id))
            };
            PonerNombre(ciclo, nombresAplicados);
            return ciclo;
        }

        /// <summary>El vigente_hasta que mas se repite (con empate, el mas tardio), o null sin clases.</summary>
        private static DateTime? FinMasComun(IEnumerable<ClaseLeida> clases)
        {
            IGrouping<DateTime, ClaseLeida> mas = clases.GroupBy(c => c.VigenteHasta)
                .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                .FirstOrDefault();
            return mas == null ? (DateTime?)null : mas.Key;
        }

        /// <summary>El del ciclo aplicado con ese primer dia (NombreAplicado) o el de sus meses (CicloPreliminarDatos.NombrePropuesto).</summary>
        private static void PonerNombre(CicloEscolar ciclo, IDictionary<DateTime, string> nombresAplicados)
        {
            string aplicado;
            if (nombresAplicados != null && nombresAplicados.TryGetValue(ciclo.Desde, out aplicado) && !string.IsNullOrWhiteSpace(aplicado))
            {
                ciclo.Nombre = aplicado.Trim();
                ciclo.NombreAplicado = true;
                return;
            }

            ciclo.Nombre = CicloPreliminarDatos.NombrePropuesto(ciclo.Desde, ciclo.Hasta);
            ciclo.NombreAplicado = false;
        }

        /// <summary>La primera letra en minuscula (es-MX): "Ciclo abril-septiembre 2026" pasa a "ciclo abril-septiembre 2026".</summary>
        internal static string MinusculaInicial(string texto)
        {
            return string.IsNullOrEmpty(texto) ? "" : Espanol.TextInfo.ToLower(texto[0]) + texto.Substring(1);
        }

        private static string MayusculaInicial(string texto)
        {
            return string.IsNullOrEmpty(texto) ? "" : Espanol.TextInfo.ToUpper(texto[0]) + texto.Substring(1);
        }

        /// <summary>
        /// "Ciclo septiembre 2026-febrero 2027" (de sus meses) o el nombre aplicado
        /// (NombreAplicadoComoSeLee): tal cual si ya empieza con "Ciclo" (sin mirar
        /// mayusculas) y, si no, "Ciclo " y el nombre.
        /// </summary>
        private static string TextoBaseDeCiclo(CicloEscolar c)
        {
            string nombre = c.NombreAplicado ? NombreAplicadoComoSeLee(c.Nombre) : MinusculaInicial(c.Nombre);
            return nombre.StartsWith("ciclo ", StringComparison.OrdinalIgnoreCase) ? MayusculaInicial(nombre) : "Ciclo " + nombre;
        }

        /// <summary>
        /// Un nombre aplicado como se lee detras de "Ciclo " o de "Este ciclo: ". El que propone
        /// Horarios > Aplicacion (CicloPreliminarDatos.NombrePropuesto, "Abril-septiembre 2026")
        /// lleva el mes en minuscula, como los de sus meses: "Ciclo abril-septiembre 2026". Solo
        /// si empieza con un mes y no tiene otra mayuscula; los demas ("2027-1", "Agosto-Diciembre
        /// 2026", "CICLO ESCOLAR 2026-1"), tal cual.
        /// </summary>
        private static string NombreAplicadoComoSeLee(string nombre)
        {
            nombre = (nombre ?? "").Trim();
            int largo = 0;
            while (largo < nombre.Length && char.IsLetter(nombre[largo]))
            {
                largo++;
            }

            string palabra = nombre.Substring(0, largo);
            bool esMes = largo > 0 && Espanol.DateTimeFormat.MonthNames.Any(m => m.Length > 0 && string.Compare(m, palabra, Espanol, CompareOptions.IgnoreCase) == 0);
            bool otraMayuscula = nombre.Skip(1).Any(char.IsUpper);
            return esMes && !otraMayuscula ? MinusculaInicial(nombre) : nombre;
        }

        /// <summary>Como sale un ciclo pasado en el combo: TextoBaseDeCiclo y, si otro se llama igual, " (desde el 05/06/2027)".</summary>
        internal static string TextoDeCiclo(CicloEscolar c)
        {
            return TextoBaseDeCiclo(c) + (c.NombreRepetido ? " (desde el " + Fecha(c.Desde) + ")" : "");
        }

        /// <summary>
        /// Las plantillas aplicadas que ya empezaron (su desde, hoy o antes), de la mas reciente a
        /// la mas vieja, con SOLO los dias en que valio cada una: de su desde al menor de su
        /// hasta, el fin_anterior de la aplicada siguiente (por desde; tambien una que aun no
        /// empieza: ese dia dejo de valer porque la sustituyeron) y hoy. Solo cuentan las de
        /// estado 'publicado' con desde (CicloPreliminar.Aplicada): una que se deshizo vuelve a
        /// 'borrador' y no corta a nadie. Sin fin_anterior en la siguiente, el dia antes de su
        /// desde. Una que se quedo sin dias (cortada antes de empezar) no sale.
        /// </summary>
        internal static List<PlantillaAplicada> ArmarPlantillas(IEnumerable<CicloPreliminar> configuraciones, DateTime hoy)
        {
            hoy = hoy.Date;
            List<CicloPreliminar> aplicadas = (configuraciones ?? Enumerable.Empty<CicloPreliminar>())
                .Where(c => c != null && c.Aplicada && c.Desde.HasValue)
                .OrderBy(c => c.Desde.Value).ThenBy(c => c.Id)
                .ToList();
            List<PlantillaAplicada> plantillas = new List<PlantillaAplicada>();
            for (int i = 0; i < aplicadas.Count; i++)
            {
                CicloPreliminar esta = aplicadas[i];
                DateTime desde = esta.Desde.Value.Date;
                if (desde > hoy)
                {
                    // Aun no empieza: no es un periodo (pero si corta a la de antes).
                    continue;
                }

                CicloPreliminar siguiente = i + 1 < aplicadas.Count ? aplicadas[i + 1] : null;
                DateTime? corte = siguiente == null ? (DateTime?)null : (siguiente.FinAnterior ?? siguiente.Desde.Value.AddDays(-1)).Date;
                // Sin hasta (no deberia pasar en una aplicada), hasta su corte o, sin el, abierta.
                DateTime hastaDelHorario = (esta.Hasta ?? corte ?? DateTime.MaxValue.Date).Date;
                bool cortada = corte.HasValue && corte.Value < hastaDelHorario;
                DateTime ultimo = cortada ? corte.Value : hastaDelHorario;
                DateTime hasta = ultimo < hoy ? ultimo : hoy;
                if (hasta < desde)
                {
                    continue;
                }

                plantillas.Add(new PlantillaAplicada
                {
                    Id = esta.Id,
                    Nombre = (esta.Nombre ?? "").Trim(),
                    Desde = desde,
                    Hasta = hasta,
                    UltimoDia = ultimo,
                    HastaDelHorario = hastaDelHorario,
                    SustituidaPor = cortada ? (siguiente.Nombre ?? "").Trim() : "",
                    EnCurso = ultimo >= hoy
                });
            }

            plantillas.Reverse();
            return plantillas;
        }

        /// <summary>
        /// Si esa pestana se guarda en CSV: la persona responsable del laboratorio solo
        /// guarda Laboratorios, Grupos y Equipos, que no llevan datos personales (sec. 7;
        /// en las demas, SinCsvParaSoloConsulta). En kiosco no hay CSV (lo decide la ventana).
        /// </summary>
        public static bool SeGuardaEnCsv(PestanaEstadisticas pestana, bool soloConsulta)
        {
            // Graficas guarda sus semanas (o meses) y las horas de cada laboratorio: sin nombres.
            return !soloConsulta || pestana == PestanaEstadisticas.Laboratorios || pestana == PestanaEstadisticas.Grupos || pestana == PestanaEstadisticas.Equipos
                || pestana == PestanaEstadisticas.Graficas;
        }

        /// <summary>
        /// Si el reporte para auditoria (PDF) lleva lo de cada docente (su grafica y su
        /// tabla): con la misma regla que el CSV de Docentes. La persona responsable del
        /// laboratorio lo ve en pantalla, pero el archivo que se lleva sale solo con los
        /// totales (SinDocentesEnElReporte lo dice en el PDF).
        /// </summary>
        public static bool ReporteConDocentes(bool soloConsulta)
        {
            return SeGuardaEnCsv(PestanaEstadisticas.Docentes, soloConsulta);
        }

        /// <summary>Lo que dice el reporte en lugar de lo de cada docente (ReporteConDocentes en false).</summary>
        public const string SinDocentesEnElReporte = "Este reporte no lleva lo de cada docente: como la pestaña Docentes, que no se guarda en Excel desde una cuenta "
            + "de responsable de laboratorio, sus nombres solo se ven en pantalla. Si lo necesitas, pídeselo a la administración de DigBit.";

        // =====================================================================
        // Formatos (R8)
        // =====================================================================

        /// <summary>Miles con coma: 1,284.</summary>
        public static string Miles(int n)
        {
            return n.ToString("#,0", Inv);
        }

        /// <summary>dd/MM/yyyy.</summary>
        public static string Fecha(DateTime dia)
        {
            return dia.ToString("dd/MM/yyyy", Inv);
        }

        /// <summary>"84 %" (truncado, como Mis grupos) o "-".</summary>
        public static string Porcentaje(double? valor)
        {
            return valor.HasValue ? ResumenAsistencia.PorcentajeMostrado(valor.Value) + " %" : SinDato;
        }

        /// <summary>"23 de 27" o "-" con N = 0.</summary>
        public static string XdeN(int x, int n)
        {
            return n == 0 ? SinDato : Miles(x) + " de " + Miles(n);
        }

        /// <summary>"1 clase", "12 clases".</summary>
        public static string Contar(int n, string uno, string varios)
        {
            return Miles(n) + " " + (n == 1 ? uno : varios);
        }

        /// <summary>"1 vez", "3 veces".</summary>
        public static string Veces(int n)
        {
            return Contar(n, "vez", "veces");
        }

        /// <summary>"lun 05/10 10:12" o, sin hora, "dom 20/09".</summary>
        public static string DiaYHora(DateTime dia, TimeSpan? hora)
        {
            return DiasCortos[(int)dia.DayOfWeek] + " " + dia.ToString("dd/MM", Inv)
                + (hora.HasValue ? " " + new DateTime(hora.Value.Ticks % TimeSpan.TicksPerDay).ToString("HH:mm", Inv) : "");
        }

        /// <summary>"a", "a y b", "a, b y c".</summary>
        private static string Unir(IList<string> partes)
        {
            if (partes.Count == 0)
            {
                return "";
            }

            return partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }

        /// <summary>El nombre del laboratorio, o "Sin laboratorio".</summary>
        private static string NombreDeLaboratorio(LecturaEstadisticas lectura, int id)
        {
            LaboratorioLeido laboratorio;
            return lectura.Laboratorios.TryGetValue(id, out laboratorio) && laboratorio.Nombre.Length > 0 ? laboratorio.Nombre : "Sin laboratorio";
        }

        // =====================================================================
        // Calculo (sin base)
        // =====================================================================

        /// <summary>
        /// Las cinco cifras, las seis tablas, las notas y los textos para ese periodo y
        /// esos filtros (R6). No consulta nada ni cambia la lectura: se puede llamar en
        /// segundo plano y a la vez que otro Calcular.
        /// </summary>
        public static ResultadoEstadisticas Calcular(LecturaEstadisticas lectura, FiltroEstadisticas filtro)
        {
            if (lectura == null || filtro == null)
            {
                throw new ArgumentNullException(lectura == null ? "lectura" : "filtro");
            }

            // Con una lectura que no cubre el periodo, las listas de sus clases saldrian
            // a medias sin que nadie lo note: quien llama tiene que leerlo antes (Leer).
            if (!CubreElPeriodo(lectura, filtro.Desde, filtro.Hasta, filtro.EsCiclo, filtro.SoloEstas))
            {
                throw new ArgumentException("Esa lectura no cubre el periodo elegido: léelo antes con EstadisticasDatos.Leer.", "filtro");
            }

            Stopwatch reloj = Stopwatch.StartNew();
            ResultadoEstadisticas resultado = new Calculo(lectura, filtro).Hacer();
            resultado.Duracion = reloj.Elapsed;
            return resultado;
        }

        /// <summary>Si es el nombre de Windows; ver Consultas.EsNombreDeWindows.</summary>
        private static bool EsDeWindows(string equipo)
        {
            return Consultas.EsNombreDeWindows(equipo);
        }

        /// <summary>Un calculo: lo de un periodo y unos filtros sobre una lectura.</summary>
        private sealed class Calculo
        {
            private const string ClaveTodoElLaboratorio = "#todo";
            private const string ClaveSinEquipo = "#sin";

            private readonly LecturaEstadisticas L;
            private readonly LecturaDeAsistencia A;
            private readonly FiltroEstadisticas F;
            private readonly ResultadoEstadisticas R = new ResultadoEstadisticas();
            private readonly DateTime hoy;
            /// <summary>Los dias que cuentan: de desde (recortado al ultimo dia - 399) a hasta (hoy como mucho).</summary>
            private readonly DateTime desde;
            private readonly DateTime hasta;
            private readonly bool recortado;
            private readonly int lab;
            /// <summary>
            /// Desde donde y hasta donde leeria una lectura hecha para este periodo
            /// (DesdeLecturaDe y HastaLecturaDe). La de la cache puede venir de otro
            /// periodo y traer mas dias: lo que se mira fuera del periodo se corta aqui,
            /// para que salga lo mismo que leido desde cero.
            /// </summary>
            private readonly DateTime desdeLeido;
            private readonly DateTime hastaLeido;
            /// <summary>Con que primer dia se eligen las clases (DesdeDeLasClases): el del ciclo o, con lo demas, desde.</summary>
            private readonly DateTime desdeDeClases;

            // Las clases del periodo (R2): todas (para los combos) y con docente y grupo.
            private List<ClaseLeida> delPeriodo;
            private List<ClaseLeida> clases;
            private HashSet<int> idsDeClases;
            private Dictionary<string, List<ClaseLeida>> clasesPorTerna;
            // Las sesiones del periodo con docente y grupo (sin el filtro de laboratorio).
            private List<SesionLeida> sesiones;
            private Dictionary<int, SesionLeida> sesionDelFiltro;
            // Cada bitacora contada (R5: un par sesion-estudiante una vez) y su sesion.
            private readonly List<KeyValuePair<SesionLeida, BitacoraLeida>> contadas = new List<KeyValuePair<SesionLeida, BitacoraLeida>>();
            private readonly List<KeyValuePair<SesionLeida, ReporteDeDocente>> reportes = new List<KeyValuePair<SesionLeida, ReporteDeDocente>>();
            // Por laboratorio (sin su filtro) y la asistencia de cada clase (con el).
            private readonly Dictionary<int, Acumulado> porLaboratorio = new Dictionary<int, Acumulado>();
            private readonly Acumulado todos = new Acumulado();
            private readonly Dictionary<int, AsistenciaDeLaClase> asistencia = new Dictionary<int, AsistenciaDeLaClase>();
            private readonly Dictionary<int, ResumenDia> calendarioPorClase = new Dictionary<int, ResumenDia>();
            private readonly Dictionary<int, HashSet<int>> laboratoriosDeLaClase = new Dictionary<int, HashSet<int>>();
            private readonly Dictionary<int, int> fallasPorClase = new Dictionary<int, int>();
            // Las clases-dia del calendario de cada clase (con el filtro de laboratorio): el detalle de Docentes.
            private readonly Dictionary<int, List<ClaseDelCalendario>> entradasDeLaClase = new Dictionary<int, List<ClaseDelCalendario>>();
            private readonly Dictionary<int, int> reportesPorDocente = new Dictionary<int, int>();
            private List<FilaEquipo> equipos;
            // Las semanas y los meses del periodo (Graficas), con el filtro de laboratorio.
            private readonly List<TramoDelPeriodo> semanas = new List<TramoDelPeriodo>();
            private readonly List<TramoDelPeriodo> meses = new List<TramoDelPeriodo>();
            private DateTime lunesDelPrimero;

            private sealed class Acumulado
            {
                public readonly ResumenDia Clases = new ResumenDia();
                // Las horas de sus clases del calendario (Graficas), con los estados de Clases.
                public readonly TramoDelPeriodo Horas = new TramoDelPeriodo();
                public int Bitacoras;
                public readonly HashSet<int> Estudiantes = new HashSet<int>();
                public int FallasDeBitacoras;
                public int Hardware;
                public int Software;
                public int Red;
                public int DeDocentes;
                public int DocHardware;
                public int DocSoftware;
                public int DocRed;
                public int Avisos;
                public int EquiposConAvisos;

                public int Fallas
                {
                    get { return FallasDeBitacoras + DeDocentes; }
                }

                public bool Algo
                {
                    get { return Bitacoras > 0 || DeDocentes > 0 || Avisos > 0; }
                }

                /// <summary>Algo que sume en "Todos": tambien clases del horario que contaban o canceladas (para que la suma cuadre).</summary>
                public bool AlgoQueSumar
                {
                    get { return Algo || Clases.ConBitacoras + Clases.Faltan + Clases.SinBitacorasConAsistencia + Clases.Canceladas > 0; }
                }
            }

            private sealed class AsistenciaDeLaClase
            {
                public ClaseLeida Clase;
                public int Estudiantes;
                public int X;
                public int N;
                public int Bajo;
                public int Dadas;
                public int DiasQueCuentan;
                public bool AlgunDiaEnElLaboratorio;
                public readonly List<ParDeAsistencia> Pares = new List<ParDeAsistencia>();
            }

            public Calculo(LecturaEstadisticas lectura, FiltroEstadisticas filtro)
            {
                L = lectura;
                A = lectura.Asistencia;
                F = filtro;
                hoy = lectura.Hoy;
                // Como mucho 400 dias seguidos (LeerCalendario), contados desde el ultimo que cuenta.
                DateTime hastaVisto = filtro.Hasta.Date > hoy ? hoy : filtro.Hasta.Date;
                DateTime primero = hastaVisto.AddDays(-(DiasLeidos - 1));
                recortado = filtro.Desde.Date < primero;
                desde = recortado ? primero : filtro.Desde.Date;
                hasta = hastaVisto;
                lab = filtro.Laboratorio;
                desdeDeClases = DesdeDeLasClases(filtro.Desde, filtro.Hasta, filtro.EsCiclo, hoy);
                desdeLeido = AsistenciaDatos.DesdeLecturaDe(A.Clases, desdeDeClases, filtro.Hasta, filtro.EsCiclo, filtro.SoloEstas);
                hastaLeido = AsistenciaDatos.HastaLecturaDe(A.Clases, desdeDeClases, filtro.Hasta, filtro.EsCiclo, filtro.SoloEstas, hoy);
            }

            private bool EnElFiltroDeLaboratorio(int? laboratorio)
            {
                return lab == 0 || (laboratorio ?? 0) == lab;
            }

            public ResultadoEstadisticas Hacer()
            {
                R.Filtro = F;
                R.Desde = desde;
                R.Hasta = hasta;
                R.Recortado = recortado;
                R.CicloCerrado = F.Tipo == TipoPeriodo.CicloPasado;
                R.Ciclo = F.Tipo == TipoPeriodo.CicloPasado ? F.Ciclo : F.Tipo == TipoPeriodo.Ciclo ? L.CicloEnCurso : null;
                R.Plantilla = F.Tipo == TipoPeriodo.Plantilla ? F.Plantilla : null;
                R.ConAvisos = L.ConAvisos;
                R.ConReportesDocentes = L.ConReportesDocentes;
                R.ConDadas = L.ConDadas;
                R.CicloEntero = F.EsCiclo && lab == 0;
                R.PeriodoPasado = hasta < hoy;
                R.TituloDerecho = R.CicloEntero ? "Derecho a evaluación (80 %)" : "80 % o más en el periodo";
                R.TituloDerechoCorto = R.CicloEntero ? "Derecho (80 %)" : "80 % o más";

                ElegirClasesYSesiones();
                R.ClasesDelPeriodo = delPeriodo.Count;
                ArmarTramos();
                ContarBitacorasYReportes();
                ContarCalendario();
                ContarAsistencia();
                equipos = ArmarEquipos();
                ArmarLaboratorios();
                ArmarGraficas();
                ArmarCifras();
                ArmarEstudiantes();
                ArmarClases();
                ArmarGrupos();
                ArmarDocentes();
                R.Equipos = equipos.Where(e => EnElFiltroDeLaboratorio(e.LaboratorioId)).ToList();
                ArmarOpciones();
                ArmarTextos();
                return R;
            }

            // -----------------------------------------------------------------
            // Que entra
            // -----------------------------------------------------------------

            private void ElegirClasesYSesiones()
            {
                delPeriodo = A.Clases.Where(c => AsistenciaDatos.DelPeriodo(c, desdeDeClases, F.Hasta, F.EsCiclo, F.SoloEstas)).ToList();
                clases = delPeriodo.Where(c => (F.Docente == 0 || c.ProfesorId == F.Docente) && (F.Grupo == 0 || c.GrupoId == F.Grupo)).ToList();
                idsDeClases = new HashSet<int>(clases.Select(c => c.Id));
                clasesPorTerna = new Dictionary<string, List<ClaseLeida>>();
                foreach (ClaseLeida clase in clases)
                {
                    string terna = clase.ProfesorId + "|" + clase.MateriaId + "|" + clase.GrupoId;
                    List<ClaseLeida> mismas;
                    if (!clasesPorTerna.TryGetValue(terna, out mismas))
                    {
                        mismas = new List<ClaseLeida>();
                        clasesPorTerna[terna] = mismas;
                    }

                    mismas.Add(clase);
                }

                // Las sesiones del periodo (tambien uso libre y fuera del horario). El
                // uso libre no tiene docente ni grupo: solo entra con los dos en "Todos".
                sesiones = A.SesionPorId.Values
                    .Where(s => s.Fecha >= desde && s.Fecha <= hasta
                        && (F.Docente == 0 || s.DocenteId == F.Docente)
                        && (F.Grupo == 0 || s.GrupoId == F.Grupo))
                    .OrderBy(s => s.Fecha).ThenBy(s => s.Id)
                    .ToList();
                sesionDelFiltro = sesiones.ToDictionary(s => s.Id);
            }

            /// <summary>La clase de una sesion: misma terna y dentro de su vigencia (como la asistencia), o null.</summary>
            private ClaseLeida ClaseDeLaSesion(SesionLeida sesion)
            {
                if (!sesion.DocenteId.HasValue || !sesion.MateriaId.HasValue || !sesion.GrupoId.HasValue)
                {
                    return null;
                }

                List<ClaseLeida> mismas;
                return clasesPorTerna.TryGetValue(sesion.DocenteId.Value + "|" + sesion.MateriaId.Value + "|" + sesion.GrupoId.Value, out mismas)
                    ? mismas.FirstOrDefault(c => sesion.Fecha >= c.VigenteDesde && sesion.Fecha <= c.VigenteHasta)
                    : null;
            }

            private Acumulado DelLaboratorio(int laboratorio)
            {
                Acumulado acumulado;
                if (!porLaboratorio.TryGetValue(laboratorio, out acumulado))
                {
                    acumulado = new Acumulado();
                    porLaboratorio[laboratorio] = acumulado;
                }

                return acumulado;
            }

            private static void Sumar(Dictionary<int, int> cuentas, int clave, int cuanto)
            {
                int hay;
                cuentas.TryGetValue(clave, out hay);
                cuentas[clave] = hay + cuanto;
            }

            /// <summary>C3, C4 y los avisos de C5, por laboratorio (sin su filtro) y en el filtro.</summary>
            private void ContarBitacorasYReportes()
            {
                HashSet<long> pares = new HashSet<long>();
                foreach (SesionLeida sesion in sesiones)
                {
                    List<BitacoraLeida> deLaSesion;
                    if (!A.BitacorasPorSesion.TryGetValue(sesion.Id, out deLaSesion))
                    {
                        continue;
                    }

                    int laboratorio = sesion.LaboratorioId ?? 0;
                    Acumulado acumulado = DelLaboratorio(laboratorio);
                    bool enElFiltro = EnElFiltroDeLaboratorio(laboratorio);
                    ClaseLeida clase = enElFiltro ? ClaseDeLaSesion(sesion) : null;
                    foreach (BitacoraLeida b in deLaSesion.OrderBy(x => x.Id))
                    {
                        // R5: un par (sesion, estudiante) cuenta una vez (la base ya lo impide).
                        if (!pares.Add(((long)sesion.Id << 32) | (uint)b.AlumnoId))
                        {
                            continue;
                        }

                        contadas.Add(new KeyValuePair<SesionLeida, BitacoraLeida>(sesion, b));
                        bool falla = (b.Banderas & BitacoraLeida.BanderaConFalla) != 0;
                        DetalleDeReporte detalle;
                        bool aviso = L.Detalles.TryGetValue(b.Id, out detalle) && detalle.SinInternet != null;
                        foreach (Acumulado a in new[] { acumulado, todos })
                        {
                            a.Bitacoras++;
                            a.Estudiantes.Add(b.AlumnoId);
                            if (falla)
                            {
                                a.FallasDeBitacoras++;
                                a.Hardware += (b.Banderas & BitacoraLeida.BanderaHardware) != 0 ? 1 : 0;
                                a.Software += (b.Banderas & BitacoraLeida.BanderaSoftware) != 0 ? 1 : 0;
                                a.Red += (b.Banderas & BitacoraLeida.BanderaRed) != 0 ? 1 : 0;
                            }

                            if (aviso)
                            {
                                a.Avisos++;
                            }
                        }

                        if (enElFiltro && falla && clase != null)
                        {
                            Sumar(fallasPorClase, clase.Id, 1);
                        }
                    }
                }

                // Los reportes de docentes (E8) de las sesiones del filtro, tengan o no
                // bitacoras: un reporte puede ser lo unico de su sesion.
                foreach (ReporteDeDocente reporte in L.ReportesDocentes.OrderBy(r => r.Id))
                {
                    SesionLeida sesion;
                    if (!sesionDelFiltro.TryGetValue(reporte.SesionId, out sesion))
                    {
                        continue;
                    }

                    reportes.Add(new KeyValuePair<SesionLeida, ReporteDeDocente>(sesion, reporte));
                    int laboratorio = sesion.LaboratorioId ?? reporte.LaboratorioId ?? 0;
                    foreach (Acumulado a in new[] { DelLaboratorio(laboratorio), todos })
                    {
                        a.DeDocentes++;
                        a.DocHardware += reporte.Area == "hardware" ? 1 : 0;
                        a.DocSoftware += reporte.Area == "software" ? 1 : 0;
                        a.DocRed += reporte.Area == "red" ? 1 : 0;
                    }

                    if (EnElFiltroDeLaboratorio(laboratorio))
                    {
                        ClaseLeida clase = ClaseDeLaSesion(sesion);
                        if (clase != null)
                        {
                            Sumar(fallasPorClase, clase.Id, 1);
                        }

                        Sumar(reportesPorDocente, reporte.DocenteId, 1);
                        // La misma fila que se cuenta (la ficha del docente): la lista y SusReportes no difieren.
                        R.ReportesDocentes.Add(new FilaReporteDocente
                        {
                            Id = reporte.Id,
                            SesionId = reporte.SesionId,
                            DocenteId = reporte.DocenteId,
                            Dia = sesion.Fecha,
                            RegistradoEn = reporte.RegistradoEn,
                            LaboratorioId = laboratorio,
                            Laboratorio = NombreDeLaboratorio(L, laboratorio),
                            ClaseId = clase == null ? (int?)null : clase.Id,
                            Clase = clase == null ? "Fuera del horario" : clase.Materia + " (" + clase.Grupo + ")",
                            Equipo = reporte.Equipo,
                            Area = reporte.Area,
                            Falla = reporte.Falla
                        });
                    }
                }
            }

            /// <summary>C2 como Faltas: por laboratorio (sin su filtro) y por clase (con el).</summary>
            private void ContarCalendario()
            {
                foreach (ClaseDelCalendario clase in L.Calendario.Clases)
                {
                    if (clase.Fecha < desde || clase.Fecha > hasta || !idsDeClases.Contains(clase.ClaseId))
                    {
                        continue;
                    }

                    // Las horas del bloque (o de la sesion extra), para las graficas de uso.
                    double horas = Math.Max(0.0, (clase.Fin - clase.Inicio).TotalHours);
                    DelLaboratorio(clase.LaboratorioId).Clases.Contar(clase.Estado);
                    DelLaboratorio(clase.LaboratorioId).Horas.Contar(clase.Estado, horas);
                    todos.Clases.Contar(clase.Estado);
                    todos.Horas.Contar(clase.Estado, horas);
                    if (EnElFiltroDeLaboratorio(clase.LaboratorioId))
                    {
                        ResumenDia resumen;
                        if (!calendarioPorClase.TryGetValue(clase.ClaseId, out resumen))
                        {
                            resumen = new ResumenDia();
                            calendarioPorClase[clase.ClaseId] = resumen;
                        }

                        resumen.Contar(clase.Estado);
                        Agregar(entradasDeLaClase, clase.ClaseId, clase);
                        SemanaDe(clase.Fecha).Contar(clase.Estado, horas);
                        MesDe(clase.Fecha).Contar(clase.Estado, horas);
                    }

                    LaboratoriosDe(clase.ClaseId).Add(clase.LaboratorioId);
                }

                // Los laboratorios de cada clase: los de sus franjas (E3) y de sus extras del periodo (E4).
                foreach (ClaseLeida clase in clases)
                {
                    HashSet<int> suyos = LaboratoriosDe(clase.Id);
                    List<FranjaLeida> franjas;
                    if (A.FranjasPorDocente.TryGetValue(clase.ProfesorId, out franjas))
                    {
                        suyos.UnionWith(franjas.Where(f => f.ClaseId == clase.Id).Select(f => f.LaboratorioId));
                    }

                    List<ExcepcionLeida> excepciones;
                    if (A.ExcepcionesPorDocente.TryGetValue(clase.ProfesorId, out excepciones))
                    {
                        suyos.UnionWith(excepciones
                            .Where(x => x.ClaseId == clase.Id && !x.EsCancelada && x.LaboratorioId.HasValue && x.Fecha >= desde && x.Fecha <= hasta)
                            .Select(x => x.LaboratorioId.Value));
                    }
                }
            }

            private HashSet<int> LaboratoriosDe(int clase)
            {
                HashSet<int> suyos;
                if (!laboratoriosDeLaClase.TryGetValue(clase, out suyos))
                {
                    suyos = new HashSet<int>();
                    laboratoriosDeLaClase[clase] = suyos;
                }

                return suyos;
            }

            /// <summary>
            /// C1 y las tablas de asistencia: cada lista de Mis grupos (CalcularTodas)
            /// contada solo en los dias del periodo y del laboratorio (ResumenAsistencia.De).
            /// </summary>
            private void ContarAsistencia()
            {
                Func<DiaDeAsistencia, bool> cuenta = d => d.Fecha >= desde && d.Fecha <= hasta && (lab == 0 || d.Laboratorios.Contains(lab));
                foreach (ClaseLeida clase in clases)
                {
                    AsistenciaDeLaClase deLaClase = new AsistenciaDeLaClase { Clase = clase };
                    asistencia[clase.Id] = deLaClase;
                    AsistenciaDeClase lista;
                    if (!L.Listas.TryGetValue(clase.Id, out lista))
                    {
                        continue;
                    }

                    ResumenAsistencia resumen = ResumenAsistencia.De(lista, cuenta);
                    ContarAsistenciaPorTramo(lista, resumen);
                    deLaClase.Estudiantes = lista.Alumnos.Count;
                    deLaClase.DiasQueCuentan = resumen.PorDia.Count(d => !d.Dia.EnCurso);
                    deLaClase.AlgunDiaEnElLaboratorio = lab != 0 && lista.Dias.Any(d => d.Fecha >= desde && d.Fecha <= hasta && d.Laboratorios.Contains(lab));
                    DateTime? primerDia = resumen.PorDia.Count == 0 ? (DateTime?)null : resumen.PorDia.Min(d => d.Dia.Fecha);
                    foreach (AlumnoResumido alumno in resumen.Alumnos)
                    {
                        EstudianteLeido estudiante;
                        A.Estudiantes.TryGetValue(alumno.Alumno.Id, out estudiante);
                        DateTime entro = alumno.Alumno.Entro;
                        ParDeAsistencia par = new ParDeAsistencia
                        {
                            ClaseId = clase.Id,
                            AlumnoId = alumno.Alumno.Id,
                            Matricula = alumno.Alumno.Matricula,
                            Nombre = alumno.Alumno.Nombre,
                            Activo = estudiante == null || estudiante.Activo,
                            Materia = clase.Materia,
                            Grupo = clase.Grupo,
                            Docente = clase.Docente,
                            Entro = alumno.Asistencias,
                            Veces = alumno.Asistencias + alumno.Faltas,
                            Retiradas = alumno.Retiradas,
                            Dadas = alumno.Dadas,
                            EntroALaLista = primerDia.HasValue && entro != DateTime.MaxValue && entro > primerDia.Value ? entro : (DateTime?)null
                        };
                        deLaClase.Pares.Add(par);
                        if (par.Veces == 0)
                        {
                            continue;
                        }

                        deLaClase.X += par.Entro;
                        deLaClase.N += par.Veces;
                        deLaClase.Dadas += par.Dadas;
                        deLaClase.Bajo += par.Bajo ? 1 : 0;
                    }
                }
            }

            /// <summary>
            /// Con un laboratorio elegido, la clase es de el: sus franjas o extras estan
            /// ahi, o algo del periodo paso ahi (clases del calendario, dias de su lista).
            /// Sin laboratorio, todas.
            /// </summary>
            private bool EsDelLaboratorio(ClaseLeida clase)
            {
                if (lab == 0)
                {
                    return true;
                }

                HashSet<int> suyos;
                AsistenciaDeLaClase deLaClase;
                return (laboratoriosDeLaClase.TryGetValue(clase.Id, out suyos) && suyos.Contains(lab))
                    || (asistencia.TryGetValue(clase.Id, out deLaClase) && deLaClase.AlgunDiaEnElLaboratorio);
            }

            // -----------------------------------------------------------------
            // Equipos (sec. 3.6)
            // -----------------------------------------------------------------

            private sealed class Equipo
            {
                public FilaEquipo Fila;
                public readonly HashSet<int> SesionesConFalla = new HashSet<int>();
                public readonly HashSet<int> SesionesUsado = new HashSet<int>();
                public readonly HashSet<DateTime> Dias = new HashSet<DateTime>();
                public readonly Dictionary<string, int> Escrituras = new Dictionary<string, int>(StringComparer.Ordinal);
                public readonly Dictionary<DateTime, int> AvisosPorDia = new Dictionary<DateTime, int>();
                public int BitacorasRegistradas;
                // El ultimo reporte: dia, hora, prioridad (0 bitacora, 1 docente, 2 aviso) y lo que dijo.
                public DateTime? UltimoDia;
                public TimeSpan? UltimaHora;
                public int UltimaPrioridad = int.MaxValue;
                public string UltimoTexto = "";
                public string UltimaEscritura;
                public bool UltimoEsAviso;

                public void Reporte(DateTime dia, DateTime? cuando, int prioridad, string texto, string escritura, bool esAviso)
                {
                    TimeSpan? hora = cuando.HasValue ? cuando.Value.TimeOfDay : (TimeSpan?)null;
                    Dias.Add(dia);
                    // Fecha y luego hora; sin hora va antes. A igual momento, la bitacora gana.
                    int orden = !UltimoDia.HasValue ? 1 : dia.CompareTo(UltimoDia.Value);
                    if (orden == 0)
                    {
                        orden = Nullable.Compare(hora, UltimaHora);
                    }

                    if (orden == 0)
                    {
                        orden = UltimaPrioridad.CompareTo(prioridad);
                    }

                    if (orden > 0)
                    {
                        UltimoDia = dia;
                        UltimaHora = hora;
                        UltimaPrioridad = prioridad;
                        UltimoTexto = texto;
                        UltimaEscritura = escritura;
                        UltimoEsAviso = esAviso;
                    }
                }
            }

            /// <summary>
            /// La clave de fila de cada nombre de equipo de cada laboratorio, con TODOS los
            /// nombres leidos (bitacoras de E6, avisos de E10 y reportes de E8): asi no
            /// depende del orden ni del filtro. Un nombre de Windows va solo, en
            /// mayusculas; los demas, con Consultas.ClaveDeEquipo; una clave sin prefijo
            /// ("|7", de "7" o "07") se junta con la unica del laboratorio con prefijo sin
            /// cifras y el mismo numero ("PC|7"); con dos o ninguna, queda sola.
            /// </summary>
            private Dictionary<int, Dictionary<string, string>> ClavesDeFila()
            {
                Dictionary<int, HashSet<string>> nombres = new Dictionary<int, HashSet<string>>();
                Action<int, string> anotar = (laboratorio, nombre) =>
                {
                    nombre = (nombre ?? "").Trim();
                    if (nombre.Length == 0)
                    {
                        return;
                    }

                    HashSet<string> delLaboratorio;
                    if (!nombres.TryGetValue(laboratorio, out delLaboratorio))
                    {
                        delLaboratorio = new HashSet<string>(StringComparer.Ordinal);
                        nombres[laboratorio] = delLaboratorio;
                    }

                    delLaboratorio.Add(nombre);
                };

                // Solo lo de [desdeLeido, hastaLeido], lo que tendria una lectura de este
                // periodo: la de la cache puede traer mas dias, y un "LAB-7" de antes (o de
                // despues) cambiaria a que fila va "7" segun el camino por el que se llego.
                foreach (KeyValuePair<int, List<BitacoraLeida>> sesion in A.BitacorasPorSesion)
                {
                    SesionLeida leida;
                    if (!A.SesionPorId.TryGetValue(sesion.Key, out leida) || leida.Fecha < desdeLeido || leida.Fecha > hastaLeido)
                    {
                        continue;
                    }

                    int laboratorio = leida.LaboratorioId ?? 0;
                    foreach (BitacoraLeida b in sesion.Value)
                    {
                        anotar(laboratorio, b.Equipo);
                    }
                }

                foreach (DetalleDeReporte detalle in L.Detalles.Values.Where(d => d.SinInternet != null))
                {
                    SesionLeida leida;
                    if (!A.SesionPorId.TryGetValue(detalle.SesionId, out leida) || leida.Fecha < desdeLeido || leida.Fecha > hastaLeido)
                    {
                        continue;
                    }

                    int laboratorio = leida.LaboratorioId ?? 0;
                    foreach (string nombre in detalle.SinInternet.Split(','))
                    {
                        anotar(laboratorio, nombre);
                    }
                }

                foreach (ReporteDeDocente reporte in L.ReportesDocentes.Where(r => r.Equipo != null && r.Dia >= desdeLeido && r.Dia <= hastaLeido))
                {
                    SesionLeida leida;
                    int laboratorio = A.SesionPorId.TryGetValue(reporte.SesionId, out leida) && leida.LaboratorioId.HasValue ? leida.LaboratorioId.Value : reporte.LaboratorioId ?? 0;
                    anotar(laboratorio, reporte.Equipo);
                }

                Dictionary<int, Dictionary<string, string>> claves = new Dictionary<int, Dictionary<string, string>>();
                foreach (KeyValuePair<int, HashSet<string>> delLaboratorio in nombres)
                {
                    Dictionary<string, string> base_ = delLaboratorio.Value.ToDictionary(n => n, ClaveBase, StringComparer.Ordinal);
                    HashSet<string> distintas = new HashSet<string>(base_.Values, StringComparer.Ordinal);
                    Dictionary<string, string> final = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, string> nombre in base_)
                    {
                        string clave = nombre.Value;
                        if (clave.StartsWith("|", StringComparison.Ordinal))
                        {
                            List<string> conPrefijo = distintas.Where(c => !c.StartsWith("W:", StringComparison.Ordinal) && c.EndsWith(clave, StringComparison.Ordinal)
                                && c.Length > clave.Length && c.IndexOf('|') == c.Length - clave.Length && !c.Substring(0, c.Length - clave.Length).Any(char.IsDigit)).ToList();
                            if (conPrefijo.Count == 1)
                            {
                                clave = conPrefijo[0];
                            }
                        }

                        final[nombre.Key] = clave;
                    }

                    claves[delLaboratorio.Key] = final;
                }

                return claves;
            }

            /// <summary>La clave de un nombre antes de juntar los numeros solos: el de Windows en mayusculas ("W:DESKTOP-AB12"), si no ClaveDeEquipo.</summary>
            private static string ClaveBase(string nombre)
            {
                return EsDeWindows(nombre) ? "W:" + nombre.Trim().ToUpperInvariant() : Consultas.ClaveDeEquipo(nombre);
            }

            /// <summary>Las filas de Equipos de todos los laboratorios (con docente y grupo, sin el filtro de laboratorio).</summary>
            private List<FilaEquipo> ArmarEquipos()
            {
                Dictionary<int, Dictionary<string, string>> claves = ClavesDeFila();
                Dictionary<string, Equipo> porClave = new Dictionary<string, Equipo>(StringComparer.Ordinal);
                Func<int, string, TipoFilaEquipo, Equipo> fila = (laboratorio, clave, tipo) =>
                {
                    Equipo equipo;
                    string llave = laboratorio.ToString(Inv) + "\u0001" + clave;
                    if (!porClave.TryGetValue(llave, out equipo))
                    {
                        equipo = new Equipo
                        {
                            Fila = new FilaEquipo
                            {
                                LaboratorioId = laboratorio,
                                Laboratorio = NombreDeLaboratorio(L, laboratorio),
                                Tipo = tipo,
                                Equipo = tipo == TipoFilaEquipo.TodoElLaboratorio ? "Todo el laboratorio" : tipo == TipoFilaEquipo.SinEquipo ? "Sin equipo" : "",
                                EsNombreDeWindows = clave.StartsWith("W:", StringComparison.Ordinal)
                            }
                        };
                        porClave[llave] = equipo;
                    }

                    return equipo;
                };
                Func<int, string, string> claveDe = (laboratorio, nombre) =>
                {
                    nombre = (nombre ?? "").Trim();
                    Dictionary<string, string> delLaboratorio;
                    string clave;
                    return claves.TryGetValue(laboratorio, out delLaboratorio) && delLaboratorio.TryGetValue(nombre, out clave) ? clave : ClaveBase(nombre);
                };

                // Las bitacoras: uso del equipo, fallas, avisos que hizo su estudiante.
                foreach (KeyValuePair<SesionLeida, BitacoraLeida> par in contadas)
                {
                    SesionLeida sesion = par.Key;
                    BitacoraLeida b = par.Value;
                    int laboratorio = sesion.LaboratorioId ?? 0;
                    string nombre = (b.Equipo ?? "").Trim();
                    Equipo equipo = nombre.Length == 0
                        ? fila(laboratorio, ClaveSinEquipo, TipoFilaEquipo.SinEquipo)
                        : fila(laboratorio, claveDe(laboratorio, nombre), TipoFilaEquipo.Equipo);
                    bool falla = (b.Banderas & BitacoraLeida.BanderaConFalla) != 0;
                    bool propio = (b.Banderas & BitacoraLeida.BanderaEquipoPropio) != 0;
                    equipo.BitacorasRegistradas++;
                    equipo.Fila.Registro = true;
                    if (nombre.Length > 0)
                    {
                        int veces;
                        equipo.Escrituras.TryGetValue(nombre, out veces);
                        equipo.Escrituras[nombre] = veces + 1;
                    }

                    if (!propio || falla)
                    {
                        equipo.SesionesUsado.Add(sesion.Id);
                    }

                    DetalleDeReporte detalle;
                    L.Detalles.TryGetValue(b.Id, out detalle);
                    if (falla)
                    {
                        equipo.SesionesConFalla.Add(sesion.Id);
                        equipo.Fila.Fallas++;
                        equipo.Fila.Hardware += (b.Banderas & BitacoraLeida.BanderaHardware) != 0 ? 1 : 0;
                        equipo.Fila.Software += (b.Banderas & BitacoraLeida.BanderaSoftware) != 0 ? 1 : 0;
                        equipo.Fila.Red += (b.Banderas & BitacoraLeida.BanderaRed) != 0 ? 1 : 0;
                        equipo.Reporte(sesion.Fecha, detalle != null ? detalle.Cuando : b.RegistradoEn, 0,
                            detalle != null && detalle.QueReporto != null ? detalle.QueReporto : "Falla reportada", nombre, false);
                    }

                    // Sus avisos: cada equipo que nombra, una vez por bitacora.
                    if (detalle != null && detalle.SinInternet != null)
                    {
                        HashSet<string> enEsta = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string avisado in detalle.SinInternet.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
                        {
                            string clave = claveDe(laboratorio, avisado);
                            if (!enEsta.Add(clave))
                            {
                                continue;
                            }

                            Equipo deAviso = fila(laboratorio, clave, TipoFilaEquipo.Equipo);
                            deAviso.Fila.Avisos++;
                            int ese;
                            deAviso.AvisosPorDia.TryGetValue(sesion.Fecha, out ese);
                            deAviso.AvisosPorDia[sesion.Fecha] = ese + 1;
                            deAviso.Reporte(sesion.Fecha, detalle.Cuando, 2, "", avisado, true);
                        }
                    }
                }

                // Los reportes de docentes: el equipo que nombran o todo el laboratorio.
                foreach (KeyValuePair<SesionLeida, ReporteDeDocente> par in reportes)
                {
                    ReporteDeDocente reporte = par.Value;
                    int laboratorio = par.Key.LaboratorioId ?? reporte.LaboratorioId ?? 0;
                    Equipo equipo = reporte.Equipo == null
                        ? fila(laboratorio, ClaveTodoElLaboratorio, TipoFilaEquipo.TodoElLaboratorio)
                        : fila(laboratorio, claveDe(laboratorio, reporte.Equipo), TipoFilaEquipo.Equipo);
                    equipo.Fila.Fallas++;
                    equipo.Fila.DeDocentes++;
                    equipo.Fila.Hardware += reporte.Area == "hardware" ? 1 : 0;
                    equipo.Fila.Software += reporte.Area == "software" ? 1 : 0;
                    equipo.Fila.Red += reporte.Area == "red" ? 1 : 0;
                    string dijo = reporte.Area == "otro" || reporte.Area.Length == 0
                        ? "Docente: " + reporte.Falla
                        : "Docente, " + ReportesProfesorDatos.NombreDeArea(reporte.Area).ToLowerInvariant() + ": " + reporte.Falla;
                    equipo.Reporte(par.Key.Fecha, reporte.RegistradoEn, 1, dijo, reporte.Equipo, false);
                }

                List<FilaEquipo> filas = new List<FilaEquipo>();
                foreach (Equipo equipo in porClave.Values)
                {
                    FilaEquipo f = equipo.Fila;
                    f.SesionesConFalla = equipo.SesionesConFalla.Count;
                    f.SesionesUsado = equipo.SesionesUsado.Count;
                    f.DiasConReporte = equipo.Dias.Count;
                    f.UltimoDia = equipo.UltimoDia;
                    f.UltimaHora = equipo.UltimaHora;
                    f.UltimoReporte = equipo.UltimoDia.HasValue ? DiaYHora(equipo.UltimoDia.Value, equipo.UltimaHora) : "";
                    if (equipo.UltimoEsAviso)
                    {
                        int ese;
                        equipo.AvisosPorDia.TryGetValue(equipo.UltimoDia.Value, out ese);
                        f.LoUltimo = ese == 1 ? "Sin internet (lo avisó 1 estudiante)" : "Sin internet (lo avisaron " + ese + " estudiantes)";
                    }
                    else
                    {
                        f.LoUltimo = equipo.UltimoTexto;
                    }

                    if (f.Tipo == TipoFilaEquipo.Equipo)
                    {
                        // La escritura mas usada en sus bitacoras; si no registro, la del ultimo aviso o reporte.
                        f.Equipo = equipo.Escrituras.Count > 0
                            ? equipo.Escrituras.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).First().Key
                            : equipo.UltimaEscritura ?? "";
                    }

                    if (f.Fallas > 0 || f.Avisos > 0)
                    {
                        filas.Add(f);
                    }

                    if (f.Tipo == TipoFilaEquipo.Equipo && f.Avisos > 0)
                    {
                        DelLaboratorio(f.LaboratorioId).EquiposConAvisos++;
                        todos.EquiposConAvisos++;
                    }
                }

                // Mas fallas y avisos primero; despues mas dias con reporte y el ultimo reporte mas reciente.
                List<FilaEquipo> ordenadas = filas
                    .OrderByDescending(e => e.Fallas + e.Avisos)
                    .ThenByDescending(e => e.DiasConReporte)
                    .ThenByDescending(e => e.UltimoDia ?? DateTime.MinValue)
                    .ThenByDescending(e => e.UltimaHora ?? TimeSpan.MinValue)
                    .ThenBy(e => e.Laboratorio, PorNombre)
                    .ThenBy(e => e.Equipo, PorNombre)
                    .ToList();

                // Para la linea de arriba y la nota de Windows (con el filtro de laboratorio).
                List<Equipo> delFiltro = porClave.Values.Where(e => EnElFiltroDeLaboratorio(e.Fila.LaboratorioId)).ToList();
                int registraron = delFiltro.Count(e => e.Fila.Tipo == TipoFilaEquipo.Equipo && e.BitacorasRegistradas > 0);
                List<FilaEquipo> conReporte = ordenadas.Where(e => EnElFiltroDeLaboratorio(e.LaboratorioId) && e.Tipo == TipoFilaEquipo.Equipo).ToList();
                int repetidos = conReporte.Count(e => e.Repetido);
                R.LineaEquipos = Contar(conReporte.Count, "equipo", "equipos") + " con fallas o avisos (" + Miles(registraron)
                    + (registraron == 1 ? " registró" : " registraron") + " en el periodo)"
                    + (repetidos > 0 ? "; " + Miles(repetidos) + " con reportes en 2 días o más (en naranja)." : ".");
                List<Equipo> deWindows = delFiltro.Where(e => e.Fila.EsNombreDeWindows && e.BitacorasRegistradas > 0).ToList();
                if (deWindows.Count > 0)
                {
                    int bitacoras = deWindows.Sum(e => e.BitacorasRegistradas);
                    R.Notas.Add(new NotaEstadisticas
                    {
                        Prioridad = 3,
                        SoloEnEquipos = true,
                        Texto = (deWindows.Count == 1 ? "1 equipo sin número (DESKTOP-...) registró " : Miles(deWindows.Count) + " equipos sin número (DESKTOP-...) registraron ")
                            + Contar(bitacoras, "bitácora", "bitácoras")
                            + (deWindows.Count == 1 ? ": ponle su «Número de máquina» al instalar DigBit." : ": ponles su «Número de máquina» al instalar DigBit.")
                    });
                }

                return ordenadas;
            }

            // -----------------------------------------------------------------
            // Laboratorios (sec. 3.5) y cifras (sec. 3.1)
            // -----------------------------------------------------------------

            private FilaLaboratorio FilaDe(Acumulado a, int id, string nombre, bool esTodos)
            {
                return new FilaLaboratorio
                {
                    LaboratorioId = id,
                    EsTodos = esTodos,
                    Laboratorio = nombre,
                    ConBitacoras = a.Clases.ConBitacoras,
                    QueContaban = a.Clases.ConBitacoras + a.Clases.Faltan + a.Clases.SinBitacorasConAsistencia,
                    SinBitacoras = a.Clases.Faltan,
                    ConDadas = a.Clases.SinBitacorasConAsistencia,
                    Bitacoras = a.Bitacoras,
                    Estudiantes = a.Estudiantes.Count,
                    Fallas = a.Fallas,
                    Hardware = a.Hardware + a.DocHardware,
                    Software = a.Software + a.DocSoftware,
                    Red = a.Red + a.DocRed,
                    DeDocentes = a.DeDocentes,
                    EquiposSinInternet = L.ConAvisos ? a.EquiposConAvisos : (int?)null,
                    AvisosSinInternet = a.Avisos
                };
            }

            private void ArmarLaboratorios()
            {
                List<FilaLaboratorio> filas = new List<FilaLaboratorio>();
                IEnumerable<int> ids = L.Laboratorios.Keys.Union(porLaboratorio.Keys);
                foreach (int id in ids)
                {
                    Acumulado a;
                    porLaboratorio.TryGetValue(id, out a);
                    a = a ?? new Acumulado();
                    LaboratorioLeido leido;
                    bool conocido = L.Laboratorios.TryGetValue(id, out leido);
                    bool activo = conocido && leido.Activo;
                    if (!activo && !a.AlgoQueSumar)
                    {
                        continue;
                    }

                    FilaLaboratorio fila = FilaDe(a, id, NombreDeLaboratorio(L, id) + (conocido && !leido.Activo ? " (desactivado)" : ""), false);
                    fila.Activo = activo;
                    fila.RegistraDesde = conocido ? leido.EmpezoEl : null;
                    fila.RegistraDesdeTexto = fila.RegistraDesde.HasValue ? Fecha(fila.RegistraDesde.Value) : a.Bitacoras > 0 ? "Solo uso libre" : "Aún no";
                    fila.AunNo = !fila.RegistraDesde.HasValue && a.Bitacoras == 0;
                    filas.Add(fila);
                }

                // RF142: de mas a menos fallas; "Todos" siempre arriba.
                R.Laboratorios = new List<FilaLaboratorio> { FilaDe(todos, 0, "Todos los laboratorios", true) };
                R.Laboratorios[0].RegistraDesde = L.Laboratorios.Values.Where(l => l.EmpezoEl.HasValue).Select(l => l.EmpezoEl).DefaultIfEmpty(null).Min();
                R.Laboratorios[0].RegistraDesdeTexto = R.Laboratorios[0].RegistraDesde.HasValue ? Fecha(R.Laboratorios[0].RegistraDesde.Value) : todos.Bitacoras > 0 ? "Solo uso libre" : "Aún no";
                R.Laboratorios.AddRange(filas.OrderByDescending(f => f.Fallas).ThenBy(f => f.Laboratorio, PorNombre));
            }

            // -----------------------------------------------------------------
            // Graficas: semanas, meses y horas de cada laboratorio
            // -----------------------------------------------------------------

            private static readonly string[] MesesCortos = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };
            private static readonly string[] MesesLargos = { "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };

            /// <summary>Las semanas (de lunes a domingo) y los meses de [desde, hasta], recortados al periodo.</summary>
            private void ArmarTramos()
            {
                lunesDelPrimero = desde.AddDays(-(((int)desde.DayOfWeek + 6) % 7));
                for (DateTime lunes = lunesDelPrimero; lunes <= hasta; lunes = lunes.AddDays(7))
                {
                    DateTime primero = lunes < desde ? desde : lunes;
                    DateTime ultimo = lunes.AddDays(6) > hasta ? hasta : lunes.AddDays(6);
                    semanas.Add(new TramoDelPeriodo
                    {
                        Desde = primero,
                        Hasta = ultimo,
                        Etiqueta = primero.ToString("dd/MM", Inv),
                        EtiquetaLarga = DelAlCorto(primero, ultimo)
                    });
                }

                for (DateTime uno = new DateTime(desde.Year, desde.Month, 1); uno <= hasta; uno = uno.AddMonths(1))
                {
                    DateTime primero = uno < desde ? desde : uno;
                    DateTime ultimo = uno.AddMonths(1).AddDays(-1) > hasta ? hasta : uno.AddMonths(1).AddDays(-1);
                    bool entero = primero == uno && ultimo == uno.AddMonths(1).AddDays(-1);
                    meses.Add(new TramoDelPeriodo
                    {
                        Desde = primero,
                        Hasta = ultimo,
                        Etiqueta = MesesCortos[uno.Month - 1] + " " + uno.Year.ToString(Inv),
                        EtiquetaLarga = MesesLargos[uno.Month - 1] + " de " + uno.Year.ToString(Inv)
                            + (entero ? "" : " (del " + primero.ToString("dd/MM", Inv) + " al " + ultimo.ToString("dd/MM", Inv) + ")")
                    });
                }
            }

            /// <summary>"Del 05/10 al 11/10/2026" (con el ano de los dos si cambia).</summary>
            private static string DelAlCorto(DateTime primero, DateTime ultimo)
            {
                return primero.Year == ultimo.Year
                    ? "Del " + primero.ToString("dd/MM", Inv) + " al " + ultimo.ToString("dd/MM/yyyy", Inv)
                    : DelAl(primero, ultimo);
            }

            /// <summary>La semana de un dia del periodo (los de fuera van a la primera o a la ultima: no pasa, las cuentas ya los filtran).</summary>
            private TramoDelPeriodo SemanaDe(DateTime dia)
            {
                if (semanas.Count == 0)
                {
                    return new TramoDelPeriodo();
                }

                int indice = (int)Math.Floor((dia.Date - lunesDelPrimero).TotalDays / 7.0);
                return semanas[Math.Max(0, Math.Min(semanas.Count - 1, indice))];
            }

            private TramoDelPeriodo MesDe(DateTime dia)
            {
                if (meses.Count == 0)
                {
                    return new TramoDelPeriodo();
                }

                int indice = (dia.Year - desde.Year) * 12 + dia.Month - desde.Month;
                return meses[Math.Max(0, Math.Min(meses.Count - 1, indice))];
            }

            /// <summary>
            /// La asistencia de una lista en cada semana y cada mes, dia por dia, con la
            /// regla de ResumenAsistencia.De para cada estudiante: cuenta la A (tambien la
            /// de hoy), cuenta la falta y lo pendiente de hoy (la clase sigue y aun no
            /// registra) no cuenta. Sumado da el X de N de la clase, y todas, el de C1.
            /// </summary>
            private void ContarAsistenciaPorTramo(AsistenciaDeClase lista, ResumenAsistencia resumen)
            {
                foreach (AsistenciaDelDia delDia in resumen.PorDia)
                {
                    DateTime fecha = delDia.Dia.Fecha;
                    int entraron = 0;
                    int veces = 0;
                    foreach (AlumnoDeAsistencia alumno in lista.Alumnos)
                    {
                        bool vino = alumno.Presente.Contains(fecha);
                        if (delDia.Dia.EnCurso && !vino && !alumno.Retiradas.Contains(fecha))
                        {
                            continue;
                        }

                        veces++;
                        entraron += vino ? 1 : 0;
                    }

                    foreach (TramoDelPeriodo tramo in new[] { SemanaDe(fecha), MesDe(fecha) })
                    {
                        tramo.Entraron += entraron;
                        tramo.Veces += veces;
                    }
                }
            }

            private static UsoDeLaboratorio UsoDe(Acumulado a, int id, string nombre)
            {
                return new UsoDeLaboratorio
                {
                    LaboratorioId = id,
                    Laboratorio = nombre,
                    ConBitacoras = a.Clases.ConBitacoras,
                    ConDadas = a.Clases.SinBitacorasConAsistencia,
                    SinRegistro = a.Clases.Faltan,
                    Canceladas = a.Clases.Canceladas,
                    HorasDelHorario = a.Horas.HorasDelHorario,
                    HorasUsadas = a.Horas.HorasUsadas,
                    HorasCanceladas = a.Horas.HorasCanceladas
                };
            }

            /// <summary>
            /// Lo de la pestana Graficas: las semanas y los meses (con el filtro de
            /// laboratorio) y el uso del horario de cada laboratorio, del mismo acumulado
            /// que la pestana Laboratorios (con el filtro, solo el elegido).
            /// </summary>
            private void ArmarGraficas()
            {
                GraficasEstadisticas g = R.Graficas;
                g.PorSemana = semanas;
                g.PorMes = meses;
                g.Sugerida = semanas.Count > 16 ? AgrupacionGraficas.Mes : AgrupacionGraficas.Semana;
                Acumulado total;
                if (lab == 0)
                {
                    total = todos;
                }
                else if (!porLaboratorio.TryGetValue(lab, out total))
                {
                    total = new Acumulado();
                }

                g.Total = UsoDe(total, lab, lab == 0 ? "Todos los laboratorios" : NombreDeLaboratorio(L, lab));
                foreach (int id in L.Laboratorios.Keys.Union(porLaboratorio.Keys).Where(i => lab == 0 || i == lab))
                {
                    Acumulado a;
                    porLaboratorio.TryGetValue(id, out a);
                    a = a ?? new Acumulado();
                    LaboratorioLeido leido;
                    bool conocido = L.Laboratorios.TryGetValue(id, out leido);
                    if (conocido && leido.Activo && !leido.EmpezoEl.HasValue)
                    {
                        g.SinDigBit.Add(leido.Nombre);
                    }

                    if (a.Horas.QueContaban == 0 && a.Horas.Canceladas == 0)
                    {
                        continue;
                    }

                    UsoDeLaboratorio uso = UsoDe(a, id, NombreDeLaboratorio(L, id) + (conocido && !leido.Activo ? " (desactivado)" : ""));
                    uso.AunNo = conocido && !leido.EmpezoEl.HasValue;
                    g.Laboratorios.Add(uso);
                }

                g.Laboratorios = g.Laboratorios.OrderByDescending(u => u.HorasDelHorario).ThenBy(u => u.Laboratorio, PorNombre).ToList();
                g.SinDigBit = g.SinDigBit.OrderBy(n => n, PorNombre).ToList();
            }

            private void ArmarCifras()
            {
                CifrasEstadisticas c = R.Cifras;
                Acumulado a = lab == 0 ? todos : porLaboratorio.ContainsKey(lab) ? porLaboratorio[lab] : new Acumulado();

                // C1: asistencia (pares con N > 0 de las clases que entran).
                HashSet<int> bajo = new HashSet<int>();
                foreach (AsistenciaDeLaClase deLaClase in asistencia.Values)
                {
                    c.Entraron += deLaClase.X;
                    c.Veces += deLaClase.N;
                    c.Dadas += deLaClase.Dadas;
                    bajo.UnionWith(deLaClase.Pares.Where(p => p.Bajo).Select(p => p.AlumnoId));
                }

                c.EstudiantesBajo = bajo.Count;
                CifraEstadistica c1 = c.Asistencia;
                c1.Etiqueta = "asistencia de los estudiantes";
                c1.Pestana = PestanaEstadisticas.Estudiantes;
                string comoSeCuenta = "Se cuenta como en Mis grupos del docente: la lista de cada clase se arma con quienes registran su bitácora, "
                    + "quien llega después entra con falta en las clases anteriores y cuentan los días de clase con al menos una bitácora"
                    + (L.ConDadas ? " o una asistencia que dio su docente" : "") + ". "
                    + "Solo aparecen quienes han registrado al menos una vez: DigBit no tiene la lista oficial de cada grupo.";
                if (c.Veces == 0)
                {
                    c1.Valor = SinDato;
                    c1.Tono = TonoCifra.Gris;
                    c1.Ayuda = "Aún no hay días de clase que contar en este periodo" + (lab != 0 ? " y este laboratorio" : "") + ". " + comoSeCuenta;
                }
                else
                {
                    double porcentaje = 100.0 * c.Entraron / c.Veces;
                    c1.Valor = Porcentaje(porcentaje);
                    c1.Tono = ResumenAsistencia.TieneDerecho(porcentaje) ? TonoCifra.Verde : TonoCifra.Rojo;
                    string enElPeriodo = R.CicloEntero ? "" : " en el periodo";
                    // Que quepa a 1366 (unos 238 px): sin el ciclo entero, sin la palabra "estudiantes".
                    c1.Detalle = c.EstudiantesBajo == 0 ? "nadie bajo el 80 %" + enElPeriodo
                        : R.CicloEntero ? Contar(c.EstudiantesBajo, "estudiante", "estudiantes") + " bajo el 80 %"
                        : Miles(c.EstudiantesBajo) + " bajo el 80 % en el periodo";
                    c1.Ayuda = "Entraron " + Miles(c.Entraron) + " de " + Miles(c.Veces) + " veces a sus clases"
                        + (c.EstudiantesBajo > 0 ? "; " + Contar(c.EstudiantesBajo, "estudiante", "estudiantes") + " bajo el 80 % en alguna clase" + enElPeriodo : "") + ". "
                        + "Solo cuentan los días de clase de este periodo" + (lab != 0 ? " en este laboratorio" : "") + ". " + comoSeCuenta
                        + (c.Dadas > 0 ? " Incluye " + (c.Dadas == 1 ? "1 asistencia que dio un docente" : Miles(c.Dadas) + " asistencias que dieron los docentes")
                            + " a quien no pudo registrar su bitácora." : "")
                        + (R.CicloCerrado ? " Quien entró a la lista después del primer día de su clase cuenta con falta en las clases de antes, como en Mis grupos."
                            : R.PeriodoPasado ? " Quien entró a la lista después del periodo cuenta con falta en sus días, como en Mis grupos: "
                            + "por eso estas cifras pueden bajar cuando alguien nuevo registra." : "");
                }

                // C2: clases con bitacoras, como Faltas.
                c.ClasesConBitacorasX = a.Clases.ConBitacoras;
                c.ClasesSinBitacoras = a.Clases.Faltan;
                c.ClasesConDadas = a.Clases.SinBitacorasConAsistencia;
                c.ClasesQueContaban = c.ClasesConBitacorasX + c.ClasesSinBitacoras + c.ClasesConDadas;
                c.Canceladas = a.Clases.Canceladas;
                c.PorVenir = a.Clases.PorVenir;
                c.NoContaban = a.Clases.NoCuentan;
                CifraEstadistica c2 = c.ClasesConBitacoras;
                c2.Etiqueta = "clases con bitácoras";
                c2.Pestana = PestanaEstadisticas.Docentes;
                string sinBitacoras = c.ClasesSinBitacoras > 0 ? Miles(c.ClasesSinBitacoras) + " sin bitácoras" : null;
                List<string> otras = new List<string>();
                if (c.Canceladas > 0)
                {
                    otras.Add(Contar(c.Canceladas, "cancelada", "canceladas"));
                }

                if (c.PorVenir > 0)
                {
                    otras.Add(Miles(c.PorVenir) + " por venir (la clase en curso cuenta al terminar)");
                }

                if (c.NoContaban > 0)
                {
                    otras.Add(Miles(c.NoContaban) + " que no contaban");
                }

                string reglas = "Cada bloque cuenta aparte (dos bloques el mismo día son dos clases), igual que cada sesión extra; "
                    + "una clase recortada por una excepción cuenta como cancelada y lo que le quedó, como clase aparte. "
                    + "No cuentan las canceladas, la clase en curso (cuenta al terminar) ni las que no contaban: sin bitácoras en un laboratorio "
                    + "que aún no registraba sus clases con DigBit, con su docente de baja ese día o una sesión extra puesta después de su hora. "
                    + "Una clase sin bitácoras puede ser que no se dio o que nadie registró.";
                if (c.ClasesQueContaban == 0)
                {
                    c2.Valor = SinDato;
                    c2.Tono = TonoCifra.Gris;
                    c2.Ayuda = "Aún no hay clases del horario que ya terminaron y contaban en este periodo"
                        + (otras.Count > 0 ? " (" + Unir(otras) + ")" : "") + ". " + reglas;
                }
                else
                {
                    c2.Valor = Miles(c.ClasesConBitacorasX) + " de " + Miles(c.ClasesQueContaban);
                    c2.Tono = TonoCifra.Verde;
                    c2.ParteRoja = sinBitacoras;
                    // Que quepa a 1366: con asistencias dadas, las canceladas van solo en la ayuda.
                    c2.Detalle = c.ClasesConDadas > 0
                        ? (sinBitacoras != null ? sinBitacoras + ", " + Miles(c.ClasesConDadas) + " con asistencia dada" : Miles(c.ClasesConDadas) + " sin bitácoras con asistencia dada")
                        : (sinBitacoras ?? "ninguna sin bitácoras") + (c.Canceladas > 0 ? ", " + Contar(c.Canceladas, "cancelada", "canceladas") : "");
                    c2.Ayuda = "Las clases del horario que ya terminaron, contadas como en la pestaña Faltas de Horarios: "
                        + Miles(c.ClasesConBitacorasX) + " con bitácoras" + (c.ClasesConDadas > 0 ? ", " : " y ") + Miles(c.ClasesSinBitacoras) + " sin bitácoras"
                        + (c.ClasesConDadas > 0 ? " y " + Miles(c.ClasesConDadas) + " sin bitácoras en las que su docente dio la asistencia a quien no pudo registrar (se dieron, pero no cuentan como con bitácoras)" : "")
                        + "." + (otras.Count > 0 ? " Aparte: " + Unir(otras) + "." : "") + " " + reglas;
                }

                // "Clases que se dieron": con bitacoras o con asistencias dadas, de las que contaban.
                CifraEstadistica dadas = c.ClasesQueSeDieronCifra;
                dadas.Etiqueta = "clases que se dieron";
                dadas.Pestana = PestanaEstadisticas.Docentes;
                if (c.ClasesQueContaban == 0)
                {
                    dadas.Valor = SinDato;
                    dadas.Tono = TonoCifra.Gris;
                    dadas.Ayuda = AyudaClasesQueSeDieron;
                }
                else
                {
                    string sinRegistro = c.ClasesSinRegistro > 0 ? Miles(c.ClasesSinRegistro) + " sin registro" : null;
                    dadas.Valor = Miles(c.ClasesQueSeDieron) + " de " + Miles(c.ClasesQueContaban);
                    dadas.Tono = TonoCifra.Verde;
                    dadas.ParteRoja = sinRegistro;
                    dadas.Detalle = (sinRegistro ?? "ninguna sin registro") + (c.Canceladas > 0 ? ", " + Contar(c.Canceladas, "cancelada", "canceladas") : "");
                    dadas.Ayuda = "De " + Contar(c.ClasesQueContaban, "clase del horario que ya pasó", "clases del horario que ya pasaron") + ": "
                        + Miles(c.ClasesConBitacorasX) + " con bitácoras"
                        + (c.ClasesConDadas > 0 ? ", " + Miles(c.ClasesConDadas) + " sin bitácoras pero con asistencias que dio su docente" : "")
                        + " y " + Miles(c.ClasesSinRegistro) + " sin registro." + (otras.Count > 0 ? " Aparte: " + Unir(otras) + "." : "") + " " + AyudaClasesQueSeDieron;
                }

                // C3: bitacoras registradas.
                c.BitacorasRegistradas = a.Bitacoras;
                c.EstudiantesDistintos = a.Estudiantes.Count;
                CifraEstadistica c3 = c.Bitacoras;
                c3.Etiqueta = "bitácoras registradas";
                c3.Pestana = PestanaEstadisticas.Laboratorios;
                c3.Valor = Miles(c.BitacorasRegistradas);
                c3.Tono = c.BitacorasRegistradas == 0 ? TonoCifra.Gris : TonoCifra.Verde;
                c3.Detalle = c.BitacorasRegistradas == 0 ? "ninguna en el periodo"
                    : c.EstudiantesDistintos == 1 ? "de 1 estudiante" : "de " + Miles(c.EstudiantesDistintos) + " estudiantes distintos";
                c3.Ayuda = "Cada vez que un estudiante registró su bitácora en una sesión del periodo, también en uso libre y fuera del horario."
                    + (L.ConDadas ? " No cuentan las asistencias que dio el docente: no son bitácoras." : "");

                // C4: fallas reportadas (bitacoras con falla y reportes de docentes).
                c.FallasDeEstudiantes = a.FallasDeBitacoras;
                c.FallasDeDocentes = a.DeDocentes;
                c.FallasReportadas = a.Fallas;
                c.Hardware = a.Hardware + a.DocHardware;
                c.Software = a.Software + a.DocSoftware;
                c.Red = a.Red + a.DocRed;
                CifraEstadistica c4 = c.Fallas;
                c4.Etiqueta = "fallas reportadas";
                c4.Pestana = PestanaEstadisticas.Equipos;
                c4.Valor = Miles(c.FallasReportadas);
                c4.Tono = c.FallasReportadas == 0 ? TonoCifra.Gris : TonoCifra.Naranja;
                List<string> areas = new List<string>();
                if (c.Hardware > 0)
                {
                    areas.Add("hardware " + Miles(c.Hardware));
                }

                if (c.Software > 0)
                {
                    areas.Add("software " + Miles(c.Software));
                }

                if (c.Red > 0)
                {
                    areas.Add("red " + Miles(c.Red));
                }

                c4.Detalle = areas.Count == 0 ? (c.FallasReportadas == 0 ? "ninguna reportada" : "ninguna de hardware, software ni red") : string.Join(", ", areas);
                string deEstudiantes = c.FallasDeEstudiantes == 1 ? "1 bitácora de estudiante con alguna falla" : Miles(c.FallasDeEstudiantes) + " bitácoras de estudiantes con alguna falla";
                c4.Ayuda = L.ConReportesDocentes
                    ? deEstudiantes + " y " + Contar(c.FallasDeDocentes, "reporte de docente", "reportes de docentes") + ". "
                      + "Cada área suma las bitácoras y los reportes de docentes de esa área; una bitácora puede reportar en varias y un reporte de docente "
                      + "puede ser de «otro», así que las tres pueden sumar más o menos que el total. En Alertas, el reparto por área es solo de las bitácoras. "
                      + "No cuentan «Todo bien», «Traje mi propio equipo» ni los avisos de equipos sin internet."
                    : deEstudiantes + ". Una bitácora puede reportar en varias áreas, así que las tres pueden sumar más que el total. "
                      + "No cuentan «Todo bien», «Traje mi propio equipo» ni los avisos de equipos sin internet.";

                // C5: equipos avisados sin internet (db/10).
                CifraEstadistica c5 = c.SinInternet;
                c5.Etiqueta = "equipos avisados sin internet";
                c5.Pestana = PestanaEstadisticas.Equipos;
                c5.Ayuda = "Equipos que otros estudiantes avisaron que no tenían internet. No es la falla de red del equipo del propio estudiante.";
                c.AvisosSinInternet = a.Avisos;
                if (!L.ConAvisos)
                {
                    c.EquiposSinInternet = null;
                    c5.Valor = SinDato;
                    c5.Tono = TonoCifra.Gris;
                    c5.Detalle = "falta una actualización de la base";
                    c5.Ayuda = "A la base de datos le falta una actualización (db/10): aún no se cuentan los equipos sin internet.";
                }
                else
                {
                    c.EquiposSinInternet = a.EquiposConAvisos;
                    c5.Valor = Miles(a.EquiposConAvisos);
                    c5.Tono = a.EquiposConAvisos == 0 ? TonoCifra.Gris : TonoCifra.Naranja;
                    c5.Detalle = a.Avisos == 0 ? "ningún aviso" : "en " + Contar(a.Avisos, "aviso", "avisos");
                    c5.Ayuda += " Un equipo cuenta una vez por bitácora; «7» y «PC-07» se juntan cuando en el laboratorio no hay otro equipo con ese número.";
                }
            }

            // -----------------------------------------------------------------
            // Estudiantes (sec. 3.2)
            // -----------------------------------------------------------------

            private void ArmarEstudiantes()
            {
                Dictionary<int, FilaEstudiante> filas = new Dictionary<int, FilaEstudiante>();
                Func<int, FilaEstudiante> fila = id =>
                {
                    FilaEstudiante f;
                    if (!filas.TryGetValue(id, out f))
                    {
                        EstudianteLeido estudiante = A.Estudiantes[id];
                        f = new FilaEstudiante
                        {
                            Id = id,
                            Matricula = estudiante.Matricula,
                            Nombre = estudiante.Nombre,
                            Activo = estudiante.Activo,
                            Grupo = estudiante.Grupo ?? ""
                        };
                        filas[id] = f;
                    }

                    return f;
                };

                // Las listas en las que esta con N > 0 en el filtro.
                foreach (AsistenciaDeLaClase deLaClase in asistencia.Values)
                {
                    foreach (ParDeAsistencia par in deLaClase.Pares.Where(p => p.Veces > 0 && A.Estudiantes.ContainsKey(p.AlumnoId)))
                    {
                        FilaEstudiante f = fila(par.AlumnoId);
                        f.Clases++;
                        f.Entro += par.Entro;
                        f.Veces += par.Veces;
                        f.ClasesBajo += par.Bajo ? 1 : 0;
                        f.Retiradas += par.Retiradas;
                        f.Dadas += par.Dadas;
                        f.SusClases.Add(par);
                    }
                }

                // Las bitacoras que no cuentan en ningun dia de ninguna lista de una clase
                // del periodo (uso libre, sesiones fuera de las fechas de sus clases).
                // Solo las del periodo: la lectura de la cache puede traer listas de
                // clases de antes (otro periodo), y saldria distinto que leido desde cero.
                HashSet<int> enAlgunaLista = new HashSet<int>();
                foreach (ClaseLeida delPeriodoClase in delPeriodo)
                {
                    AsistenciaDeClase lista;
                    if (!L.Listas.TryGetValue(delPeriodoClase.Id, out lista))
                    {
                        continue;
                    }

                    foreach (AlumnoDeAsistencia alumno in lista.Alumnos)
                    {
                        foreach (List<int> delDia in alumno.BitacorasDelDia.Values)
                        {
                            enAlgunaLista.UnionWith(delDia);
                        }
                    }
                }

                // Primero quien tiene alguna fuera (sale aunque su N sea 0), despues todas
                // las suyas: asi no depende del orden en que llegan.
                List<KeyValuePair<SesionLeida, BitacoraLeida>> suyas = contadas
                    .Where(p => EnElFiltroDeLaboratorio(p.Key.LaboratorioId) && A.Estudiantes.ContainsKey(p.Value.AlumnoId))
                    .ToList();
                foreach (KeyValuePair<SesionLeida, BitacoraLeida> par in suyas.Where(p => !enAlgunaLista.Contains(p.Value.Id)))
                {
                    fila(par.Value.AlumnoId);
                }

                foreach (KeyValuePair<SesionLeida, BitacoraLeida> par in suyas)
                {
                    SesionLeida sesion = par.Key;
                    BitacoraLeida b = par.Value;
                    FilaEstudiante f;
                    if (!filas.TryGetValue(b.AlumnoId, out f))
                    {
                        continue;
                    }

                    f.Bitacoras++;
                    f.Fallas += (b.Banderas & BitacoraLeida.BanderaConFalla) != 0 ? 1 : 0;
                    f.FueraDeSusClases += enAlgunaLista.Contains(b.Id) ? 0 : 1;
                    if (!f.UltimaBitacora.HasValue || sesion.Fecha > f.UltimaBitacora.Value)
                    {
                        f.UltimaBitacora = sesion.Fecha;
                    }
                }

                foreach (FilaEstudiante f in filas.Values)
                {
                    f.SusClases = f.SusClases.OrderBy(p => p.Materia, PorNombre).ThenBy(p => p.Grupo, PorNombre).ToList();
                }

                // Asistencia de menor a mayor, despues mas faltas; los "-" al final.
                R.Estudiantes = filas.Values
                    .OrderBy(f => f.SoloFuera ? 1 : 0)
                    .ThenBy(f => f.Porcentaje ?? double.MaxValue)
                    .ThenByDescending(f => f.Faltas)
                    .ThenBy(f => f.Nombre, PorNombre)
                    .ToList();
            }

            // -----------------------------------------------------------------
            // Clases (sec. 3.3), Grupos (sec. 3.7) y Docentes (sec. 3.4)
            // -----------------------------------------------------------------

            private ResumenDia CalendarioDe(int clase)
            {
                ResumenDia resumen;
                return calendarioPorClase.TryGetValue(clase, out resumen) ? resumen : new ResumenDia();
            }

            private void ArmarClases()
            {
                List<FilaClase> filas = new List<FilaClase>();
                foreach (ClaseLeida clase in clases.Where(EsDelLaboratorio))
                {
                    AsistenciaDeLaClase deLaClase = asistencia[clase.Id];
                    ResumenDia calendario = CalendarioDe(clase.Id);
                    HashSet<int> suyos;
                    laboratoriosDeLaClase.TryGetValue(clase.Id, out suyos);
                    int fallas;
                    fallasPorClase.TryGetValue(clase.Id, out fallas);
                    List<string> nombres = (suyos ?? new HashSet<int>()).Where(l => l > 0).Select(l => NombreDeLaboratorio(L, l)).OrderBy(n => n, PorNombre).ToList();
                    filas.Add(new FilaClase
                    {
                        ClaseId = clase.Id,
                        DocenteId = clase.ProfesorId,
                        GrupoId = clase.GrupoId,
                        Materia = clase.Materia,
                        Grupo = clase.Grupo,
                        Docente = clase.Docente,
                        Laboratorios = Unir(nombres),
                        NombresDeLaboratorios = nombres,
                        ConBitacoras = calendario.ConBitacoras,
                        QueContaban = calendario.ConBitacoras + calendario.Faltan + calendario.SinBitacorasConAsistencia,
                        SinBitacoras = calendario.Faltan,
                        ConDadas = calendario.SinBitacorasConAsistencia,
                        Canceladas = calendario.Canceladas,
                        Estudiantes = deLaClase.Estudiantes,
                        Entro = deLaClase.X,
                        Veces = deLaClase.N,
                        Bajo = deLaClase.Bajo,
                        Fallas = fallas,
                        DiasQueCuentan = deLaClase.DiasQueCuentan,
                        Lista = deLaClase.Pares
                    });
                }

                // Asistencia del grupo de menor a mayor; las que no tienen nada que contar, al final.
                R.Clases = filas
                    .OrderBy(f => f.SinNada ? 1 : 0)
                    .ThenBy(f => f.Asistencia ?? double.MaxValue)
                    .ThenBy(f => f.Materia, PorNombre)
                    .ThenBy(f => f.Grupo, PorNombre)
                    .ToList();
            }

            private void ArmarGrupos()
            {
                List<FilaGrupo> filas = new List<FilaGrupo>();
                foreach (IGrouping<int, ClaseLeida> grupo in clases.Where(EsDelLaboratorio).GroupBy(c => c.GrupoId))
                {
                    FilaGrupo fila = new FilaGrupo
                    {
                        GrupoId = grupo.Key,
                        Grupo = grupo.First().Grupo,
                        Clases = grupo.Count(),
                        Materias = Unir(grupo.Select(c => c.Materia).Distinct().OrderBy(m => m, PorNombre).ToList())
                    };
                    HashSet<int> estudiantes = new HashSet<int>();
                    HashSet<int> bajo = new HashSet<int>();
                    foreach (ClaseLeida clase in grupo)
                    {
                        AsistenciaDeLaClase deLaClase = asistencia[clase.Id];
                        fila.Entro += deLaClase.X;
                        fila.Veces += deLaClase.N;
                        estudiantes.UnionWith(deLaClase.Pares.Where(p => p.Veces > 0).Select(p => p.AlumnoId));
                        bajo.UnionWith(deLaClase.Pares.Where(p => p.Bajo).Select(p => p.AlumnoId));
                        ResumenDia calendario = CalendarioDe(clase.Id);
                        fila.ConBitacoras += calendario.ConBitacoras;
                        fila.SinBitacoras += calendario.Faltan;
                        fila.ConDadas += calendario.SinBitacorasConAsistencia;
                    }

                    fila.QueContaban = fila.ConBitacoras + fila.SinBitacoras + fila.ConDadas;
                    fila.Estudiantes = estudiantes.Count;
                    fila.Bajo = bajo.Count;
                    filas.Add(fila);
                }

                R.Grupos = filas
                    .OrderBy(f => f.SinNada ? 1 : 0)
                    .ThenBy(f => f.Asistencia ?? double.MaxValue)
                    .ThenBy(f => f.Grupo, PorNombre)
                    .ToList();
            }

            private void ArmarDocentes()
            {
                // Las asistencias que dio cada docente (db/22), activas, de sus clases del filtro y en sus dias.
                Dictionary<int, int> dadas = new Dictionary<int, int>();
                if (L.ConDadas)
                {
                    foreach (ClaseLeida clase in clases)
                    {
                        List<AsistenciaDada> deLaClase;
                        if (!A.DadasPorClase.TryGetValue(clase.Id, out deLaClase))
                        {
                            continue;
                        }

                        foreach (AsistenciaDada dada in deLaClase.Where(d => d.Fecha.Date >= desde && d.Fecha.Date <= hasta
                            && d.Fecha.Date >= clase.VigenteDesde && d.Fecha.Date <= clase.VigenteHasta && (lab == 0 || d.LaboratorioId == lab)))
                        {
                            Sumar(dadas, dada.ProfesorId, 1);
                            // La misma que se cuenta (la ficha del docente): sin el correo, solo matricula y nombre.
                            EstudianteLeido estudiante;
                            A.Estudiantes.TryGetValue(dada.AlumnoId, out estudiante);
                            R.AsistenciasDadas.Add(new FilaAsistenciaDada
                            {
                                Id = dada.Id,
                                DocenteId = dada.ProfesorId,
                                ClaseId = clase.Id,
                                Clase = clase.Materia + " (" + clase.Grupo + ")",
                                Fecha = dada.Fecha.Date,
                                DadaEn = dada.DadaEn,
                                AlumnoId = dada.AlumnoId,
                                Matricula = estudiante != null ? estudiante.Matricula : dada.Matricula ?? "",
                                Nombre = estudiante != null ? estudiante.Nombre : dada.Nombre ?? "",
                                Motivo = dada.Motivo ?? "",
                                Equipo = dada.Equipo,
                                Comentario = dada.Comentario,
                                SesionId = dada.SesionId,
                                LaboratorioId = dada.LaboratorioId
                            });
                        }
                    }
                }

                List<FilaDocente> filas = new List<FilaDocente>();
                foreach (IGrouping<int, ClaseLeida> docente in clases.Where(EsDelLaboratorio).GroupBy(c => c.ProfesorId))
                {
                    FilaDocente fila = new FilaDocente
                    {
                        DocenteId = docente.Key,
                        Docente = docente.First().Docente,
                        Materias = docente.Count(),
                        NombresDeMaterias = Unir(docente.OrderBy(c => c.Materia, PorNombre).ThenBy(c => c.Grupo, PorNombre)
                            .Select(c => c.Materia + " (" + c.Grupo + ")").ToList())
                    };
                    // Solo la asistencia del docente (decision del usuario, 2026-10-22): nada de estudiantes.
                    foreach (ClaseLeida clase in docente)
                    {
                        ResumenDia calendario = CalendarioDe(clase.Id);
                        fila.ConBitacoras += calendario.ConBitacoras;
                        fila.SinBitacoras += calendario.Faltan;
                        fila.ConDadas += calendario.SinBitacorasConAsistencia;
                        fila.Canceladas += calendario.Canceladas;
                        List<ClaseDelCalendario> entradas;
                        if (entradasDeLaClase.TryGetValue(clase.Id, out entradas))
                        {
                            fila.SusClasesDia.AddRange(entradas.Select(e => ClaseDia(clase, e)));
                        }
                    }

                    // De la mas reciente a la mas vieja.
                    fila.SusClasesDia = fila.SusClasesDia.OrderByDescending(d => d.Fecha).ThenByDescending(d => d.Inicio).ThenBy(d => d.Materia, PorNombre).ToList();
                    fila.QueContaban = fila.ConBitacoras + fila.SinBitacoras + fila.ConDadas;
                    reportesPorDocente.TryGetValue(docente.Key, out fila.SusReportes);
                    dadas.TryGetValue(docente.Key, out fila.AsistenciasQueDio);
                    filas.Add(fila);
                }

                // "Sin registro" de mayor a menor (pedido del usuario); los que no tienen nada que contar, al final.
                R.Docentes = filas
                    .OrderBy(f => f.SinNada ? 1 : 0)
                    .ThenByDescending(f => f.SinRegistro)
                    .ThenBy(f => f.Docente, PorNombre)
                    .ToList();
            }

            private static void Agregar<TValor>(Dictionary<int, List<TValor>> porClave, int clave, TValor valor)
            {
                List<TValor> lista;
                if (!porClave.TryGetValue(clave, out lista))
                {
                    lista = new List<TValor>();
                    porClave[clave] = lista;
                }

                lista.Add(valor);
            }

            /// <summary>Una clase-dia del calendario para el detalle de Docentes: "Se dio?" con su porque, como Faltas.</summary>
            private ClaseDiaDocente ClaseDia(ClaseLeida clase, ClaseDelCalendario c)
            {
                bool propias = c.SesionId.HasValue && c.Bitacoras > 0;
                ClaseDiaDocente dia = new ClaseDiaDocente
                {
                    ClaseId = clase.Id,
                    Fecha = c.Fecha,
                    FechaTexto = HorariosDatos.NombresDia[((int)c.Fecha.DayOfWeek + 6) % 7 + 1] + " " + EstadisticasDatos.Fecha(c.Fecha),
                    Materia = clase.Materia,
                    Grupo = clase.Grupo,
                    Inicio = c.Inicio,
                    Fin = c.Fin,
                    EsExtra = c.EsExtra,
                    Horario = HorariosDatos.Hora(c.Inicio) + "-" + HorariosDatos.Hora(c.Fin) + (c.EsExtra ? " (sesión extra)" : ""),
                    LaboratorioId = c.LaboratorioId,
                    Laboratorio = NombreDeLaboratorio(L, c.LaboratorioId),
                    Estado = c.Estado,
                    SesionId = c.SesionId,
                    Bitacoras = propias ? c.Bitacoras : c.BitacorasEnOtra,
                    AsistenciasDadas = c.AsistenciasDadas,
                    SesionDeSusBitacoras = propias ? c.SesionId : c.BitacorasEnOtra > 0 && c.OtraClase != null ? c.OtraClase.SesionId : null,
                    Calendario = c
                };
                switch (c.Estado)
                {
                    case EstadoEnCalendario.ConBitacoras:
                        dia.SeDioTexto = "Sí (" + Contar(dia.Bitacoras, "bitácora", "bitácoras") + ")";
                        if (!propias)
                        {
                            dia.Ayuda = c.EsResto
                                ? "Sus estudiantes registraron en la sesión de su franja, antes de que la clase se recortara."
                                : "Sus estudiantes llegaron antes y registraron en la sesión de la clase anterior del mismo docente (código fijo).";
                        }

                        // Las que dio ese dia tambien suman en su columna "Asistencias que dio".
                        if (c.AsistenciasDadas > 0)
                        {
                            dia.Ayuda = (dia.Ayuda == null ? "" : dia.Ayuda + " ")
                                + "Su docente también dio " + (c.AsistenciasDadas == 1 ? "1 asistencia" : Miles(c.AsistenciasDadas) + " asistencias") + " a quien no pudo registrar.";
                        }

                        break;
                    case EstadoEnCalendario.SinBitacorasConAsistencia:
                        dia.SeDioTexto = "Sí (" + (c.AsistenciasDadas == 1 ? "1 asistencia dada" : Miles(c.AsistenciasDadas) + " asistencias dadas") + ", sin bitácoras)";
                        dia.Ayuda = "Nadie registró su bitácora, pero su docente dio la asistencia a quien no pudo registrar: la clase sí se dio.";
                        break;
                    case EstadoEnCalendario.SinSesion:
                    case EstadoEnCalendario.SesionVacia:
                        dia.SeDioTexto = "Sin registro";
                        dia.Ayuda = (c.SesionId.HasValue ? "Su sesión se abrió, pero nadie registró su bitácora" : "Nadie abrió su sesión ni registró su bitácora")
                            + (L.Calendario.ConDadas ? " y su docente no dio asistencias" : "") + ". Puede que no se diera o que nadie registrara.";
                        break;
                    case EstadoEnCalendario.Cancelada:
                        dia.SeDioTexto = "Cancelada" + (string.IsNullOrWhiteSpace(c.Motivo) ? "" : ": " + c.Motivo.Trim());
                        // Cancelada, pero con bitacoras o asistencias dadas en su sesion: quiza
                        // si se dio (o se recorto ya empezada). Sigue sin contar, como en Faltas.
                        if (c.SesionId.HasValue && (c.Bitacoras > 0 || c.AsistenciasDadas > 0))
                        {
                            dia.Ayuda = "Se canceló, pero en su sesión hay "
                                + (c.Bitacoras > 0 ? Contar(c.Bitacoras, "bitácora", "bitácoras") : "")
                                + (c.Bitacoras > 0 && c.AsistenciasDadas > 0 ? " y " : "")
                                + (c.AsistenciasDadas > 0 ? (c.AsistenciasDadas == 1 ? "1 asistencia que dio su docente" : Miles(c.AsistenciasDadas) + " asistencias que dio su docente") : "")
                                + ": quizá sí se dio. Las canceladas no cuentan.";
                        }

                        break;
                    case EstadoEnCalendario.PorVenir:
                        // La de hoy que ya empezo esta "en curso", no "aun no pasa"; cuenta al terminar.
                        if (c.Fecha == hoy && c.Inicio <= L.Calendario.Ahora.TimeOfDay)
                        {
                            // Lo que lleva: sus bitacoras y las asistencias que dio su docente, las
                            // dos si las hay. Las dos juntas, en corto ("En curso (1 bitácora y 1
                            // asistencia)", sin "dada" ni "hasta ahora"; la ayuda lo dice entero):
                            // a 1920 el detalle va al lado de la lista y a la columna "Se dio?" de
                            // VentanaEstadisticas le quedan unos 268 px sin desplazar la tabla.
                            string bitacoras = dia.Bitacoras > 0 ? Contar(dia.Bitacoras, "bitácora", "bitácoras") : null;
                            string dadas = c.AsistenciasDadas == 1 ? "1 asistencia" : c.AsistenciasDadas > 1 ? Miles(c.AsistenciasDadas) + " asistencias" : null;
                            dia.SeDioTexto = bitacoras != null && dadas != null ? "En curso (" + bitacoras + " y " + dadas + ")"
                                : bitacoras != null ? "En curso (" + bitacoras + " hasta ahora)"
                                : dadas != null ? "En curso (" + dadas + (c.AsistenciasDadas == 1 ? " dada" : " dadas") + " hasta ahora)"
                                : "En curso";
                            dia.Ayuda = "La clase sigue en curso: cuenta al terminar."
                                + (dadas != null ? " Su docente dio " + dadas + " a quien no pudo registrar su bitácora." : "");
                        }
                        else
                        {
                            dia.SeDioTexto = "Aún no pasa";
                        }

                        break;
                    default:
                        dia.SeDioTexto = "No cuenta";
                        dia.Ayuda = PorQueNoCuenta(c);
                        break;
                }

                return dia;
            }

            /// <summary>Por que una clase "no cuenta" (EstadoEnCalendario.NoContaba), con las reglas de HorariosDatos.EstadoDe.</summary>
            private string PorQueNoCuenta(ClaseDelCalendario c)
            {
                DateTime empezo;
                bool conDigBit = L.Calendario.EmpezoEl.TryGetValue(c.LaboratorioId, out empezo);
                if (!L.Calendario.ContarTodos && (!conDigBit || c.Fecha < empezo))
                {
                    return conDigBit
                        ? "Ese día el laboratorio aún no registraba sus clases con DigBit (lo hace desde el " + EstadisticasDatos.Fecha(empezo) + "): sin bitácoras no cuenta, como en Faltas."
                        : "El laboratorio aún no registra sus clases con DigBit: sin bitácoras no cuenta, como en Faltas.";
                }

                if (!c.Utilizable)
                {
                    return c.EsExtra ? "Ese día la clase no estaba vigente o su docente estaba de baja: no cuenta." : "Ese día su docente estaba de baja: no cuenta.";
                }

                if (c.EsExtra && !c.EsResto && c.CreadaEn.HasValue && c.CreadaEn.Value >= c.Fecha + c.Fin)
                {
                    return "La sesión extra se puso cuando ya había pasado su hora: nadie pudo usarla.";
                }

                return "No cuenta, como en Faltas.";
            }

            // -----------------------------------------------------------------
            // Combos, lineas y notas (sec. 6.2 y sec. 6.5)
            // -----------------------------------------------------------------

            private void ArmarOpciones()
            {
                R.OpcionesLaboratorio.Add(new OpcionCombo { Id = 0, Texto = "Todos los laboratorios" });
                foreach (LaboratorioLeido laboratorio in L.Laboratorios.Values.OrderBy(l => l.Activo ? 0 : 1).ThenBy(l => l.Nombre, PorNombre))
                {
                    R.OpcionesLaboratorio.Add(new OpcionCombo
                    {
                        Id = laboratorio.Id,
                        Texto = laboratorio.Nombre + (!laboratorio.Activo ? " (desactivado)" : !laboratorio.EmpezoEl.HasValue ? " (aún no registra sus clases)" : "")
                    });
                }

                R.OpcionesDocente.Add(new OpcionCombo { Id = 0, Texto = "Todo el personal docente" });
                foreach (IGrouping<int, ClaseLeida> docente in delPeriodo.GroupBy(c => c.ProfesorId).OrderBy(g => g.First().Docente, PorNombre))
                {
                    R.OpcionesDocente.Add(new OpcionCombo { Id = docente.Key, Texto = docente.First().Docente });
                }

                R.OpcionesGrupo.Add(new OpcionCombo { Id = 0, Texto = "Todos los grupos" });
                foreach (IGrouping<int, ClaseLeida> grupo in delPeriodo.GroupBy(c => c.GrupoId).OrderBy(g => g.First().Grupo, PorNombre))
                {
                    R.OpcionesGrupo.Add(new OpcionCombo { Id = grupo.Key, Texto = grupo.First().Grupo });
                }
            }

            private void ArmarTextos()
            {
                // "Ciclo septiembre 2026-febrero 2027: del 30/09/2026 al 28/02/2027 (completo).", "Este
                // ciclo: 2027-1, del ... al ..." (con una plantilla aplicada), "Horario «2026-2»: del ...
                // al ... (en curso)." (una plantilla elegida en Periodo) o "Del ... al ...".
                string fin = FinDePlantilla(R.Plantilla);
                string cabeza = F.Tipo == TipoPeriodo.CicloPasado && R.Ciclo != null
                    ? TextoDeCiclo(R.Ciclo) + ": del " + Fecha(desde) + " al " + Fecha(hasta) + (recortado ? "." : " (completo).")
                    : F.Tipo == TipoPeriodo.Ciclo && R.Ciclo != null && R.Ciclo.NombreAplicado
                        ? NombreDeEsteCiclo(R.Ciclo) + ", del " + Fecha(desde) + " al " + Fecha(hasta) + "."
                    : F.Tipo == TipoPeriodo.Plantilla && R.Plantilla != null
                        ? NombreDePlantilla(R.Plantilla) + ": del " + Fecha(desde) + " al " + Fecha(hasta)
                            + (R.Plantilla.EnCurso ? " (en curso)." : recortado && fin == "completo" ? "." : " (" + fin + ").")
                        : DelAl(desde, hasta) + ".";
                R.LineaResumen = cabeza
                    + (recortado ? " DigBit cuenta como mucho " + DiasLeidos + " días seguidos: se cuenta desde el " + Fecha(desde) + "." : "")
                    + (F.AvisoDelPeriodo != null ? " " + F.AvisoDelPeriodo : "")
                    + (F.Aviso != null ? " " + F.Aviso : " Leído a las " + L.AhoraServidor.ToString("HH:mm", Inv) + " (hora del servidor).");

                int enListas = R.Estudiantes.Count(e => !e.SoloFuera);
                R.LineaEstudiantes = Contar(enListas, "estudiante", "estudiantes") + " en las listas de sus clases "
                    + (R.CicloCerrado ? "(solo quienes registraron en el ciclo: DigBit no tiene la lista oficial de cada grupo)."
                        : R.PeriodoPasado ? "(quien entró a la lista después cuenta con falta en el periodo)."
                        : "(solo quienes ya registraron: DigBit no tiene la lista oficial de cada grupo).");

                int clasesSinNada = R.Clases.Count(c => c.SinNada);
                R.LineaClases = Contar(R.Clases.Count, "clase", "clases") + (clasesSinNada > 0 ? "; " + Miles(clasesSinNada) + " sin nada que contar en el periodo" : "")
                    + ". Elige una para ver a sus estudiantes.";
                int gruposSinNada = R.Grupos.Count(g => g.SinNada);
                R.LineaGrupos = Contar(R.Grupos.Count, "grupo", "grupos") + (gruposSinNada > 0 ? "; " + Miles(gruposSinNada) + " sin nada que contar en el periodo" : "")
                    + ". Doble clic en uno para ver sus clases.";
                int docentesSinNada = R.Docentes.Count(d => d.SinNada);
                R.LineaDocentes = Contar(R.Docentes.Count, "docente", "docentes") + (docentesSinNada > 0 ? "; " + Miles(docentesSinNada) + " sin nada que contar en el periodo" : "")
                    + ". Las clases se cuentan como en la pestaña Faltas de Horarios.";
                R.LineaLaboratorios = "Pulsa un encabezado para ordenar. «Estudiantes distintos» de «Todos» no es la suma: un estudiante puede ir a varios laboratorios.";

                // Sin nada en el periodo. Con clases del horario que ya pasaron y contaban
                // (aunque todas sin registro) si hay algo: Docentes y Clases dicen cuantas
                // se dieron, y cada tabla vacia dice lo suyo.
                CifrasEstadisticas cifras = R.Cifras;
                if (cifras.BitacorasRegistradas == 0 && cifras.Veces == 0 && cifras.FallasDeDocentes == 0 && cifras.AvisosSinInternet == 0
                    && cifras.ClasesQueContaban == 0)
                {
                    LaboratorioLeido elegido;
                    R.TextoVacio = lab != 0 && L.Laboratorios.TryGetValue(lab, out elegido) && !elegido.EmpezoEl.HasValue
                        ? "Este laboratorio aún no registra con DigBit."
                        : "Aún no hay bitácoras en este periodo.";
                }

                // Las notas de la linea naranja, por prioridad (la de Windows ya esta).
                if (!L.ConAvisos)
                {
                    const string FaltaLa10 = "Falta una actualización de la base (db/10): aún no se cuentan los equipos sin internet.";
                    R.Notas.Add(new NotaEstadisticas
                    {
                        Prioridad = 1,
                        Texto = FaltaLa10,
                        TextoLargo = F.SoloConsulta ? FaltaLa10 + " Avisa a la administración de DigBit." : null
                    });
                }

                List<LaboratorioLeido> sinDigBit = L.Laboratorios.Values.Where(l => l.Activo && !l.EmpezoEl.HasValue).OrderBy(l => l.Nombre, PorNombre).ToList();
                if (sinDigBit.Count > 0)
                {
                    const string ComoEnFaltas = ": sus clases sin bitácoras no cuentan, como en Faltas.";
                    R.Notas.Add(new NotaEstadisticas
                    {
                        Prioridad = 2,
                        Texto = sinDigBit.Count == 1 ? "1 laboratorio aún no registra sus clases con DigBit" + ComoEnFaltas
                            : Miles(sinDigBit.Count) + " laboratorios aún no registran sus clases con DigBit" + ComoEnFaltas,
                        TextoLargo = sinDigBit.Count == 1 ? sinDigBit[0].Nombre + " aún no registra sus clases con DigBit" + ComoEnFaltas : null
                    });
                }

                R.Notas = R.Notas.OrderBy(n => n.Prioridad).ToList();
            }
        }
    }
}
