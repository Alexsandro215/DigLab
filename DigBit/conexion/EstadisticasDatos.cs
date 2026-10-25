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
    // (AsistenciaDatos.LeerEnLote, CalcularTodas y ResumenAsistencia.De) y
    // "Clases con bitacoras" como la pestana Faltas (LeerCalendario y ResumenDia).
    // =========================================================================

    /// <summary>Los periodos del combo, en este orden (R2).</summary>
    internal enum TipoPeriodo
    {
        /// <summary>"Este ciclo": de ciclo_desde (clases vigentes hoy) a hoy.</summary>
        Ciclo,
        /// <summary>Entre ciclos: las clases que terminaron hace 30 dias o menos.</summary>
        CicloAnterior,
        Mes,
        Semana,
        Ultimos30,
        /// <summary>"Elegir fechas...": los dos dias que eligio (de los ultimos 400).</summary>
        Fechas
    }

    /// <summary>Las seis pestanas, en su orden.</summary>
    internal enum PestanaEstadisticas
    {
        Estudiantes,
        Clases,
        Grupos,
        Docentes,
        Laboratorios,
        Equipos
    }

    /// <summary>
    /// El periodo que se pide. Con Fechas lleva sus dos dias; con los demas, los
    /// dias salen del reloj del servidor y de E0 al leer (EstadisticasDatos.Resolver).
    /// </summary>
    internal sealed class PeriodoElegido
    {
        public TipoPeriodo Tipo;
        /// <summary>Solo con Fechas: el primero y el ultimo dia, incluidos.</summary>
        public DateTime Desde;
        public DateTime Hasta;

        public static PeriodoElegido De(TipoPeriodo tipo)
        {
            return new PeriodoElegido { Tipo = tipo };
        }

        public static PeriodoElegido EntreFechas(DateTime desde, DateTime hasta)
        {
            return new PeriodoElegido { Tipo = TipoPeriodo.Fechas, Desde = desde.Date, Hasta = hasta.Date };
        }
    }

    /// <summary>
    /// El periodo y los filtros con que se calcula (EstadisticasDatos.Resolver). Desde
    /// es el del periodo sin recortar: con un ciclo de mas de 400 dias los dias que
    /// cuentan empiezan en hoy - 399 (DesdeDeLosDias), pero las clases del ciclo se
    /// eligen con el (revision de E1, punto 5).
    /// </summary>
    internal sealed class FiltroEstadisticas
    {
        /// <summary>El que quedo: "Este ciclo" sin clases vigentes hoy pasa a "Ciclo anterior" o a "Ultimos 30 dias".</summary>
        public TipoPeriodo Tipo;
        public DateTime Desde;
        public DateTime Hasta;
        /// <summary>"Este ciclo" o "Ciclo anterior": solo son del periodo las clases que empezaron en el (R2).</summary>
        public bool EsCiclo;
        /// <summary>0 = todos.</summary>
        public int Laboratorio;
        public int Docente;
        public int Grupo;
        /// <summary>La persona responsable del laboratorio (Permisos.SoloConsulta): cambia algun texto y lo que se guarda en CSV.</summary>
        public bool SoloConsulta;
        /// <summary>El docente o el grupo elegido no tiene clases en el periodo y se volvio a "Todos": lo que dice lblResumen, o null.</summary>
        public string Aviso;
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
        /// <summary>LeerCalendario(conexion, max(DesdeLectura, hoy - 399), hoy, false), como Faltas con la casilla sin marcar.</summary>
        public CalendarioBitacoras Calendario;
        /// <summary>E5 con EmpezoEl.</summary>
        public Dictionary<int, LaboratorioLeido> Laboratorios = new Dictionary<int, LaboratorioLeido>();
        /// <summary>E8 (vacia sin db/15).</summary>
        public List<ReporteDeDocente> ReportesDocentes = new List<ReporteDeDocente>();
        /// <summary>E10 por id de bitacora.</summary>
        public Dictionary<int, DetalleDeReporte> Detalles = new Dictionary<int, DetalleDeReporte>();
        /// <summary>E0: las clases activas vigentes hoy (ciclo) y, entre ciclos, las que terminaron hace 30 dias o menos.</summary>
        public DateTime? CicloDesde;
        public DateTime? CicloHasta;
        public DateTime? AnteriorDesde;
        public DateTime? AnteriorHasta;
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

        /// <summary>Lo leido empieza aqui (R3); acaba hoy.</summary>
        public DateTime DesdeLectura
        {
            get { return Asistencia.DesdeLectura; }
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
        public CifrasEstadisticas Cifras = new CifrasEstadisticas();
        public List<FilaEstudiante> Estudiantes = new List<FilaEstudiante>();
        public List<FilaClase> Clases = new List<FilaClase>();
        public List<FilaGrupo> Grupos = new List<FilaGrupo>();
        public List<FilaDocente> Docentes = new List<FilaDocente>();
        public List<FilaLaboratorio> Laboratorios = new List<FilaLaboratorio>();
        public List<FilaEquipo> Equipos = new List<FilaEquipo>();
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

        /// <summary>"Este ciclo" o "Ciclo anterior" con todos los laboratorios: la columna del 80 % es el derecho a evaluacion.</summary>
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

        private static readonly object candado = new object();
        private static LecturaEstadisticas guardada;

        // Sube con cada Olvidar. Una lectura que empezo antes (y acaba despues de
        // cerrar el panel o de "Actualizar") no se guarda: si no, volvia a dejar en
        // memoria los datos que Olvidar acababa de soltar.
        private static int vecesOlvidada;

        // =====================================================================
        // Lectura y cache
        // =====================================================================

        /// <summary>
        /// Lo que hace falta para el periodo, en una conexion: E0; LeerEnLote (E1,
        /// DesdeLectura, E2 a E4, modos, E6, E7 y E11); LeerCalendario; CalcularTodas
        /// con los tramos de hoy; E5, E8 y E10. Reutiliza la ultima lectura si es de la
        /// misma cuenta, del mismo dia del servidor, de hace menos de 10 minutos y
        /// cubre el periodo (Cubre); entonces ni abre la conexion. Va en segundo
        /// plano (LeerEnSegundoPlano): no toca la pantalla. Lanza si tiene que ir a
        /// la base y no hay conexion: quien llama lo ensena y sigue con la lectura
        /// de antes (y con su periodo: Calcular no acepta un periodo que esa
        /// lectura no cubre).
        /// </summary>
        /// <param name="forzar">"Actualizar" (F5): olvida la lectura guardada.</param>
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

            // Si la guardada sirve, sin abrir la conexion: con la base remota son 3
            // viajes menos por cada cambio de periodo, y sin red se sigue pudiendo
            // cambiar a un periodo que ya esta leido. La cuenta se sabe sin abrirla.
            string sinAbrir = CuentaSinConexion();
            LecturaEstadisticas previa;
            int olvidadaAlEmpezar;
            lock (candado)
            {
                previa = guardada;
                olvidadaAlEmpezar = vecesOlvidada;
            }

            if (previa != null && sinAbrir != null && previa.CuentaSinConexion == sinAbrir && Vigente(previa) && Cubre(previa, periodo))
            {
                return previa;
            }

            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                string cuenta = Esquema.CuentaDe(conexion);
                if (previa != null && previa.Cuenta == cuenta && Vigente(previa) && Cubre(previa, periodo))
                {
                    return previa;
                }

                LecturaEstadisticas leida = LeerDeLaBase(conexion, cuenta, periodo);
                leida.CuentaSinConexion = sinAbrir;
                lock (candado)
                {
                    // Si mientras se leia alguien llamo a Olvidar, se devuelve a quien
                    // la pidio pero no se guarda.
                    if (vecesOlvidada == olvidadaAlEmpezar)
                    {
                        guardada = leida;
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
        /// Olvida la lectura guardada: "Actualizar" y al cerrar el panel de la
        /// administracion (otra cuenta no la reutiliza y no quedan datos de
        /// estudiantes en memoria). Las lecturas que ya estaban en camino tampoco
        /// se guardan al terminar.
        /// </summary>
        public static void Olvidar()
        {
            lock (candado)
            {
                guardada = null;
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
        /// La lectura sirve para ese periodo: la lista de cada clase del periodo sale
        /// entera de lo leido (su DesdeLectura, calculado con E1, no es anterior al de
        /// la lectura). Las clases del periodo se eligen con el esCiclo del periodo nuevo.
        /// </summary>
        public static bool Cubre(LecturaEstadisticas lectura, PeriodoElegido periodo)
        {
            TipoPeriodo tipo;
            DateTime desde, hasta;
            Fechas(periodo, lectura.Hoy, lectura.CicloDesde, lectura.AnteriorDesde, lectura.AnteriorHasta, out tipo, out desde, out hasta);
            bool esCiclo = tipo == TipoPeriodo.Ciclo || tipo == TipoPeriodo.CicloAnterior;
            return AsistenciaDatos.DesdeLecturaDe(lectura.Asistencia.Clases, desde, hasta, esCiclo) >= lectura.DesdeLectura;
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
                TipoPeriodo tipo;
                DateTime desde, hasta;
                Fechas(periodo, ahoraE0.Date, lectura.CicloDesde, lectura.AnteriorDesde, lectura.AnteriorHasta, out tipo, out desde, out hasta);
                bool esCiclo = tipo == TipoPeriodo.Ciclo || tipo == TipoPeriodo.CicloAnterior;

                lectura.Asistencia = AsistenciaDatos.LeerEnLote(conexion, desde, hasta, esCiclo);
                DateTime hoy = lectura.Hoy;
                DateTime desdeCalendario = lectura.DesdeLectura > hoy.AddDays(-(DiasLeidos - 1)) ? lectura.DesdeLectura : hoy.AddDays(-(DiasLeidos - 1));
                lectura.Calendario = HorariosDatos.LeerCalendario(conexion, desdeCalendario, hoy, false);
                if (vuelta == 0 && (ahoraE0.Date != hoy || lectura.Calendario.Ahora.Date != hoy))
                {
                    continue;
                }

                lectura.Listas = AsistenciaDatos.CalcularTodas(lectura.Asistencia, TramosDeHoy(lectura.Calendario, hoy));

                // Cada Esquema.* una vez por lectura (lo que falta se vuelve a preguntar cada vez).
                lectura.ConAvisos = Esquema.EquiposSinInternet(conexion);
                bool conLlegada = lectura.ConAvisos && Esquema.HoraDeLlegada(conexion);
                lectura.ConReportesDocentes = Esquema.ReportesDeProfesor(conexion);
                LeerLaboratorios(conexion, lectura, Esquema.LaboratoriosDesactivables(conexion));
                if (lectura.ConReportesDocentes)
                {
                    LeerReportesDocentes(conexion, lectura, hoy);
                }

                LeerDetalles(conexion, lectura, hoy, conLlegada);
                lectura.LeidaUtc = DateTime.UtcNow;
                lectura.Duracion = reloj.Elapsed;
                Log.Info("Estadisticas: leido de " + lectura.DesdeLectura.ToString("yyyy-MM-dd", Inv) + " a " + hoy.ToString("yyyy-MM-dd", Inv)
                    + " en " + (int)reloj.ElapsedMilliseconds + " ms: " + lectura.Asistencia.Clases.Count + " clases, "
                    + lectura.Asistencia.SesionPorId.Count + " sesiones, " + lectura.Asistencia.BitacorasPorSesion.Values.Sum(l => l.Count) + " bitacoras.");
                return lectura;
            }
        }

        /// <summary>E0: el reloj del servidor y el ciclo (e0_reloj_ciclo_rev.sql). Devuelve NOW().</summary>
        private static DateTime LeerReloj(MySqlConnection conexion, LecturaEstadisticas lectura)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT NOW() AS ahora,
                       (SELECT MIN(c.vigente_desde) FROM clases c
                         WHERE c.activa = 1 AND c.vigente_desde <= CURDATE() AND c.vigente_hasta >= CURDATE()) AS ciclo_desde,
                       (SELECT MAX(c.vigente_hasta) FROM clases c
                         WHERE c.activa = 1 AND c.vigente_desde <= CURDATE() AND c.vigente_hasta >= CURDATE()) AS ciclo_hasta,
                       (SELECT MIN(c.vigente_desde) FROM clases c
                         WHERE c.activa = 1 AND c.vigente_hasta < CURDATE() AND c.vigente_hasta >= CURDATE() - INTERVAL 30 DAY) AS anterior_desde,
                       (SELECT MAX(c.vigente_hasta) FROM clases c
                         WHERE c.activa = 1 AND c.vigente_hasta < CURDATE() AND c.vigente_hasta >= CURDATE() - INTERVAL 30 DAY) AS anterior_hasta", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                reader.Read();
                lectura.CicloDesde = FechaONula(reader["ciclo_desde"]);
                lectura.CicloHasta = FechaONula(reader["ciclo_hasta"]);
                lectura.AnteriorDesde = FechaONula(reader["anterior_desde"]);
                lectura.AnteriorHasta = FechaONula(reader["anterior_hasta"]);
                return Convert.ToDateTime(reader["ahora"], Inv);
            }
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

        /// <summary>E8: los reportes de docentes de [DesdeLectura, hoy] (db/15).</summary>
        private static void LeerReportesDocentes(MySqlConnection conexion, LecturaEstadisticas lectura, DateTime hoy)
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
                comando.Parameters.AddWithValue("@Desde", lectura.DesdeLectura.Date);
                comando.Parameters.AddWithValue("@Hasta", hoy.Date);
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
        private static void LeerDetalles(MySqlConnection conexion, LecturaEstadisticas lectura, DateTime hoy, bool conLlegada)
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
                comando.Parameters.AddWithValue("@Hasta", hoy.Date);
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
        /// Los dias del periodo con el reloj del servidor (R2). "Este ciclo" sin
        /// clases vigentes hoy pasa a "Ciclo anterior" y, sin ese, a "Ultimos 30
        /// dias". Con Fechas, recortadas a hoy y a los ultimos 400 dias.
        /// </summary>
        private static void Fechas(PeriodoElegido periodo, DateTime hoy, DateTime? cicloDesde, DateTime? anteriorDesde, DateTime? anteriorHasta,
            out TipoPeriodo tipo, out DateTime desde, out DateTime hasta)
        {
            hoy = hoy.Date;
            tipo = periodo.Tipo;
            if (tipo == TipoPeriodo.Ciclo && !cicloDesde.HasValue)
            {
                tipo = anteriorDesde.HasValue ? TipoPeriodo.CicloAnterior : TipoPeriodo.Ultimos30;
            }
            else if (tipo == TipoPeriodo.CicloAnterior && !anteriorDesde.HasValue)
            {
                tipo = cicloDesde.HasValue ? TipoPeriodo.Ciclo : TipoPeriodo.Ultimos30;
            }

            hasta = hoy;
            switch (tipo)
            {
                case TipoPeriodo.Ciclo:
                    desde = cicloDesde.Value;
                    break;
                case TipoPeriodo.CicloAnterior:
                    desde = anteriorDesde.Value;
                    hasta = anteriorHasta.HasValue && anteriorHasta.Value < hoy ? anteriorHasta.Value : hoy;
                    break;
                case TipoPeriodo.Mes:
                    desde = new DateTime(hoy.Year, hoy.Month, 1);
                    break;
                case TipoPeriodo.Semana:
                    desde = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
                    break;
                case TipoPeriodo.Fechas:
                    hasta = periodo.Hasta.Date > hoy ? hoy : periodo.Hasta.Date;
                    desde = periodo.Desde.Date < hoy.AddDays(-(DiasLeidos - 1)) ? hoy.AddDays(-(DiasLeidos - 1)) : periodo.Desde.Date;
                    if (desde > hasta)
                    {
                        desde = hasta;
                    }

                    break;
                default:
                    desde = hoy.AddDays(-29);
                    break;
            }
        }

        /// <summary>
        /// El filtro con que se calcula: los dias del periodo (con el reloj y E0 de la
        /// lectura) y los combos. Si el docente o el grupo elegido no tiene clases en
        /// el periodo, vuelve a "Todos" y Aviso lo dice (sec. 6.5). Un laboratorio que ya
        /// no existe, tambien a "Todos".
        /// </summary>
        public static FiltroEstadisticas Resolver(PeriodoElegido periodo, LecturaEstadisticas lectura, int laboratorio, int docente, int grupo)
        {
            FiltroEstadisticas filtro = new FiltroEstadisticas
            {
                Laboratorio = laboratorio,
                Docente = docente,
                Grupo = grupo,
                SoloConsulta = Permisos.SoloConsulta
            };
            Fechas(periodo, lectura.Hoy, lectura.CicloDesde, lectura.AnteriorDesde, lectura.AnteriorHasta, out filtro.Tipo, out filtro.Desde, out filtro.Hasta);
            filtro.EsCiclo = filtro.Tipo == TipoPeriodo.Ciclo || filtro.Tipo == TipoPeriodo.CicloAnterior;
            if (filtro.Laboratorio != 0 && !lectura.Laboratorios.ContainsKey(filtro.Laboratorio))
            {
                filtro.Laboratorio = 0;
            }

            List<ClaseLeida> delPeriodo = lectura.Asistencia.Clases.Where(c => AsistenciaDatos.DelPeriodo(c, filtro.Desde, filtro.Hasta, filtro.EsCiclo)).ToList();
            bool sinDocente = filtro.Docente != 0 && !delPeriodo.Any(c => c.ProfesorId == filtro.Docente);
            bool sinGrupo = filtro.Grupo != 0 && !delPeriodo.Any(c => c.GrupoId == filtro.Grupo);
            if (sinDocente)
            {
                filtro.Docente = 0;
            }

            if (sinGrupo)
            {
                filtro.Grupo = 0;
            }

            filtro.Aviso = sinDocente && sinGrupo ? "Ese docente y ese grupo no tienen clases en estas fechas: se ve todo."
                : sinDocente ? "Ese docente no tiene clases en estas fechas: se ve todo el personal."
                : sinGrupo ? "Ese grupo no tiene clases en estas fechas: se ven todos."
                : null;
            return filtro;
        }

        /// <summary>Las opciones del combo Periodo, en orden: "Este ciclo" (o, entre ciclos, "Ciclo anterior" si lo hay) y las demas.</summary>
        public static List<TipoPeriodo> OpcionesDePeriodo(LecturaEstadisticas lectura)
        {
            List<TipoPeriodo> opciones = new List<TipoPeriodo>();
            if (lectura == null || lectura.CicloDesde.HasValue)
            {
                opciones.Add(TipoPeriodo.Ciclo);
            }
            else if (lectura.AnteriorDesde.HasValue)
            {
                opciones.Add(TipoPeriodo.CicloAnterior);
            }

            opciones.AddRange(new[] { TipoPeriodo.Mes, TipoPeriodo.Semana, TipoPeriodo.Ultimos30, TipoPeriodo.Fechas });
            return opciones;
        }

        /// <summary>
        /// El texto del combo: "Este ciclo (desde el 30/09/2026)", "Ciclo anterior (del
        /// 30/09/2026)", "Este mes", "Esta semana", "Ultimos 30 dias", "Del 05/10/2026 al
        /// 11/10/2026" o "Elegir fechas...". Sin lectura todavia, "Este ciclo".
        /// </summary>
        public static string TextoDePeriodo(TipoPeriodo tipo, LecturaEstadisticas lectura, FiltroEstadisticas filtro)
        {
            switch (tipo)
            {
                case TipoPeriodo.Ciclo:
                    return lectura != null && lectura.CicloDesde.HasValue ? "Este ciclo (desde el " + Fecha(lectura.CicloDesde.Value) + ")" : "Este ciclo";
                case TipoPeriodo.CicloAnterior:
                    return lectura != null && lectura.AnteriorDesde.HasValue ? "Ciclo anterior (del " + Fecha(lectura.AnteriorDesde.Value) + ")" : "Ciclo anterior";
                case TipoPeriodo.Mes:
                    return "Este mes";
                case TipoPeriodo.Semana:
                    return "Esta semana";
                case TipoPeriodo.Ultimos30:
                    return "Últimos 30 días";
                default:
                    return filtro != null && filtro.Tipo == TipoPeriodo.Fechas ? DelAl(filtro.Desde, filtro.Hasta) : "Elegir fechas...";
            }
        }

        /// <summary>"Del 05/10/2026 al 11/10/2026" (el periodo completo, para la ayuda del combo y lblResumen).</summary>
        public static string DelAl(DateTime desde, DateTime hasta)
        {
            return "Del " + Fecha(desde) + " al " + Fecha(hasta);
        }

        /// <summary>El primer dia que se puede elegir en "Elegir fechas" (hoy - 399).</summary>
        public static DateTime PrimerDiaElegible(DateTime hoy)
        {
            return hoy.Date.AddDays(-(DiasLeidos - 1));
        }

        /// <summary>Lo que dice el dialogo "Elegir fechas" si no valen (sec. 6.4), o null.</summary>
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

            if (desde.Date < PrimerDiaElegible(hoy))
            {
                return "Elige fechas de los últimos 400 días: desde el " + Fecha(PrimerDiaElegible(hoy)) + ".";
            }

            return null;
        }

        /// <summary>
        /// Si esa pestana se guarda en CSV: la persona responsable del laboratorio solo
        /// guarda Laboratorios, Grupos y Equipos, que no llevan datos personales (sec. 7;
        /// en las demas, SinCsvParaSoloConsulta). En kiosco no hay CSV (lo decide la ventana).
        /// </summary>
        public static bool SeGuardaEnCsv(PestanaEstadisticas pestana, bool soloConsulta)
        {
            return !soloConsulta || pestana == PestanaEstadisticas.Laboratorios || pestana == PestanaEstadisticas.Grupos || pestana == PestanaEstadisticas.Equipos;
        }

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
            if (AsistenciaDatos.DesdeLecturaDe(lectura.Asistencia.Clases, filtro.Desde, filtro.Hasta, filtro.EsCiclo) < lectura.DesdeLectura)
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
            /// <summary>Los dias que cuentan: de desde (recortado a hoy - 399) a hasta.</summary>
            private readonly DateTime desde;
            private readonly DateTime hasta;
            private readonly bool recortado;
            private readonly int lab;
            /// <summary>
            /// Desde donde leeria una lectura hecha para este periodo (DesdeLecturaDe).
            /// La de la cache puede venir de antes (otro periodo): lo que se mira fuera
            /// del periodo se corta aqui, para que salga lo mismo que leido desde cero.
            /// </summary>
            private readonly DateTime desdeLeido;

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

            private sealed class Acumulado
            {
                public readonly ResumenDia Clases = new ResumenDia();
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
                DateTime primero = PrimerDiaElegible(hoy);
                recortado = filtro.Desde.Date < primero;
                desde = recortado ? primero : filtro.Desde.Date;
                hasta = filtro.Hasta.Date > hoy ? hoy : filtro.Hasta.Date;
                lab = filtro.Laboratorio;
                desdeLeido = AsistenciaDatos.DesdeLecturaDe(A.Clases, filtro.Desde, filtro.Hasta, filtro.EsCiclo);
            }

            private bool EnElFiltroDeLaboratorio(int? laboratorio)
            {
                return lab == 0 || (laboratorio ?? 0) == lab;
            }

            public ResultadoEstadisticas Hacer()
            {
                R.Filtro = F;
                R.ConAvisos = L.ConAvisos;
                R.ConReportesDocentes = L.ConReportesDocentes;
                R.ConDadas = L.ConDadas;
                R.CicloEntero = F.EsCiclo && lab == 0;
                R.PeriodoPasado = hasta < hoy;
                R.TituloDerecho = R.CicloEntero ? "Derecho a evaluación (80 %)" : "80 % o más en el periodo";
                R.TituloDerechoCorto = R.CicloEntero ? "Derecho (80 %)" : "80 % o más";

                ElegirClasesYSesiones();
                ContarBitacorasYReportes();
                ContarCalendario();
                ContarAsistencia();
                equipos = ArmarEquipos();
                ArmarLaboratorios();
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
                delPeriodo = A.Clases.Where(c => AsistenciaDatos.DelPeriodo(c, F.Desde, F.Hasta, F.EsCiclo)).ToList();
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

                    DelLaboratorio(clase.LaboratorioId).Clases.Contar(clase.Estado);
                    todos.Clases.Contar(clase.Estado);
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

                // Solo lo de [desdeLeido, hoy], lo que tendria una lectura de este
                // periodo: la de la cache puede traer mas dias, y un "LAB-7" de antes
                // cambiaria a que fila va "7" segun el camino por el que se llego.
                foreach (KeyValuePair<int, List<BitacoraLeida>> sesion in A.BitacorasPorSesion)
                {
                    SesionLeida leida;
                    if (!A.SesionPorId.TryGetValue(sesion.Key, out leida) || leida.Fecha < desdeLeido)
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
                    if (!A.SesionPorId.TryGetValue(detalle.SesionId, out leida) || leida.Fecha < desdeLeido)
                    {
                        continue;
                    }

                    int laboratorio = leida.LaboratorioId ?? 0;
                    foreach (string nombre in detalle.SinInternet.Split(','))
                    {
                        anotar(laboratorio, nombre);
                    }
                }

                foreach (ReporteDeDocente reporte in L.ReportesDocentes.Where(r => r.Equipo != null && r.Dia >= desdeLeido))
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
                        + (R.PeriodoPasado ? " Quien entró a la lista después del periodo cuenta con falta en sus días, como en Mis grupos: "
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
                    filas.Add(new FilaClase
                    {
                        ClaseId = clase.Id,
                        DocenteId = clase.ProfesorId,
                        GrupoId = clase.GrupoId,
                        Materia = clase.Materia,
                        Grupo = clase.Grupo,
                        Docente = clase.Docente,
                        Laboratorios = Unir((suyos ?? new HashSet<int>()).Where(l => l > 0).Select(l => NombreDeLaboratorio(L, l)).OrderBy(n => n, PorNombre).ToList()),
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
                    SesionDeSusBitacoras = propias ? c.SesionId : c.BitacorasEnOtra > 0 && c.OtraClase != null ? c.OtraClase.SesionId : null
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
                R.LineaResumen = DelAl(desde, hasta) + "."
                    + (recortado ? " Solo se leen los últimos 400 días." : "")
                    + (F.Aviso != null ? " " + F.Aviso : " Leído a las " + L.AhoraServidor.ToString("HH:mm", Inv) + " (hora del servidor).");

                int enListas = R.Estudiantes.Count(e => !e.SoloFuera);
                R.LineaEstudiantes = Contar(enListas, "estudiante", "estudiantes") + " en las listas de sus clases "
                    + (R.PeriodoPasado ? "(quien entró a la lista después cuenta con falta en el periodo)." : "(solo quienes ya registraron: DigBit no tiene la lista oficial de cada grupo).");

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
