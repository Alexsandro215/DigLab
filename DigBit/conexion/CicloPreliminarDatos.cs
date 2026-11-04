using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Que es una configuracion de horario (ciclos_preliminares.estado, db/16 y db/23).</summary>
    public enum EstadoConfiguracion
    {
        /// <summary>'borrador': la que se prepara (como mucho una). La unica que se edita.</summary>
        EnPreparacion,
        /// <summary>'guardado': una copia con nombre. No se edita: se carga, se renombra, se borra o se reemplaza con Guardar.</summary>
        Guardada,
        /// <summary>'publicado': el registro de un horario que se aplico. Se ve y se carga, pero no se borra.</summary>
        Aplicada
    }

    /// <summary>
    /// Una configuracion de horario (ciclos_preliminares, db/16 y db/23): cada
    /// docente con sus clases y sus horas en cada laboratorio, con nombre.
    /// </summary>
    public class CicloPreliminar
    {
        public int Id;
        public string Nombre = "";
        public EstadoConfiguracion Estado;
        /// <summary>Primer dia del horario: el vigente_desde de sus clases al aplicarla. Null en una guardada y en la que se prepara hasta ponerlo en Aplicacion.</summary>
        public DateTime? Desde;
        /// <summary>Ultimo dia del horario: el vigente_hasta.</summary>
        public DateTime? Hasta;
        /// <summary>El dia cuyas clases se copiaron; null si se empezo en blanco o de otra configuracion.</summary>
        public DateTime? CopiadoDel;
        public DateTime CreadoEn;
        /// <summary>El ultimo cambio (en una guardada, cuando se guardo por ultima vez).</summary>
        public DateTime? CambiadoEn;
        /// <summary>El numero de empleado de quien hizo el ultimo cambio ("ADMIN001"), o vacio.</summary>
        public string CambiadoPor = "";
        /// <summary>
        /// De que configuracion viene (o en cual se guardo por ultima vez con
        /// Guardar o Guardar como). Puede ya no existir: entonces OrigenEstado es null.
        /// </summary>
        public int? OrigenId;
        public string OrigenNombre = "";
        public EstadoConfiguracion? OrigenEstado;
        /// <summary>Aplicada: el ultimo dia que se dio al horario de antes.</summary>
        public DateTime? FinAnterior;
        /// <summary>Aplicada: el numero de empleado de quien la aplico.</summary>
        public string PublicadoPor = "";
        public DateTime? PublicadoEn;
        /// <summary>
        /// Aplicada: el ultimo dia en que se dio, si una aplicada posterior la
        /// corto antes de su Hasta (su fin_anterior). Lo llena LeerSituacion.
        /// </summary>
        public DateTime? CortadaEl;

        // Las cuentas de la lista (LeerCiclos): de sus clases sin quitar.
        public int Clases;
        /// <summary>Personas docentes distintas (las clases sin docente no cuentan).</summary>
        public int Docentes;
        /// <summary>Franjas: los horarios de sus clases.</summary>
        public int Horarios;
        public double Horas;
        public int Laboratorios;

        public bool EnPreparacion
        {
            get { return Estado == EstadoConfiguracion.EnPreparacion; }
        }

        public bool Guardada
        {
            get { return Estado == EstadoConfiguracion.Guardada; }
        }

        public bool Aplicada
        {
            get { return Estado == EstadoConfiguracion.Aplicada; }
        }

        /// <summary>En preparacion y con donde guardarse con "Guardar" (GuardarEn): su origen es una guardada que sigue existiendo.</summary>
        public bool SeGuardaEnOrigen
        {
            get { return EnPreparacion && OrigenId.HasValue && OrigenEstado == EstadoConfiguracion.Guardada; }
        }

        /// <summary>El ultimo dia en que se da (o se dio) una aplicada: Hasta, o antes si otra la corto.</summary>
        public DateTime? UltimoDia
        {
            get { return CortadaEl ?? Hasta; }
        }

        /// <summary>Aplicada que todavia no empieza (desde despues de hoy).</summary>
        public bool PorEmpezar(DateTime hoy)
        {
            return Aplicada && Desde.HasValue && Desde.Value.Date > hoy.Date;
        }

        /// <summary>Aplicada y en uso hoy.</summary>
        public bool EnUso(DateTime hoy)
        {
            return Aplicada && Desde.HasValue && UltimoDia.HasValue && Desde.Value.Date <= hoy.Date && hoy.Date <= UltimoDia.Value.Date;
        }

        /// <summary>
        /// Frente a que dice cada clase lo que cambio (RevisionCiclo.ComoQuedaDe):
        /// "Frente a <<2026-1>>", "Frente al horario del 24/10/2026", o vacio si se
        /// empezo en blanco y nunca se guardo (entonces todas son "Nueva").
        /// Guardar y Guardar como dejan las clases comparadas con lo que se guardo.
        /// </summary>
        public string FrenteA
        {
            get
            {
                if (OrigenId.HasValue)
                {
                    return OrigenEstado.HasValue
                        ? "Frente a «" + OrigenNombre + "»"
                        : "Frente a la plantilla de la que viene, que ya se borró";
                }

                return CopiadoDel.HasValue
                    ? "Frente al horario del " + CopiadoDel.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    : "";
            }
        }
    }

    /// <summary>Las horas de una clase de la configuracion en un laboratorio (ciclo_franjas).</summary>
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
    /// Una materia con un grupo en una configuracion (ciclo_cargas): una clase al
    /// aplicarla. Recuerda como era en lo que se copio (o en lo que se guardo
    /// por ultima vez), para decir que cambio.
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
        /// <summary>En la v2 siempre false: Quitar borra la fila.</summary>
        public bool Quitada;
        /// <summary>La clase real de la que viene (en una aplicada, la que creo); null si se agrego a mano.</summary>
        public int? ClaseOrigen;
        public int? ProfesorOrigenId;
        public string ProfesorOrigenCorto = "";
        public int? GrupoOrigenId;
        public string GrupoOrigen = "";
        public int? MateriaOrigenId;
        public string MateriaOrigen = "";
        /// <summary>La firma de su horario en lo que se copio (RevisionCiclo.Firma); null si no se copio.</summary>
        public string HorarioOrigen;
        public int? ClasePublicada;
        /// <summary>La clase de la configuracion de la que se copio (db/23); puede ya no existir.</summary>
        public int? CargaOrigen;
        public List<FranjaCiclo> Franjas = new List<FranjaCiclo>();

        /// <summary>"Graficacion con 5ISC11".</summary>
        public string Nombre
        {
            get { return Materia + " con " + Grupo; }
        }

        /// <summary>Se copio (de un dia, de otra configuracion o de lo que se guardo): no es nueva.</summary>
        public bool Copiada
        {
            get { return ClaseOrigen.HasValue || ProfesorOrigenId.HasValue || GrupoOrigenId.HasValue || CargaOrigen.HasValue; }
        }
    }

    /// <summary>
    /// Unas horas que le faltan a una clase del horario en preparacion ("Falta
    /// asignar", como en Horario semanal con db/25): las que otra clase le quito
    /// al ponerse encima, o las que se quitaron con "Quitar horas" para darlas en
    /// otro horario. Bajan solas al ponerle horas a la clase (lo mas antiguo
    /// primero). Sin migracion: van en ciclo_cargas (CicloPreliminarDatos.QuitadaPorAsignar).
    /// </summary>
    public class PorAsignarCiclo
    {
        /// <summary>El idcarga del renglon que las guarda (para "Ya no hace falta").</summary>
        public int Id;
        /// <summary>La clase de la plantilla a la que le faltan (su idcarga).</summary>
        public int CargaId;
        /// <summary>Lo que falta, en minutos (baja al ponerle horas a la clase).</summary>
        public int Minutos;
        public int LaboratorioId;
        public string Laboratorio = "";
        /// <summary>De donde salieron: el dia (1 = lunes) y las horas que se le quitaron.</summary>
        public int DiaSemana;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        /// <summary>HorariosDatos.MotivoDesplazada (otra clase se puso en su lugar) o MotivoQuitada ("Quitar horas").</summary>
        public string Motivo = "";

        /// <summary>Lo que faltaba al crearse: las horas de donde salieron.</summary>
        public int MinutosOriginales
        {
            get { return HorariosDatos.MinutosEntre(Inicio, Fin); }
        }

        /// <summary>"Laboratorio CISCO, lunes 08:00–10:00".</summary>
        public string Origen
        {
            get
            {
                return (string.IsNullOrEmpty(Laboratorio) ? "" : EstiloHorarios.NombreLaboratorio(Laboratorio) + ", ")
                    + (DiaSemana >= 1 && DiaSemana <= 7 ? HorariosDatos.NombresDia[DiaSemana].ToLowerInvariant() + " " : "")
                    + HorariosDatos.Hora(Inicio) + "–" + HorariosDatos.Hora(Fin);
            }
        }
    }

    /// <summary>Una persona docente con clases en la configuracion (para la leyenda y el filtro).</summary>
    public class ProfesorDelCiclo
    {
        public int Id;
        /// <summary>El nombre entero.</summary>
        public string Nombre = "";
        public string Corto = "";
        public bool Activo = true;
    }

    /// <summary>Una clase de este ciclo que ninguna clase de la configuracion copio, y por que (para despues: punto 8).</summary>
    public class ClaseNoCopiada
    {
        public ClaseInfo Clase;
        public List<FranjaInfo> Franjas = new List<FranjaInfo>();
        public string PorQue = "";
    }

    /// <summary>
    /// Las fechas con las que se revisa una configuracion antes de aplicarla:
    /// el primer y el ultimo dia del horario nuevo y el ultimo del de ahora.
    /// </summary>
    public class DatosDeFechas
    {
        public DateTime Desde;
        public DateTime Hasta;
        /// <summary>El ultimo dia del horario de ahora.</summary>
        public DateTime Fin;
        /// <summary>
        /// "Ahora mismo" (pestana Aplicacion): el horario nuevo empieza al
        /// aplicarlo. Desde es hoy y Fin ayer, los dos del servidor; lo ya
        /// registrado hoy en las clases que se recortan no impide aplicar.
        /// </summary>
        public bool EmpiezaAhora;
    }

    /// <summary>Todo lo de una configuracion, leido de una vez (CicloPreliminarDatos.Leer).</summary>
    public class CicloLeido
    {
        public CicloPreliminar Ciclo;
        /// <summary>Hoy segun el servidor.</summary>
        public DateTime Hoy;
        /// <summary>Por docente (sin docente primero, luego apellido paterno y nombre), materia y grupo.</summary>
        public List<CargaCiclo> Cargas = new List<CargaCiclo>();
        /// <summary>Las personas docentes de sus clases, por apellido paterno y nombre.</summary>
        public List<ProfesorDelCiclo> Profesores = new List<ProfesorDelCiclo>();
        /// <summary>Para despues (punto 8): hoy siempre vacia.</summary>
        public List<ClaseNoCopiada> NoCopiadas = new List<ClaseNoCopiada>();
        /// <summary>
        /// Solo con fechas (Leer con DatosDeFechas): franjas de clases que ya estan
        /// en Clases y tambien valen en esas fechas. Ocupan su hora: cuentan en los choques.
        /// </summary>
        public List<FranjaInfo> YaProgramadas = new List<FranjaInfo>();
        /// <summary>Las fechas con que se leyo; null sin fechas.</summary>
        public DatosDeFechas Fechas;
        /// <summary>La base guarda bajas de docentes (db/08): una persona docente de baja no deja aplicar.</summary>
        public bool ConBajas;
        /// <summary>La base guarda laboratorios desactivados (db/08).</summary>
        public bool ConLaboratoriosDesactivables;
        /// <summary>
        /// "Falta asignar": lo que le falta a cada una de sus clases, de lo mas
        /// antiguo a lo mas nuevo (solo de clases que siguen en ella).
        /// </summary>
        public List<PorAsignarCiclo> PorAsignar = new List<PorAsignarCiclo>();

        /// <summary>Todo lo que falta asignar, en minutos.</summary>
        public int MinutosPorAsignar
        {
            get { return PorAsignar.Sum(p => p.Minutos); }
        }

        public ProfesorDelCiclo Profesor(int id)
        {
            return Profesores.FirstOrDefault(p => p.Id == id);
        }

        public CargaCiclo Carga(int id)
        {
            return Cargas.FirstOrDefault(c => c.Id == id);
        }
    }

    /// <summary>Que hay en las pestanas Configuraciones y Aplicacion al entrar (CicloPreliminarDatos.LeerSituacion).</summary>
    public class SituacionConfiguraciones
    {
        /// <summary>Hoy segun el servidor.</summary>
        public DateTime Hoy;
        /// <summary>Falta db/16 (o esta cuenta no ve sus tablas).</summary>
        public bool SinTablas;
        /// <summary>Falta db/23 (o esta cuenta no ve sus columnas: digbit_consulta sin su permiso).</summary>
        public bool SinGuardados;
        /// <summary>Falta db/09: sin un codigo por docente no se sabe que codigo dar a las clases nuevas.</summary>
        public bool SinCodigoPorProfesor;
        /// <summary>
        /// La que esta en preparacion, o null. Siempre null en solo consulta: la
        /// persona responsable del laboratorio no la ve.
        /// </summary>
        public CicloPreliminar Borrador;
        /// <summary>Una aplicada que todavia no empieza (desde despues de hoy), o null.</summary>
        public CicloPreliminar PorEmpezar;
        /// <summary>Las guardadas y las aplicadas, de la mas reciente a la mas vieja (COALESCE(publicado_en, cambiado_en, creado_en)).</summary>
        public List<CicloPreliminar> Configuraciones = new List<CicloPreliminar>();

        /// <summary>Falta alguna de las tres (16, 23 o 09): la tarjeta "Falta preparar la base".</summary>
        public bool FaltaBase
        {
            get { return SinTablas || SinGuardados || SinCodigoPorProfesor; }
        }

        public List<CicloPreliminar> Guardadas
        {
            get { return Configuraciones.Where(c => c.Guardada).ToList(); }
        }

        public List<CicloPreliminar> Aplicadas
        {
            get { return Configuraciones.Where(c => c.Aplicada).ToList(); }
        }
    }

    /// <summary>Una franja (o una clase sin franjas) de las que se copiarian: para contar antes de guardar o preparar.</summary>
    public class OrigenDeClase
    {
        public int ClaseId;
        public int ProfesorId;
        /// <summary>La vigencia de la clase: con la mas temprana y la mas tardia se propone el nombre (NombrePropuesto).</summary>
        public DateTime VigenteDesde;
        public DateTime VigenteHasta;
        /// <summary>Null: la clase no tiene horario en ningun laboratorio.</summary>
        public int? LaboratorioId;
        public double Horas;
    }

    /// <summary>Lo que quedo en una configuracion al guardarla o prepararla.</summary>
    public class ResultadoEmpezar
    {
        public int CicloId;
        public string Nombre = "";
        public int Clases;
        public int Docentes;
        public int Horarios;
    }

    /// <summary>
    /// De donde empieza el horario en preparacion (CicloPreliminarDatos.Preparar):
    /// las clases que se dan un dia, una configuracion guardada o aplicada, o en blanco.
    /// </summary>
    public class FuentePreparacion
    {
        private FuentePreparacion()
        {
        }

        /// <summary>El dia cuyas clases se copian; null si no es de un dia.</summary>
        public DateTime? Dia { get; private set; }
        /// <summary>Con Dia: los laboratorios cuyas horas se copian (null = todos).</summary>
        public ICollection<int> Laboratorios { get; private set; }
        /// <summary>Con Dia: tambien las clases sin horario en ningun laboratorio.</summary>
        public bool SinHorario { get; private set; }
        /// <summary>La configuracion guardada o aplicada que se copia; null si no es de una.</summary>
        public int? ConfiguracionId { get; private set; }
        /// <summary>
        /// Con ConfiguracionId: solo sus clases (docente, materia y grupo), sin sus
        /// horas, para armar el horario de cero ("Crear plantilla nueva...").
        /// </summary>
        public bool SoloClases { get; private set; }

        public bool EsEnBlanco
        {
            get { return !Dia.HasValue && !ConfiguracionId.HasValue; }
        }

        public static FuentePreparacion DelDia(DateTime dia, ICollection<int> laboratorios, bool sinHorario)
        {
            return new FuentePreparacion { Dia = dia.Date, Laboratorios = laboratorios, SinHorario = sinHorario };
        }

        public static FuentePreparacion DeConfiguracion(int idConfiguracion)
        {
            return new FuentePreparacion { ConfiguracionId = idConfiguracion };
        }

        public static FuentePreparacion ClasesDeConfiguracion(int idConfiguracion)
        {
            return new FuentePreparacion { ConfiguracionId = idConfiguracion, SoloClases = true };
        }

        public static FuentePreparacion EnBlanco()
        {
            return new FuentePreparacion();
        }
    }

    /// <summary>
    /// Las configuraciones de horario (Horarios > Configuraciones y Aplicacion,
    /// db/16 y db/23): guardar el horario de un dia, preparar uno (de un dia, de
    /// una configuracion o en blanco), editarlo, guardarlo, renombrar y borrar.
    /// Nada de esto toca clases, horarios, excepciones, sesiones, bitacoras ni
    /// asistencias: solo las tablas de la 16 (regla R1). Aplicar y Deshacer van
    /// en CicloPreliminarDatos.Aplicar.cs. Cada escritura va en una transaccion,
    /// empieza con Permisos.Exigir (solo la administracion escribe) y, si toca
    /// una configuracion, la bloquea primero (FOR UPDATE) y se niega si ya no es
    /// lo que era. Hoy es CURDATE() del servidor (zona de la conexion).
    /// </summary>
    internal partial class CicloPreliminarDatos
    {
        public const int LargoNombre = 60;
        /// <summary>El nombre que se propone al preparar un horario.</summary>
        public const string NombrePreparacion = "Próximo ciclo";
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

        /// <summary>
        /// ciclo_cargas.quitada = 2: el renglon no es una clase, sino unas horas
        /// que le faltan a otra de la misma plantilla (su carga_origen): "Falta
        /// asignar" sin migracion (2026-11-03). Lleva el docente, el grupo y la
        /// materia de esa clase (las claves foraneas) y, en horario_origen,
        /// "motivo|minutos|laboratorio|dia|HH:mm|HH:mm" (Cifrar). Todo lo demas
        /// mira quitada = 0 (en la v2 Quitar borra la fila: el 1 ya no se usa),
        /// asi que no se cuenta, no se copia a otra plantilla (Guardar como,
        /// Cargar como base) ni se aplica como clase. Al aplicar, pasa tambien a
        /// horas_por_asignar de la clase nueva (db/25: Falta asignar de Horario
        /// semanal); sigue con la plantilla y vuelve si se deshace. Se borra al
        /// ponerle sus horas a la clase (AsignarPorAsignar), con "Ya no hace
        /// falta" o al quitar la clase.
        /// </summary>
        internal const int QuitadaPorAsignar = 2;

        private readonly Conexion mconexion = new Conexion();

        // ---------------------------------------------------------------------
        // Al entrar a las pestanas
        // ---------------------------------------------------------------------

        /// <summary>
        /// Si la base tiene lo necesario (16, 23 y 09), la que esta en
        /// preparacion (nunca en solo consulta), la aplicada por empezar y la
        /// lista de las guardadas y las aplicadas.
        /// </summary>
        public SituacionConfiguraciones LeerSituacion()
        {
            SituacionConfiguraciones situacion = new SituacionConfiguraciones();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                situacion.Hoy = Hoy(conexion, null);
                situacion.SinTablas = !Esquema.CicloPreliminar(conexion);
                situacion.SinGuardados = situacion.SinTablas || !Esquema.HorariosGuardados(conexion);
                situacion.SinCodigoPorProfesor = !Esquema.CodigoPorProfesor(conexion);
                if (situacion.SinTablas || situacion.SinGuardados)
                {
                    return situacion;
                }

                List<CicloPreliminar> todas = LeerCiclos(conexion, null, "", null);
                MarcarCortadas(todas);
                situacion.Borrador = Permisos.SoloConsulta ? null : todas.FirstOrDefault(c => c.EnPreparacion);
                situacion.PorEmpezar = todas.Where(c => c.PorEmpezar(situacion.Hoy)).OrderBy(c => c.Desde).FirstOrDefault();
                situacion.Configuraciones = todas.Where(c => !c.EnPreparacion).ToList();
            }

            return situacion;
        }

        /// <summary>
        /// A cada aplicada, el dia en que la corto la aplicada siguiente (su
        /// fin_anterior), si fue antes de su Hasta y no antes de su Desde: dos
        /// aplicadas "Ahora mismo" el mismo dia no se recortan (la de la manana
        /// empezo hoy y el fin de la otra es ayer).
        /// </summary>
        private static void MarcarCortadas(List<CicloPreliminar> ciclos)
        {
            List<CicloPreliminar> aplicadas = ciclos.Where(c => c.Aplicada && c.Desde.HasValue).OrderBy(c => c.Desde).ThenBy(c => c.Id).ToList();
            for (int i = 0; i + 1 < aplicadas.Count; i++)
            {
                CicloPreliminar siguiente = aplicadas[i + 1];
                CicloPreliminar esta = aplicadas[i];
                if (siguiente.FinAnterior.HasValue && esta.Hasta.HasValue && siguiente.FinAnterior.Value < esta.Hasta.Value
                    && siguiente.FinAnterior.Value >= esta.Desde.Value)
                {
                    esta.CortadaEl = siguiente.FinAnterior;
                }
            }
        }

        private static DateTime Hoy(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT CURDATE()", conexion, transaccion))
            {
                return Convert.ToDateTime(comando.ExecuteScalar()).Date;
            }
        }

        /// <summary>
        /// Las configuraciones (todas, o las de la condicion: "WHERE cp.idciclo = @C"),
        /// de la mas reciente a la mas vieja, con su origen, quien la cambio y sus
        /// cuentas. Necesita db/23.
        /// </summary>
        private static List<CicloPreliminar> LeerCiclos(MySqlConnection conexion, MySqlTransaction transaccion, string condicion, Action<MySqlCommand> parametros)
        {
            List<CicloPreliminar> ciclos = new List<CicloPreliminar>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT cp.idciclo, cp.nombre, cp.desde, cp.hasta, cp.copiado_del, cp.estado, cp.creado_en,
                       cp.cambiado_en, uc.numero_identificador AS cambiado_por,
                       cp.origen_ciclo, o.nombre AS origen_nombre, o.estado AS origen_estado,
                       cp.fin_anterior, cp.publicado_en, up.numero_identificador AS publicado_por,
                       (SELECT COUNT(*) FROM ciclo_cargas cc WHERE cc.idciclo = cp.idciclo AND cc.quitada = 0) AS n_clases,
                       (SELECT COUNT(DISTINCT cc.profesor) FROM ciclo_cargas cc WHERE cc.idciclo = cp.idciclo AND cc.quitada = 0) AS n_docentes,
                       (SELECT COUNT(*) FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                         WHERE cc.idciclo = cp.idciclo AND cc.quitada = 0) AS n_franjas,
                       (SELECT SUM(TIME_TO_SEC(TIMEDIFF(f.hora_fin, f.hora_inicio))) / 3600 FROM ciclo_franjas f
                         INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga WHERE cc.idciclo = cp.idciclo AND cc.quitada = 0) AS n_horas,
                       (SELECT COUNT(DISTINCT f.laboratorio) FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                         WHERE cc.idciclo = cp.idciclo AND cc.quitada = 0) AS n_laboratorios
                FROM ciclos_preliminares cp
                LEFT JOIN ciclos_preliminares o ON o.idciclo = cp.origen_ciclo
                LEFT JOIN usuarios uc ON uc.idusuarios = cp.cambiado_por
                LEFT JOIN usuarios up ON up.idusuarios = cp.publicado_por " + condicion + @"
                ORDER BY COALESCE(cp.publicado_en, cp.cambiado_en, cp.creado_en) DESC, cp.idciclo DESC", conexion, transaccion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string origen = Texto(reader, "origen_estado");
                        ciclos.Add(new CicloPreliminar
                        {
                            Id = reader.GetInt32("idciclo"),
                            Nombre = reader.GetString("nombre"),
                            Estado = EstadoDe(reader.GetString("estado")),
                            Desde = Fecha(reader, "desde"),
                            Hasta = Fecha(reader, "hasta"),
                            CopiadoDel = Fecha(reader, "copiado_del"),
                            CreadoEn = reader.GetDateTime("creado_en"),
                            CambiadoEn = FechaHora(reader, "cambiado_en"),
                            CambiadoPor = Texto(reader, "cambiado_por"),
                            OrigenId = Entero(reader, "origen_ciclo"),
                            OrigenNombre = Texto(reader, "origen_nombre"),
                            OrigenEstado = origen.Length == 0 ? (EstadoConfiguracion?)null : EstadoDe(origen),
                            FinAnterior = Fecha(reader, "fin_anterior"),
                            PublicadoEn = FechaHora(reader, "publicado_en"),
                            PublicadoPor = Texto(reader, "publicado_por"),
                            Clases = Convert.ToInt32(reader["n_clases"]),
                            Docentes = Convert.ToInt32(reader["n_docentes"]),
                            Horarios = Convert.ToInt32(reader["n_franjas"]),
                            Horas = reader.IsDBNull(reader.GetOrdinal("n_horas")) ? 0 : Convert.ToDouble(reader["n_horas"]),
                            Laboratorios = Convert.ToInt32(reader["n_laboratorios"])
                        });
                    }
                }
            }

            return ciclos;
        }

        /// <summary>'borrador', 'guardado' o 'publicado' (ciclos_preliminares.estado).</summary>
        internal static string EstadoEnBase(EstadoConfiguracion estado)
        {
            switch (estado)
            {
                case EstadoConfiguracion.Guardada:
                    return "guardado";
                case EstadoConfiguracion.Aplicada:
                    return "publicado";
                default:
                    return "borrador";
            }
        }

        internal static EstadoConfiguracion EstadoDe(string estado)
        {
            switch (estado)
            {
                case "guardado":
                    return EstadoConfiguracion.Guardada;
                case "publicado":
                    return EstadoConfiguracion.Aplicada;
                default:
                    return EstadoConfiguracion.EnPreparacion;
            }
        }

        // ---------------------------------------------------------------------
        // Antes de guardar o preparar: que se copiaria
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
                    SELECT c.idclases, c.usuarios_idusuarios, c.vigente_desde, c.vigente_hasta, h.laboratorios_idlaboratorios,
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
                                VigenteDesde = reader.GetDateTime("vigente_desde").Date,
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
        // Nombres (3.3)
        // ---------------------------------------------------------------------

        /// <summary>
        /// El error de un nombre que se puede ver sin la base ("Escribe un
        /// nombre.", "El nombre puede tener hasta 60 letras."), o null. Para los
        /// dialogos; los repetidos los dice la operacion al guardar.
        /// </summary>
        public static string ErrorDeNombre(string nombre)
        {
            string limpio = (nombre ?? "").Trim();
            if (limpio.Length == 0)
            {
                return "Escribe un nombre.";
            }

            return limpio.Length > LargoNombre ? "El nombre puede tener hasta " + LargoNombre + " letras." : null;
        }

        /// <summary>
        /// nombreBase, o "nombreBase (2)", "nombreBase (3)"... el primero que no
        /// tenga otra configuracion de esa clase (sin acentos ni mayusculas). En
        /// preparacion no se compara. Para las propuestas de los dialogos.
        /// </summary>
        public string NombreLibre(string nombreBase, EstadoConfiguracion estado)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return NombreLibre(conexion, null, nombreBase, estado);
            }
        }

        internal static string NombreLibre(MySqlConnection conexion, MySqlTransaction transaccion, string nombreBase, EstadoConfiguracion estado)
        {
            string limpio = (nombreBase ?? "").Trim();
            string candidato = Recortar(limpio, LargoNombre);
            if (estado == EstadoConfiguracion.EnPreparacion || candidato.Length == 0)
            {
                return candidato;
            }

            HashSet<string> usados = new HashSet<string>(NombresDe(conexion, transaccion, estado, 0, false).Select(n => ClaveDeNombre(n.Value)));
            for (int n = 2; usados.Contains(ClaveDeNombre(candidato)); n++)
            {
                string sufijo = " (" + n + ")";
                candidato = Recortar(limpio, LargoNombre - sufijo.Length).TrimEnd() + sufijo;
            }

            return candidato;
        }

        /// <summary>
        /// El nombre limpio, o ErrorDeDatos: vacio, largo, o (en una guardada o
        /// una aplicada) igual que otra de su clase. Con FOR UPDATE: dos que
        /// guardan a la vez el mismo nombre no pasan los dos.
        /// </summary>
        private static string ValidarNombre(MySqlConnection conexion, MySqlTransaction transaccion, string nombre, EstadoConfiguracion estado, int excluirId)
        {
            string error = ErrorDeNombre(nombre);
            if (error != null)
            {
                throw new ErrorDeDatos(error);
            }

            string limpio = nombre.Trim();
            if (estado == EstadoConfiguracion.EnPreparacion)
            {
                return limpio;
            }

            string clave = ClaveDeNombre(limpio);
            foreach (KeyValuePair<int, string> otra in NombresDe(conexion, transaccion, estado, excluirId, true))
            {
                if (ClaveDeNombre(otra.Value) == clave)
                {
                    throw new ErrorDeDatos("Ya hay una plantilla " + (estado == EstadoConfiguracion.Guardada ? "guardada" : "aplicada")
                        + " que se llama «" + otra.Value + "»: usa otro nombre.");
                }
            }

            return limpio;
        }

        /// <summary>Los nombres de las configuraciones de esa clase (menos excluirId), por id.</summary>
        private static List<KeyValuePair<int, string>> NombresDe(MySqlConnection conexion, MySqlTransaction transaccion, EstadoConfiguracion estado,
            int excluirId, bool bloquear)
        {
            List<KeyValuePair<int, string>> nombres = new List<KeyValuePair<int, string>>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idciclo, nombre FROM ciclos_preliminares WHERE estado = @Estado AND idciclo <> @Excluir" + (bloquear ? " FOR UPDATE" : ""),
                conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Estado", EstadoEnBase(estado));
                comando.Parameters.AddWithValue("@Excluir", excluirId);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        nombres.Add(new KeyValuePair<int, string>(reader.GetInt32("idciclo"), reader.GetString("nombre")));
                    }
                }
            }

            return nombres;
        }

        /// <summary>Para comparar nombres: sin acentos, sin mayusculas y con un solo espacio entre palabras.</summary>
        private static string ClaveDeNombre(string nombre)
        {
            return Regex.Replace(EstiloAdmin.Normalizar((nombre ?? "").Trim()), @"\s+", " ");
        }

        private static string Recortar(string texto, int largo)
        {
            return texto.Length <= largo ? texto : texto.Substring(0, Math.Max(0, largo));
        }

        // ---------------------------------------------------------------------
        // Guardar un dia, preparar, guardar, renombrar, borrar y descartar
        // ---------------------------------------------------------------------

        /// <summary>
        /// "Guardar el horario de hoy": una configuracion guardada con las clases
        /// que se dan ese dia, con sus horas en esos laboratorios (y, con
        /// sinHorario, las que no tienen ninguna). Cada una con su docente, aunque
        /// este de baja: es copia fiel del horario de ese dia. Nada cambia en los
        /// laboratorios.
        /// </summary>
        public ResultadoEmpezar GuardarDia(string nombre, DateTime dia, ICollection<int> laboratorios, bool sinHorario, int idAdministrador)
        {
            Permisos.Exigir("Guardar el horario de un dia");
            ExigirLaboratorios(laboratorios, sinHorario);
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                nombre = ValidarNombre(conexion, transaccion, nombre, EstadoConfiguracion.Guardada, 0);
                int idCiclo = InsertarCiclo(conexion, transaccion, nombre, EstadoConfiguracion.Guardada, dia.Date, null, idAdministrador);
                if (CopiarClases(conexion, transaccion, idCiclo, dia, null, laboratorios, sinHorario) == 0)
                {
                    throw new ErrorDeDatos("Con esos laboratorios no hay clases que guardar ese día.");
                }

                ResultadoEmpezar resultado = Contar(conexion, transaccion, idCiclo, nombre);
                transaccion.Commit();
                Log.Info("Configuracion de horario " + idCiclo + " guardada (" + nombre + "): las clases del " + dia.ToString("yyyy-MM-dd")
                    + " en los laboratorios " + (laboratorios == null ? "todos" : string.Join(",", laboratorios)) + (sinHorario ? " y las sin horario" : "")
                    + ": " + resultado.Clases + " clases de " + resultado.Docentes + " docentes con " + resultado.Horarios + " horarios.");
                return resultado;
            }
        }

        /// <summary>
        /// Empieza el horario en preparacion, como copia de las clases de un dia,
        /// de una configuracion guardada o aplicada, o en blanco. No pide fechas
        /// (se ponen en Aplicacion). Si ya hay uno en preparacion: con reemplazar
        /// se descarta; sin reemplazar, ErrorDeDatos. Con una aplicada que todavia
        /// no empieza, ErrorDeDatos (hay que deshacerla antes).
        /// </summary>
        public ResultadoEmpezar Preparar(string nombre, FuentePreparacion fuente, bool reemplazar, int idAdministrador)
        {
            Permisos.Exigir("Preparar un horario");
            if (fuente == null)
            {
                throw new ArgumentNullException("fuente");
            }

            if (fuente.Dia.HasValue)
            {
                ExigirLaboratorios(fuente.Laboratorios, fuente.SinHorario);
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                nombre = ValidarNombre(conexion, transaccion, nombre, EstadoConfiguracion.EnPreparacion, 0);

                // Nadie mas prepara otro a la vez: el segundo espera aqui y luego ve el primero.
                List<CicloPreliminar> estorban = new List<CicloPreliminar>();
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT idciclo, nombre, estado FROM ciclos_preliminares
                     WHERE estado = 'borrador' OR (estado = 'publicado' AND desde > CURDATE())
                     ORDER BY idciclo FOR UPDATE", conexion, transaccion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        estorban.Add(new CicloPreliminar
                        {
                            Id = reader.GetInt32("idciclo"),
                            Nombre = reader.GetString("nombre"),
                            Estado = EstadoDe(reader.GetString("estado"))
                        });
                    }
                }

                CicloPreliminar porEmpezar = estorban.FirstOrDefault(c => c.Aplicada);
                if (porEmpezar != null)
                {
                    throw new ErrorDeDatos("«" + porEmpezar.Nombre + "» ya está aplicado y todavía no empieza: para preparar otro, deshaz su aplicación en Próximo ciclo, «3. Aplicar con fechas».");
                }

                if (estorban.Count > 0 && !reemplazar)
                {
                    throw new ErrorDeDatos("Ya hay un horario en preparación: vuelve a entrar a la pestaña.");
                }

                string origenNombre = "";
                if (fuente.ConfiguracionId.HasValue)
                {
                    string estado = EstadoBloqueado(conexion, transaccion, fuente.ConfiguracionId.Value);
                    if (estado != "guardado" && estado != "publicado")
                    {
                        throw new ErrorDeDatos("La plantilla que elegiste ya no está: vuelve a entrar a la pestaña.");
                    }

                    origenNombre = NombreDe(conexion, transaccion, fuente.ConfiguracionId.Value);
                }

                foreach (CicloPreliminar anterior in estorban)
                {
                    using (MySqlCommand comando = new MySqlCommand(
                        "DELETE FROM ciclos_preliminares WHERE idciclo = @C AND estado = 'borrador'", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@C", anterior.Id);
                        comando.ExecuteNonQuery();
                    }
                }

                int idCiclo = InsertarCiclo(conexion, transaccion, nombre, EstadoConfiguracion.EnPreparacion, fuente.Dia, fuente.ConfiguracionId, idAdministrador);
                if (fuente.Dia.HasValue)
                {
                    if (CopiarClases(conexion, transaccion, idCiclo, fuente.Dia.Value, null, fuente.Laboratorios, fuente.SinHorario) == 0)
                    {
                        throw new ErrorDeDatos("Con esos laboratorios no hay clases que copiar ese día.");
                    }
                }
                else if (fuente.ConfiguracionId.HasValue)
                {
                    CopiarCiclo(conexion, transaccion, fuente.ConfiguracionId.Value, idCiclo, !fuente.SoloClases);
                }

                ResultadoEmpezar resultado = Contar(conexion, transaccion, idCiclo, nombre);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo + " (" + nombre + ")"
                    + (estorban.Count > 0 ? ", en lugar del " + string.Join(",", estorban.Select(c => c.Id)) : "")
                    + (fuente.Dia.HasValue
                        ? ": copia de las clases del " + fuente.Dia.Value.ToString("yyyy-MM-dd") + " en los laboratorios "
                          + (fuente.Laboratorios == null ? "todos" : string.Join(",", fuente.Laboratorios)) + (fuente.SinHorario ? " y las sin horario" : "")
                        : fuente.ConfiguracionId.HasValue ? (fuente.SoloClases ? ": las clases, sin horas, de la configuracion " : ": copia de la configuracion ")
                          + fuente.ConfiguracionId.Value + " (" + origenNombre + ")"
                        : ": en blanco")
                    + "; " + resultado.Clases + " clases de " + resultado.Docentes + " docentes con " + resultado.Horarios + " horarios.");
                return resultado;
            }
        }

        /// <summary>
        /// "Guardar como...": una guardada nueva con ese nombre y el contenido del
        /// horario en preparacion, que desde ahora se guarda en ella con Guardar.
        /// </summary>
        public ResultadoEmpezar GuardarComo(int idBorrador, string nombre, int idAdministrador)
        {
            Permisos.Exigir("Guardar como una configuracion nueva");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idBorrador, EstadoConfiguracion.EnPreparacion);
                nombre = ValidarNombre(conexion, transaccion, nombre, EstadoConfiguracion.Guardada, 0);
                DateTime? copiado;
                using (MySqlCommand comando = new MySqlCommand("SELECT copiado_del FROM ciclos_preliminares WHERE idciclo = @C", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idBorrador);
                    object valor = comando.ExecuteScalar();
                    copiado = valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor).Date;
                }

                int idNueva = InsertarCiclo(conexion, transaccion, nombre, EstadoConfiguracion.Guardada, copiado, null, idAdministrador);
                CopiarCiclo(conexion, transaccion, idBorrador, idNueva);
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE ciclos_preliminares SET origen_ciclo = @Nueva WHERE idciclo = @C", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idBorrador);
                    comando.Parameters.AddWithValue("@Nueva", idNueva);
                    comando.ExecuteNonQuery();
                }

                RenovarOrigenes(conexion, transaccion, idBorrador);
                ResultadoEmpezar resultado = Contar(conexion, transaccion, idNueva, nombre);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idBorrador + " guardado como " + idNueva + " (" + nombre + "): "
                    + resultado.Clases + " clases de " + resultado.Docentes + " docentes con " + resultado.Horarios + " horarios.");
                return resultado;
            }
        }

        /// <summary>
        /// "Guardar": reemplaza el contenido de la guardada de la que viene el
        /// horario en preparacion (su origen) con el de ahora. Si ya no hay donde
        /// guardarse (la guardada se borro, o viene de un dia o de una aplicada),
        /// ErrorDeDatos: la pantalla usa GuardarComo (CicloPreliminar.SeGuardaEnOrigen).
        /// </summary>
        public ResultadoEmpezar GuardarEn(int idBorrador, int idAdministrador)
        {
            Permisos.Exigir("Guardar el horario en preparacion");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idBorrador, EstadoConfiguracion.EnPreparacion);
                int? idGuardada;
                using (MySqlCommand comando = new MySqlCommand("SELECT origen_ciclo FROM ciclos_preliminares WHERE idciclo = @C", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idBorrador);
                    object valor = comando.ExecuteScalar();
                    idGuardada = valor == null || valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor);
                }

                if (!idGuardada.HasValue || EstadoBloqueado(conexion, transaccion, idGuardada.Value) != "guardado")
                {
                    throw new ErrorDeDatos("Este horario no tiene una plantilla guardada donde guardarse (se borró, o viene de un día o de una aplicada): usa «Guardar como plantilla nueva...».");
                }

                using (MySqlCommand comando = new MySqlCommand("DELETE FROM ciclo_cargas WHERE idciclo = @G", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@G", idGuardada.Value);
                    comando.ExecuteNonQuery();
                }

                CopiarCiclo(conexion, transaccion, idBorrador, idGuardada.Value);
                Tocar(conexion, transaccion, idGuardada.Value, idAdministrador);
                RenovarOrigenes(conexion, transaccion, idBorrador);
                ResultadoEmpezar resultado = Contar(conexion, transaccion, idGuardada.Value, NombreDe(conexion, transaccion, idGuardada.Value));
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idBorrador + " guardado en " + idGuardada.Value + " (" + resultado.Nombre + "): "
                    + resultado.Clases + " clases de " + resultado.Docentes + " docentes con " + resultado.Horarios + " horarios.");
                return resultado;
            }
        }

        /// <summary>
        /// Cambia el nombre de una configuracion de cualquier clase (en una
        /// aplicada, su nombre en el registro), con las reglas de 3.3.
        /// </summary>
        public void Renombrar(int idCiclo, string nombre, int idAdministrador)
        {
            Permisos.Exigir("Renombrar una configuracion de horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                string estado = EstadoBloqueado(conexion, transaccion, idCiclo);
                if (estado == null)
                {
                    throw new ErrorDeDatos("Esa plantilla ya no está: vuelve a entrar a la pestaña.");
                }

                EstadoConfiguracion clase = EstadoDe(estado);
                nombre = ValidarNombre(conexion, transaccion, nombre, clase, idCiclo);
                using (MySqlCommand comando = new MySqlCommand("UPDATE ciclos_preliminares SET nombre = @Nombre WHERE idciclo = @C", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.Parameters.AddWithValue("@Nombre", nombre);
                    comando.ExecuteNonQuery();
                }

                if (clase == EstadoConfiguracion.EnPreparacion)
                {
                    Tocar(conexion, transaccion, idCiclo, idAdministrador);
                }

                transaccion.Commit();
                Log.Info("Configuracion de horario " + idCiclo + " (" + estado + ") renombrada: " + nombre + ".");
            }
        }

        /// <summary>Borra una guardada con sus clases. Las aplicadas quedan como registro y la que se prepara se descarta (Descartar).</summary>
        public void Borrar(int idGuardada)
        {
            Permisos.Exigir("Borrar una configuracion guardada");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                string estado = EstadoBloqueado(conexion, transaccion, idGuardada);
                if (estado == null)
                {
                    throw new ErrorDeDatos("Esa plantilla ya no está: vuelve a entrar a la pestaña.");
                }

                if (estado == "borrador")
                {
                    throw new ErrorDeDatos("Solo se borran las guardadas: el horario en preparación se descarta con «Descartar...».");
                }

                int borradas;
                using (MySqlCommand comando = new MySqlCommand(
                    "DELETE FROM ciclos_preliminares WHERE idciclo = @C AND estado = 'guardado'", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idGuardada);
                    borradas = comando.ExecuteNonQuery();
                }

                if (borradas != 1)
                {
                    throw new ErrorDeDatos("Solo se borran las guardadas: las aplicadas quedan como registro.");
                }

                transaccion.Commit();
                Log.Info("Configuracion de horario guardada " + idGuardada + " borrada.");
            }
        }

        /// <summary>Borra el horario en preparacion entero (sus clases y franjas se van con el). Las guardadas y el horario que se usa no cambian.</summary>
        public void Descartar(int idCiclo)
        {
            Permisos.Exigir("Descartar el horario en preparacion");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idCiclo, EstadoConfiguracion.EnPreparacion);
                using (MySqlCommand comando = new MySqlCommand(
                    "DELETE FROM ciclos_preliminares WHERE idciclo = @C AND estado = 'borrador'", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.ExecuteNonQuery();
                }

                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo + " descartado.");
            }
        }

        /// <summary>
        /// Las fechas propuestas del horario en preparacion (Aplicacion las guarda
        /// al salir de cada fecha). Solo pide hasta >= desde si estan las dos: lo
        /// demas se revisa al aplicar.
        /// </summary>
        public void GuardarFechas(int idCiclo, DateTime? desde, DateTime? hasta, int idAdministrador)
        {
            Permisos.Exigir("Guardar las fechas del horario en preparacion");
            if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            {
                throw new ErrorDeDatos("El último día del nuevo horario no puede ser antes del primero.");
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idCiclo, EstadoConfiguracion.EnPreparacion);
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE ciclos_preliminares SET desde = @Desde, hasta = @Hasta WHERE idciclo = @C", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.Parameters.AddWithValue("@Desde", desde.HasValue ? (object)desde.Value.Date : DBNull.Value);
                    comando.Parameters.AddWithValue("@Hasta", hasta.HasValue ? (object)hasta.Value.Date : DBNull.Value);
                    comando.ExecuteNonQuery();
                }

                Tocar(conexion, transaccion, idCiclo, idAdministrador);
                transaccion.Commit();
            }
        }

        private static void ExigirLaboratorios(ICollection<int> laboratorios, bool sinHorario)
        {
            if (laboratorios != null && laboratorios.Count == 0 && !sinHorario)
            {
                throw new ErrorDeDatos("Elige al menos un laboratorio.");
            }
        }

        /// <summary>La fila nueva de ciclos_preliminares (sin fechas) y su id.</summary>
        private static int InsertarCiclo(MySqlConnection conexion, MySqlTransaction transaccion, string nombre, EstadoConfiguracion estado,
            DateTime? copiadoDel, int? origen, int idAdministrador)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO ciclos_preliminares (nombre, desde, hasta, copiado_del, origen_ciclo, estado, creado_por, creado_en, cambiado_en, cambiado_por)
                VALUES (@Nombre, NULL, NULL, @Copiado, @Origen, @Estado, @Admin, NOW(), NOW(), @Admin);
                SELECT LAST_INSERT_ID();", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Nombre", nombre);
                comando.Parameters.AddWithValue("@Copiado", copiadoDel.HasValue ? (object)copiadoDel.Value.Date : DBNull.Value);
                comando.Parameters.AddWithValue("@Origen", origen.HasValue ? (object)origen.Value : DBNull.Value);
                comando.Parameters.AddWithValue("@Estado", EstadoEnBase(estado));
                comando.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        /// <summary>El ultimo cambio: al final de cada escritura en preparacion y de GuardarEn.</summary>
        private static void Tocar(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, int idAdministrador)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "UPDATE ciclos_preliminares SET cambiado_en = NOW(), cambiado_por = @Admin WHERE idciclo = @C", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>Clases (sin quitar), docentes distintos y franjas de una configuracion.</summary>
        private static ResultadoEmpezar Contar(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, string nombre)
        {
            ResultadoEmpezar resultado = new ResultadoEmpezar { CicloId = idCiclo, Nombre = nombre ?? "" };
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT COUNT(*), COUNT(DISTINCT profesor) FROM ciclo_cargas WHERE idciclo = @C AND quitada = 0", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        resultado.Clases = Convert.ToInt32(reader.GetValue(0));
                        resultado.Docentes = Convert.ToInt32(reader.GetValue(1));
                    }
                }
            }

            resultado.Horarios = ContarFranjas(conexion, transaccion, idCiclo);
            return resultado;
        }

        private static string NombreDe(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT nombre FROM ciclos_preliminares WHERE idciclo = @C", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? "" : valor.ToString();
            }
        }

        /// <summary>
        /// Copia al ciclo las clases que valen ese dia (o solo esas, con clases) con
        /// sus franjas en esos laboratorios (null = en todos, tambien los
        /// desactivados). Cada una con su docente, aunque este de baja (la revision
        /// lo marca). Devuelve cuantas clases entraron.
        /// </summary>
        private static int CopiarClases(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, DateTime dia,
            ICollection<int> clases, ICollection<int> laboratorios, bool sinHorario)
        {
            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.Parameters.AddWithValue("@Dia", dia.Date);
                comando.Parameters.AddWithValue("@SinHorario", sinHorario ? 1 : 0);
                string enLabs = laboratorios == null ? "" : " AND h.laboratorios_idlaboratorios IN (" + Lista(comando, "l", laboratorios) + ")";
                string soloEstas = clases == null ? "" : " AND c.idclases IN (" + Lista(comando, "k", clases) + ")";
                // Solo las franjas de la clase como estaba ese dia (db/20): ni las ya
                // terminadas ni las que la sustituyen despues.
                string vigente = FranjaDeEseDia(Esquema.VigenciaDeFranjas(conexion, transaccion), "@Dia");
                comando.CommandText = @"
                    INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada, clase_origen,
                                              profesor_origen, grupo_origen, materia_origen, horario_origen)
                    SELECT @C, c.usuarios_idusuarios, c.grupos_idgrupos, c.materias_id_materia, 0, c.idclases,
                           c.usuarios_idusuarios, c.grupos_idgrupos, c.materias_id_materia,
                           (SELECT GROUP_CONCAT(CONCAT(h.laboratorios_idlaboratorios, ':', h.dia_semana, ':',
                                   TIME_FORMAT(h.hora_inicio, '%H:%i'), '-', TIME_FORMAT(h.hora_fin, '%H:%i'))
                                   ORDER BY h.dia_semana, h.hora_inicio, h.laboratorios_idlaboratorios SEPARATOR ';')
                              FROM horarios h WHERE h.clases_idclases = c.idclases" + enLabs + @"
                               AND " + vigente + @")
                    FROM clases c
                    WHERE c.activa = 1" + (clases == null ? " AND @Dia BETWEEN c.vigente_desde AND c.vigente_hasta" : "") + soloEstas + @"
                      AND (EXISTS (SELECT 1 FROM horarios h WHERE h.clases_idclases = c.idclases" + enLabs + @"
                                   AND " + vigente + @")
                           OR (@SinHorario = 1 AND NOT EXISTS (SELECT 1 FROM horarios h WHERE h.clases_idclases = c.idclases AND " + vigente + @")))
                      AND NOT EXISTS (SELECT 1 FROM ciclo_cargas x WHERE x.idciclo = @C AND x.clase_origen = c.idclases)
                    ORDER BY c.idclases;
                    SELECT ROW_COUNT();";
                int materias = Convert.ToInt32(comando.ExecuteScalar());

                comando.CommandText = @"
                    INSERT INTO ciclo_franjas (idcarga, laboratorio, dia_semana, hora_inicio, hora_fin)
                    SELECT cc.idcarga, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin
                    FROM ciclo_cargas cc
                    INNER JOIN horarios h ON h.clases_idclases = cc.clase_origen
                    INNER JOIN clases c   ON c.idclases = h.clases_idclases
                    WHERE cc.idciclo = @C" + enLabs + " AND " + vigente + @"
                      AND NOT EXISTS (SELECT 1 FROM ciclo_franjas f WHERE f.idcarga = cc.idcarga)" + (clases == null ? "" : " AND cc.clase_origen IN (" + Lista(comando, "m", clases) + ")");
                comando.ExecuteNonQuery();
                return materias;
            }
        }

        /// <summary>
        /// Copia todas las clases de una configuracion (sin quitar) con sus
        /// franjas en otra que esta vacia. Cada copia recuerda como era en la de
        /// origen (para "Como queda") y de que clase de ella viene (carga_origen).
        /// De una aplicada, clase_origen es la clase que creo.
        /// </summary>
        /// <param name="conHoras">
        /// False ("Crear plantilla nueva..."): solo las clases, sin sus horas ni el
        /// horario de origen (todas salen "sin horario" y sus horas, al ponerlas, nuevas).
        /// </param>
        private static void CopiarCiclo(MySqlConnection conexion, MySqlTransaction transaccion, int idOrigen, int idDestino, bool conHoras = true)
        {
            string horarioOrigen = conHoras
                ? @"(SELECT GROUP_CONCAT(CONCAT(f.laboratorio, ':', f.dia_semana, ':', TIME_FORMAT(f.hora_inicio, '%H:%i'), '-', TIME_FORMAT(f.hora_fin, '%H:%i'))
                               ORDER BY f.dia_semana, f.hora_inicio, f.laboratorio SEPARATOR ';') FROM ciclo_franjas f WHERE f.idcarga = o.idcarga)"
                : "NULL";
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada, clase_origen, profesor_origen, grupo_origen,
                                          materia_origen, horario_origen, carga_origen)
                SELECT @Destino, o.profesor, o.grupo, o.materia, 0, COALESCE(o.clase_publicada, o.clase_origen), o.profesor, o.grupo, o.materia,
                       " + horarioOrigen + @",
                       o.idcarga
                FROM ciclo_cargas o WHERE o.idciclo = @Origen AND o.quitada = 0 ORDER BY o.idcarga;"
                + (conHoras ? @"
                INSERT INTO ciclo_franjas (idcarga, laboratorio, dia_semana, hora_inicio, hora_fin)
                SELECT n.idcarga, f.laboratorio, f.dia_semana, f.hora_inicio, f.hora_fin
                FROM ciclo_cargas n INNER JOIN ciclo_franjas f ON f.idcarga = n.carga_origen
                WHERE n.idciclo = @Destino
                ORDER BY n.idcarga, f.idfranja;" : ""), conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Origen", idOrigen);
                comando.Parameters.AddWithValue("@Destino", idDestino);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Al guardar (Guardar y Guardar como): las clases del horario en
        /// preparacion quedan comparadas con lo que se guardo, que es su origen
        /// desde ahora ("Frente a <<2026-1>>", CicloPreliminar.FrenteA). Lo que
        /// falta asignar (QuitadaPorAsignar) no se toca: su horario_origen es lo que falta.
        /// </summary>
        private static void RenovarOrigenes(MySqlConnection conexion, MySqlTransaction transaccion, int idBorrador)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                UPDATE ciclo_cargas cc
                   SET cc.profesor_origen = cc.profesor, cc.grupo_origen = cc.grupo, cc.materia_origen = cc.materia,
                       cc.horario_origen = (SELECT GROUP_CONCAT(CONCAT(f.laboratorio, ':', f.dia_semana, ':', TIME_FORMAT(f.hora_inicio, '%H:%i'), '-',
                                                   TIME_FORMAT(f.hora_fin, '%H:%i'))
                                                   ORDER BY f.dia_semana, f.hora_inicio, f.laboratorio SEPARATOR ';')
                                              FROM ciclo_franjas f WHERE f.idcarga = cc.idcarga)
                 WHERE cc.idciclo = @C AND cc.quitada <> @PorAsignar", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idBorrador);
                comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                comando.ExecuteNonQuery();
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

        /// <summary>
        /// Al empezar cada escritura sobre una configuracion: la bloquea hasta el fin
        /// de la transaccion y comprueba que siga siendo de esa clase (otra ventana
        /// pudo aplicarla, guardarla o borrarla).
        /// </summary>
        private static void Bloquear(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, EstadoConfiguracion estadoEsperado)
        {
            if (EstadoBloqueado(conexion, transaccion, idCiclo) != EstadoEnBase(estadoEsperado))
            {
                throw new ErrorDeDatos("Esta plantilla ya se aplicó, se guardó o se borró: vuelve a entrar a la pestaña.");
            }
        }

        /// <summary>El estado de la configuracion ('borrador', 'guardado' o 'publicado'), bloqueandola; null si no existe.</summary>
        private static string EstadoBloqueado(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT estado FROM ciclos_preliminares WHERE idciclo = @C FOR UPDATE", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                object estado = comando.ExecuteScalar();
                return estado == null || estado == DBNull.Value ? null : estado.ToString();
            }
        }

        // ---------------------------------------------------------------------
        // Editar el horario en preparacion
        // ---------------------------------------------------------------------

        /// <summary>
        /// Una clase nueva en el horario en preparacion (profesor null: "Sin
        /// docente") con sus franjas. Lo que se cruza con ellas de otras clases
        /// del mismo laboratorio se les quita (RecortarEncimadas). Los choques
        /// (docente o grupo en otro laboratorio) no impiden guardarla: impiden
        /// aplicar. Devuelve su idcarga.
        /// </summary>
        public int AgregarClase(int idCiclo, int? profesor, int grupo, int materia, IList<FranjaCiclo> franjas, int idAdministrador)
        {
            Permisos.Exigir("Agregar una clase al horario en preparacion");
            franjas = franjas ?? new List<FranjaCiclo>();
            ValidarFranjas(franjas);
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idCiclo, EstadoConfiguracion.EnPreparacion);
                ExigirCatalogos(conexion, transaccion, profesor, grupo, materia, franjas);
                ExigirNoRepetida(conexion, transaccion, idCiclo, profesor, grupo, materia, 0);
                int idCarga;
                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada) VALUES (@C, @P, @G, @M, 0);
                    SELECT LAST_INSERT_ID();", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.Parameters.AddWithValue("@P", profesor.HasValue ? (object)profesor.Value : DBNull.Value);
                    comando.Parameters.AddWithValue("@G", grupo);
                    comando.Parameters.AddWithValue("@M", materia);
                    idCarga = Convert.ToInt32(comando.ExecuteScalar());
                }

                RecortarEncimadas(conexion, transaccion, idCiclo, idCarga, franjas, 0);
                InsertarFranjas(conexion, transaccion, idCarga, franjas);
                Tocar(conexion, transaccion, idCiclo, idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo + ": clase nueva " + idCarga + " (docente " + (profesor.HasValue ? profesor.Value.ToString() : "ninguno")
                    + ", grupo " + grupo + ", materia " + materia + ") con " + franjas.Count + " horarios.");
                return idCarga;
            }
        }

        /// <summary>
        /// Cambia el docente, el grupo, la materia y todas las franjas de una clase
        /// del horario en preparacion. Lo que se cruza con ellas de otras clases
        /// del mismo laboratorio se les quita (RecortarEncimadas).
        /// </summary>
        public void CambiarClase(int idCarga, int? profesor, int grupo, int materia, IList<FranjaCiclo> franjas, int idAdministrador)
        {
            Permisos.Exigir("Cambiar una clase del horario en preparacion");
            franjas = franjas ?? new List<FranjaCiclo>();
            ValidarFranjas(franjas);
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                int idCiclo = BloquearCarga(conexion, transaccion, idCarga);
                ExigirCatalogos(conexion, transaccion, profesor, grupo, materia, franjas);
                ExigirNoRepetida(conexion, transaccion, idCiclo, profesor, grupo, materia, idCarga);
                int minutosAntes = MinutosDeLaCarga(conexion, transaccion, idCarga);
                // Lo que le falta asignar sigue con ella: con su docente, grupo y materia nuevos (las claves foraneas).
                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE ciclo_cargas SET profesor = @P, grupo = @G, materia = @M WHERE idcarga = @Id;
                    UPDATE ciclo_cargas SET profesor = @P, grupo = @G, materia = @M
                     WHERE idciclo = @C AND quitada = @PorAsignar AND carga_origen = @Id;
                    DELETE FROM ciclo_franjas WHERE idcarga = @Id;", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", idCarga);
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                    comando.Parameters.AddWithValue("@P", profesor.HasValue ? (object)profesor.Value : DBNull.Value);
                    comando.Parameters.AddWithValue("@G", grupo);
                    comando.Parameters.AddWithValue("@M", materia);
                    comando.ExecuteNonQuery();
                }

                RecortarEncimadas(conexion, transaccion, idCiclo, idCarga, franjas, 0);
                InsertarFranjas(conexion, transaccion, idCarga, franjas);
                // Si gano tiempo, lo que le faltaba baja (como Horario semanal al cambiar horas).
                AsignarPorAsignar(conexion, transaccion, idCiclo, idCarga, MinutosDe(franjas) - minutosAntes);
                Tocar(conexion, transaccion, idCiclo, idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo + ": clase " + idCarga + " cambiada (docente " + (profesor.HasValue ? profesor.Value.ToString() : "ninguno")
                    + ", grupo " + grupo + ", materia " + materia + ") con " + franjas.Count + " horarios.");
            }
        }

        /// <summary>
        /// "Agregar estas horas a una clase que ya esta": solo anade franjas, que no
        /// pueden cruzarse con las suyas. Lo que se cruza con ellas de otras clases
        /// del mismo laboratorio se les quita (RecortarEncimadas).
        /// </summary>
        public void PonerHoras(int idCarga, IList<FranjaCiclo> franjas, int idAdministrador)
        {
            Permisos.Exigir("Poner horas a una clase del horario en preparacion");
            if (franjas == null || franjas.Count == 0)
            {
                throw new ErrorDeDatos("Elige al menos un horario.");
            }

            ValidarFranjas(franjas);
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                int idCiclo = BloquearCarga(conexion, transaccion, idCarga);
                List<FranjaCiclo> todas = new List<FranjaCiclo>(franjas);
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT laboratorio, dia_semana, hora_inicio, hora_fin FROM ciclo_franjas WHERE idcarga = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", idCarga);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            todas.Add(new FranjaCiclo
                            {
                                CargaId = idCarga,
                                LaboratorioId = reader.GetInt32("laboratorio"),
                                DiaSemana = Convert.ToInt32(reader["dia_semana"]),
                                Inicio = reader.GetTimeSpan("hora_inicio"),
                                Fin = reader.GetTimeSpan("hora_fin")
                            });
                        }
                    }
                }

                ValidarFranjas(todas);
                ExigirCatalogos(conexion, transaccion, null, null, null, franjas);
                RecortarEncimadas(conexion, transaccion, idCiclo, idCarga, franjas, 0);
                InsertarFranjas(conexion, transaccion, idCarga, franjas);
                int asignados = AsignarPorAsignar(conexion, transaccion, idCiclo, idCarga, MinutosDe(franjas));
                Tocar(conexion, transaccion, idCiclo, idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo + ": " + franjas.Count + " horarios mas para la clase " + idCarga
                    + (asignados > 0 ? "; bajan " + asignados + " min de lo que le faltaba asignar" : "") + ".");
            }
        }

        /// <summary>"Quitar estas horas": una franja del horario en preparacion (sus horas no quedan en "Falta asignar").</summary>
        public void QuitarFranja(int idFranja, int idAdministrador)
        {
            QuitarHoras(idFranja, false, idAdministrador);
        }

        /// <summary>
        /// "Quitar horas...": una franja del horario en preparacion. Con
        /// porAsignar ("¿Se va a dar en otro horario?" Si, como Horario semanal),
        /// sus horas quedan en "Falta asignar" de su clase.
        /// </summary>
        public void QuitarHoras(int idFranja, bool porAsignar, int idAdministrador)
        {
            Permisos.Exigir("Quitar unas horas del horario en preparacion");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                FranjaCiclo franja = null;
                int? idCiclo = null;
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT cc.idciclo, f.idcarga, f.laboratorio, f.dia_semana, f.hora_inicio, f.hora_fin
                    FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga WHERE f.idfranja = @F", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@F", idFranja);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            idCiclo = reader.GetInt32("idciclo");
                            franja = new FranjaCiclo
                            {
                                Id = idFranja,
                                CargaId = reader.GetInt32("idcarga"),
                                LaboratorioId = reader.GetInt32("laboratorio"),
                                DiaSemana = Convert.ToInt32(reader["dia_semana"]),
                                Inicio = reader.GetTimeSpan("hora_inicio"),
                                Fin = reader.GetTimeSpan("hora_fin")
                            };
                        }
                    }
                }

                if (!idCiclo.HasValue)
                {
                    throw new ErrorDeDatos("Esas horas ya no están en este horario: vuelve a entrar a la pestaña.");
                }

                Bloquear(conexion, transaccion, idCiclo.Value, EstadoConfiguracion.EnPreparacion);
                int quitadas;
                using (MySqlCommand comando = new MySqlCommand("DELETE FROM ciclo_franjas WHERE idfranja = @F", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@F", idFranja);
                    quitadas = comando.ExecuteNonQuery();
                }

                if (quitadas != 1)
                {
                    throw new ErrorDeDatos("Esas horas ya no están en este horario: vuelve a entrar a la pestaña.");
                }

                if (porAsignar)
                {
                    InsertarPorAsignar(conexion, transaccion, idCiclo.Value, franja.CargaId, HorariosDatos.MotivoQuitada, franja.LaboratorioId, franja.DiaSemana,
                        franja.Inicio, franja.Fin);
                }

                Tocar(conexion, transaccion, idCiclo.Value, idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo.Value + ": franja " + idFranja + " quitada" + (porAsignar ? " (sus horas quedan por asignar)" : "") + ".");
            }
        }

        /// <summary>
        /// "Cambiar horas" de Proximo ciclo: una franja del horario en preparacion
        /// pasa a otro dia, otras horas u otro laboratorio y, si se elige, a otra
        /// clase del mismo horario (idCarga), en su sitio (conserva su id). No
        /// puede cruzarse con las demas horas de la clase a la que va; a otras
        /// clases del mismo laboratorio se les quita lo que se cruza
        /// (RecortarEncimadas), y los choques (docente o grupo en otro
        /// laboratorio) no lo impiden (impiden aplicar).
        /// </summary>
        public void CambiarFranja(int idFranja, int idCarga, FranjaCiclo nueva, int idAdministrador)
        {
            Permisos.Exigir("Cambiar unas horas del horario en preparacion");
            if (nueva == null)
            {
                throw new ErrorDeDatos("Elige el día y las horas.");
            }

            ValidarFranjas(new List<FranjaCiclo> { nueva });
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                int idCiclo = BloquearCarga(conexion, transaccion, idCarga);
                // De que clase eran y cuanto duraban: lo que gana la clase a la que van baja lo que le faltaba.
                int? cicloDeLaFranja = null;
                int cargaDeAntes = 0;
                int minutosDeAntes = 0;
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT cc.idciclo, f.idcarga, f.hora_inicio, f.hora_fin FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                     WHERE f.idfranja = @F FOR UPDATE", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@F", idFranja);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            cicloDeLaFranja = reader.GetInt32("idciclo");
                            cargaDeAntes = reader.GetInt32("idcarga");
                            minutosDeAntes = HorariosDatos.MinutosEntre(reader.GetTimeSpan("hora_inicio"), reader.GetTimeSpan("hora_fin"));
                        }
                    }
                }

                if (cicloDeLaFranja != idCiclo)
                {
                    throw new ErrorDeDatos("Esas horas ya no están en este horario: vuelve a entrar a la pestaña.");
                }

                // Las demas horas de la clase a la que va (sin las que se cambian): no pueden cruzarse.
                List<FranjaCiclo> todas = new List<FranjaCiclo> { nueva };
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT laboratorio, dia_semana, hora_inicio, hora_fin FROM ciclo_franjas WHERE idcarga = @Carga AND idfranja <> @F", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Carga", idCarga);
                    comando.Parameters.AddWithValue("@F", idFranja);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            todas.Add(new FranjaCiclo
                            {
                                CargaId = idCarga,
                                LaboratorioId = reader.GetInt32("laboratorio"),
                                DiaSemana = Convert.ToInt32(reader["dia_semana"]),
                                Inicio = reader.GetTimeSpan("hora_inicio"),
                                Fin = reader.GetTimeSpan("hora_fin")
                            });
                        }
                    }
                }

                ValidarFranjas(todas);
                ExigirCatalogos(conexion, transaccion, null, null, null, new List<FranjaCiclo> { nueva });
                RecortarEncimadas(conexion, transaccion, idCiclo, idCarga, new List<FranjaCiclo> { nueva }, idFranja);
                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE ciclo_franjas SET idcarga = @Carga, laboratorio = @L, dia_semana = @D, hora_inicio = @I, hora_fin = @Fin
                     WHERE idfranja = @F", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Carga", idCarga);
                    comando.Parameters.AddWithValue("@L", nueva.LaboratorioId);
                    comando.Parameters.AddWithValue("@D", nueva.DiaSemana);
                    comando.Parameters.AddWithValue("@I", nueva.Inicio);
                    comando.Parameters.AddWithValue("@Fin", nueva.Fin);
                    comando.Parameters.AddWithValue("@F", idFranja);
                    comando.ExecuteNonQuery();
                }

                // Como Horario semanal: si va a otra clase, esa gana todas sus horas.
                AsignarPorAsignar(conexion, transaccion, idCiclo, idCarga,
                    HorariosDatos.MinutosEntre(nueva.Inicio, nueva.Fin) - (cargaDeAntes == idCarga ? minutosDeAntes : 0));
                Tocar(conexion, transaccion, idCiclo, idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo + ": franja " + idFranja + " cambiada (clase " + idCarga + ", laboratorio " + nueva.LaboratorioId
                    + ", dia " + nueva.DiaSemana + ", " + HorariosDatos.Hora(nueva.Inicio) + "-" + HorariosDatos.Hora(nueva.Fin) + ").");
            }
        }

        /// <summary>Quita clases del horario en preparacion (sus franjas se van con ellas). Devuelve cuantas quito.</summary>
        public int QuitarClases(ICollection<int> idsCarga, int idAdministrador)
        {
            Permisos.Exigir("Quitar clases del horario en preparacion");
            if (idsCarga == null || idsCarga.Count == 0)
            {
                return 0;
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                List<int> ciclos = new List<int>();
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT DISTINCT idciclo FROM ciclo_cargas WHERE idcarga IN (" + Lista(comando, "k", idsCarga) + ")";
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ciclos.Add(reader.GetInt32(0));
                        }
                    }
                }

                if (ciclos.Count == 0)
                {
                    throw new ErrorDeDatos("Esas clases ya no están en este horario: vuelve a entrar a la pestaña.");
                }

                if (ciclos.Count > 1)
                {
                    throw new ErrorDeDatos("Esas clases son de plantillas distintas: vuelve a entrar a la pestaña.");
                }

                Bloquear(conexion, transaccion, ciclos[0], EstadoConfiguracion.EnPreparacion);
                int quitadas;
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", ciclos[0]);
                    comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                    // Lo que les faltaba asignar se va con ellas.
                    string ids = Lista(comando, "k", idsCarga);
                    comando.CommandText = "DELETE FROM ciclo_cargas WHERE idciclo = @C AND quitada = @PorAsignar AND carga_origen IN (" + ids + ")";
                    comando.ExecuteNonQuery();
                    comando.CommandText = "DELETE FROM ciclo_cargas WHERE idciclo = @C AND quitada <> @PorAsignar AND idcarga IN (" + ids + ")";
                    quitadas = comando.ExecuteNonQuery();
                }

                Tocar(conexion, transaccion, ciclos[0], idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + ciclos[0] + ": " + quitadas + " clases quitadas (" + string.Join(",", idsCarga) + ").");
                return quitadas;
            }
        }

        /// <summary>
        /// Las reglas de las franjas de UNA clase, sin la base: laboratorio y dia,
        /// fin despues de inicio, y dos de ellas no se cruzan el mismo dia (aunque
        /// sean de laboratorios distintos). ErrorDeDatos con el primer fallo.
        /// </summary>
        public static void ValidarFranjas(IList<FranjaCiclo> franjas)
        {
            if (franjas == null)
            {
                return;
            }

            foreach (FranjaCiclo franja in franjas)
            {
                if (franja == null || franja.LaboratorioId <= 0 || franja.DiaSemana < 1 || franja.DiaSemana > 7)
                {
                    throw new ErrorDeDatos("Elige el laboratorio y el día de cada horario.");
                }

                if (franja.Fin <= franja.Inicio)
                {
                    throw new ErrorDeDatos("La hora de fin tiene que ser después de la de inicio.");
                }
            }

            for (int i = 0; i < franjas.Count; i++)
            {
                for (int j = i + 1; j < franjas.Count; j++)
                {
                    FranjaCiclo a = franjas[i];
                    FranjaCiclo b = franjas[j];
                    if (a.DiaSemana == b.DiaSemana && a.Inicio < b.Fin && a.Fin > b.Inicio)
                    {
                        throw new ErrorDeDatos("Dos horarios de esta clase se cruzan el " + HorariosDatos.NombresDia[a.DiaSemana] + ".");
                    }
                }
            }
        }

        /// <summary>
        /// Que sigan en DigBit la persona docente, el grupo, la materia y los
        /// laboratorios elegidos (null: no se mira). Sin esto, uno que se borro
        /// mientras el dialogo estaba abierto daria un error de clave foranea sin
        /// explicacion.
        /// </summary>
        private static void ExigirCatalogos(MySqlConnection conexion, MySqlTransaction transaccion, int? profesor, int? grupo, int? materia,
            IList<FranjaCiclo> franjas)
        {
            List<int> laboratorios = (franjas ?? new List<FranjaCiclo>()).Select(f => f.LaboratorioId).Distinct().ToList();
            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@P", profesor ?? 0);
                comando.Parameters.AddWithValue("@G", grupo ?? 0);
                comando.Parameters.AddWithValue("@M", materia ?? 0);
                comando.CommandText = @"
                    SELECT (SELECT COUNT(*) FROM usuarios WHERE idusuarios = @P),
                           (SELECT COUNT(*) FROM grupos WHERE idgrupos = @G),
                           (SELECT COUNT(*) FROM materias WHERE id_materia = @M),
                           (SELECT COUNT(*) FROM laboratorios WHERE idlaboratorios IN (" + Lista(comando, "l", laboratorios) + "))";
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    reader.Read();
                    if (profesor.HasValue && Convert.ToInt32(reader.GetValue(0)) == 0)
                    {
                        throw new ErrorDeDatos("Esa persona docente ya no está en DigBit: elige otra.");
                    }

                    if (grupo.HasValue && Convert.ToInt32(reader.GetValue(1)) == 0)
                    {
                        throw new ErrorDeDatos("Ese grupo ya no está en DigBit: elige otro.");
                    }

                    if (materia.HasValue && Convert.ToInt32(reader.GetValue(2)) == 0)
                    {
                        throw new ErrorDeDatos("Esa materia ya no está en DigBit: elige otra.");
                    }

                    if (Convert.ToInt32(reader.GetValue(3)) < laboratorios.Count)
                    {
                        throw new ErrorDeDatos("Uno de los laboratorios ya no está en DigBit: elige otro.");
                    }
                }
            }
        }

        /// <summary>
        /// Con docente: el mismo docente, grupo y materia no pueden estar dos veces
        /// en la misma configuracion. Con FOR UPDATE sobre sus clases (el ciclo ya
        /// esta bloqueado por Bloquear).
        /// </summary>
        private static void ExigirNoRepetida(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, int? profesor, int grupo, int materia, int excluirCarga)
        {
            if (!profesor.HasValue)
            {
                return;
            }

            object otra;
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT idcarga FROM ciclo_cargas
                 WHERE idciclo = @C AND profesor = @P AND grupo = @G AND materia = @M AND quitada = 0 AND idcarga <> @X
                 LIMIT 1 FOR UPDATE", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.Parameters.AddWithValue("@P", profesor.Value);
                comando.Parameters.AddWithValue("@G", grupo);
                comando.Parameters.AddWithValue("@M", materia);
                comando.Parameters.AddWithValue("@X", excluirCarga);
                otra = comando.ExecuteScalar();
            }

            if (otra == null || otra == DBNull.Value)
            {
                return;
            }

            string docente = "Esa persona docente";
            string clase = "esa materia con ese grupo";
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT u.nombre, u.apellido_paterno,
                       (SELECT m.nombre_materia FROM materias m WHERE m.id_materia = @M) AS materia,
                       (SELECT g.nombre_grupo FROM grupos g WHERE g.idgrupos = @G) AS grupo
                FROM usuarios u WHERE u.idusuarios = @P", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@P", profesor.Value);
                comando.Parameters.AddWithValue("@G", grupo);
                comando.Parameters.AddWithValue("@M", materia);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        docente = HorariosDatos.NombreCorto(Texto(reader, "nombre"), Texto(reader, "apellido_paterno"));
                        clase = Texto(reader, "materia") + " con " + Texto(reader, "grupo");
                    }
                }
            }

            throw new ErrorDeDatos(docente + " ya tiene " + clase + " en este horario.");
        }

        /// <summary>
        /// La configuracion de una clase, bloqueada y en preparacion; luego la
        /// clase, bloqueada (otra ventana pudo quitarla). Devuelve el idciclo.
        /// </summary>
        private static int BloquearCarga(MySqlConnection conexion, MySqlTransaction transaccion, int idCarga)
        {
            int? idCiclo = CicloDeLaCarga(conexion, transaccion, idCarga, false);
            if (!idCiclo.HasValue)
            {
                throw new ErrorDeDatos("Esa clase ya no está en este horario: vuelve a entrar a la pestaña.");
            }

            Bloquear(conexion, transaccion, idCiclo.Value, EstadoConfiguracion.EnPreparacion);
            if (CicloDeLaCarga(conexion, transaccion, idCarga, true) != idCiclo)
            {
                throw new ErrorDeDatos("Esa clase ya no está en este horario: vuelve a entrar a la pestaña.");
            }

            return idCiclo.Value;
        }

        private static int? CicloDeLaCarga(MySqlConnection conexion, MySqlTransaction transaccion, int idCarga, bool bloquear)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idciclo FROM ciclo_cargas WHERE idcarga = @Id" + (bloquear ? " FOR UPDATE" : ""), conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idCarga);
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor);
            }
        }

        /// <summary>
        /// Poner unas horas encima de otra clase del mismo laboratorio la
        /// reemplaza (decision del 2026-10-31, como Horario semanal con db/25):
        /// a las franjas de OTRAS clases sin quitar de ese horario que se cruzan
        /// con "nuevas" el mismo dia en el mismo laboratorio se les quita SOLO lo
        /// que se cruza (RevisionCiclo.Resto: la de en medio se parte en dos, la
        /// tapada entera se borra), y eso queda en su "Falta asignar" (2026-11-03,
        /// como Horario semanal; la que se queda sin horas sale tambien en "Sin
        /// horario"). La de "excluirFranja" (la que se mueve) no cuenta. Dentro de
        /// la transaccion de quien la llama, con el horario ya bloqueado. Devuelve
        /// cuantas franjas toco.
        /// </summary>
        private static int RecortarEncimadas(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, int idCarga, IList<FranjaCiclo> nuevas,
            int excluirFranja)
        {
            if (nuevas == null || nuevas.Count == 0)
            {
                return 0;
            }

            List<FranjaCiclo> otras = new List<FranjaCiclo>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT f.idfranja, f.idcarga, f.laboratorio, f.dia_semana, f.hora_inicio, f.hora_fin
                FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                WHERE cc.idciclo = @C AND cc.quitada = 0 AND f.idcarga <> @Carga AND f.idfranja <> @X", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.Parameters.AddWithValue("@Carga", idCarga);
                comando.Parameters.AddWithValue("@X", excluirFranja);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        otras.Add(new FranjaCiclo
                        {
                            Id = reader.GetInt32("idfranja"),
                            CargaId = reader.GetInt32("idcarga"),
                            LaboratorioId = reader.GetInt32("laboratorio"),
                            DiaSemana = Convert.ToInt32(reader["dia_semana"]),
                            Inicio = reader.GetTimeSpan("hora_inicio"),
                            Fin = reader.GetTimeSpan("hora_fin")
                        });
                    }
                }
            }

            int tocadas = 0;
            foreach (FranjaCiclo otra in otras)
            {
                List<TimeSpan[]> resto = RevisionCiclo.Resto(otra, nuevas);
                if (resto == null)
                {
                    continue;
                }

                tocadas++;
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@F", otra.Id);
                    if (resto.Count == 0)
                    {
                        comando.CommandText = "DELETE FROM ciclo_franjas WHERE idfranja = @F";
                    }
                    else
                    {
                        // El primer trozo, en su sitio (conserva su id); el otro, nuevo.
                        comando.CommandText = "UPDATE ciclo_franjas SET hora_inicio = @I, hora_fin = @Fin WHERE idfranja = @F";
                        comando.Parameters.AddWithValue("@I", resto[0][0]);
                        comando.Parameters.AddWithValue("@Fin", resto[0][1]);
                    }

                    comando.ExecuteNonQuery();
                }

                List<FranjaCiclo> trozos = resto.Skip(1).Select(t => new FranjaCiclo
                {
                    LaboratorioId = otra.LaboratorioId,
                    DiaSemana = otra.DiaSemana,
                    Inicio = t[0],
                    Fin = t[1]
                }).ToList();
                InsertarFranjas(conexion, transaccion, otra.CargaId, trozos);
                // Lo que se le quito (cada cruce, como RevisionCiclo.Reemplazos), en su "Falta asignar".
                foreach (FranjaCiclo n in nuevas.Where(n => n.LaboratorioId == otra.LaboratorioId && n.DiaSemana == otra.DiaSemana
                    && n.Inicio < otra.Fin && n.Fin > otra.Inicio))
                {
                    InsertarPorAsignar(conexion, transaccion, idCiclo, otra.CargaId, HorariosDatos.MotivoDesplazada, otra.LaboratorioId, otra.DiaSemana,
                        n.Inicio > otra.Inicio ? n.Inicio : otra.Inicio, n.Fin < otra.Fin ? n.Fin : otra.Fin);
                }

                Log.Info("Horario en preparacion " + idCiclo + ": a la clase " + otra.CargaId + " se le quito lo que se cruzaba de su franja " + otra.Id
                    + " (laboratorio " + otra.LaboratorioId + ", dia " + otra.DiaSemana + ", " + HorariosDatos.Hora(otra.Inicio) + "-" + HorariosDatos.Hora(otra.Fin)
                    + "): " + (resto.Count == 0 ? "se quito entera" : "queda " + string.Join(" y ", resto.Select(t => HorariosDatos.Hora(t[0]) + "-" + HorariosDatos.Hora(t[1]))))
                    + "; la reemplaza la clase " + idCarga + ".");
            }

            return tocadas;
        }

        /// <summary>Las franjas de una clase, con un solo INSERT de varias filas.</summary>
        private static void InsertarFranjas(MySqlConnection conexion, MySqlTransaction transaccion, int idCarga, IList<FranjaCiclo> franjas)
        {
            if (franjas == null || franjas.Count == 0)
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Carga", idCarga);
                List<string> filas = new List<string>();
                for (int i = 0; i < franjas.Count; i++)
                {
                    filas.Add("(@Carga, @l" + i + ", @d" + i + ", @i" + i + ", @f" + i + ")");
                    comando.Parameters.AddWithValue("@l" + i, franjas[i].LaboratorioId);
                    comando.Parameters.AddWithValue("@d" + i, franjas[i].DiaSemana);
                    comando.Parameters.AddWithValue("@i" + i, franjas[i].Inicio);
                    comando.Parameters.AddWithValue("@f" + i, franjas[i].Fin);
                }

                comando.CommandText = "INSERT INTO ciclo_franjas (idcarga, laboratorio, dia_semana, hora_inicio, hora_fin) VALUES " + string.Join(", ", filas);
                comando.ExecuteNonQuery();
            }
        }

        // ---------------------------------------------------------------------
        // Falta asignar (sin migracion: QuitadaPorAsignar)
        // ---------------------------------------------------------------------

        /// <summary>
        /// "Ya no hace falta": unas horas por asignar del horario en preparacion se
        /// dan por resueltas (se borran; las horas de la clase no cambian).
        /// ErrorDeDatos si ya no estan (se asignaron, o se quito la clase, mientras tanto).
        /// </summary>
        public void YaNoHaceFalta(int idPorAsignar, int idAdministrador)
        {
            Permisos.Exigir("Dar por resueltas unas horas por asignar del horario en preparacion");
            const string YaNoEstan = "Esas horas ya no están por asignar (quizá se asignaron mientras tanto). Vuelve a abrir la lista.";
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                int? idCiclo;
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT idciclo FROM ciclo_cargas WHERE idcarga = @Id AND quitada = @PorAsignar", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", idPorAsignar);
                    comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                    object valor = comando.ExecuteScalar();
                    idCiclo = valor == null || valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor);
                }

                if (!idCiclo.HasValue)
                {
                    throw new ErrorDeDatos(YaNoEstan);
                }

                Bloquear(conexion, transaccion, idCiclo.Value, EstadoConfiguracion.EnPreparacion);
                int borradas;
                using (MySqlCommand comando = new MySqlCommand(
                    "DELETE FROM ciclo_cargas WHERE idcarga = @Id AND idciclo = @C AND quitada = @PorAsignar", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", idPorAsignar);
                    comando.Parameters.AddWithValue("@C", idCiclo.Value);
                    comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                    borradas = comando.ExecuteNonQuery();
                }

                if (borradas != 1)
                {
                    throw new ErrorDeDatos(YaNoEstan);
                }

                Tocar(conexion, transaccion, idCiclo.Value, idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo.Value + ": las horas por asignar " + idPorAsignar + " ya no hacen falta.");
            }
        }

        /// <summary>
        /// Unas horas que se le quitan a una clase del horario en preparacion
        /// quedan en su "Falta asignar" (un renglon de QuitadaPorAsignar con su
        /// docente, grupo y materia). Nada si no duran nada o la clase ya no esta.
        /// </summary>
        private static void InsertarPorAsignar(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, int idCarga, string motivo,
            int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            PorAsignarCiclo nueva = new PorAsignarCiclo
            {
                CargaId = idCarga,
                Motivo = motivo,
                LaboratorioId = idLaboratorio,
                DiaSemana = diaSemana,
                Inicio = inicio,
                Fin = fin
            };
            nueva.Minutos = nueva.MinutosOriginales;
            if (nueva.Minutos <= 0)
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada, horario_origen, carga_origen)
                SELECT idciclo, profesor, grupo, materia, @PorAsignar, @Que, idcarga FROM ciclo_cargas
                 WHERE idcarga = @Carga AND idciclo = @C AND quitada = 0", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                comando.Parameters.AddWithValue("@Que", Cifrar(nueva));
                comando.Parameters.AddWithValue("@Carga", idCarga);
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Las horas nuevas de una clase bajan lo que le faltaba asignar, de lo mas
        /// antiguo a lo mas nuevo (como Horario semanal): lo que llega a cero se
        /// borra. Devuelve cuantos minutos bajaron (0 si no gano tiempo).
        /// </summary>
        private static int AsignarPorAsignar(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, int idCarga, int minutos)
        {
            if (minutos <= 0)
            {
                return 0;
            }

            List<PorAsignarCiclo> suyas = LeerPorAsignar(conexion, transaccion, idCiclo, " AND p.carga_origen = @Carga",
                c => c.Parameters.AddWithValue("@Carga", idCarga), true);
            int quedan = minutos;
            int bajaron = 0;
            foreach (PorAsignarCiclo p in suyas)
            {
                if (quedan <= 0)
                {
                    break;
                }

                int baja = Math.Min(quedan, p.Minutos);
                quedan -= baja;
                bajaron += baja;
                p.Minutos -= baja;
                using (MySqlCommand comando = new MySqlCommand(p.Minutos == 0
                    ? "DELETE FROM ciclo_cargas WHERE idcarga = @Id AND quitada = @PorAsignar"
                    : "UPDATE ciclo_cargas SET horario_origen = @Que WHERE idcarga = @Id AND quitada = @PorAsignar", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", p.Id);
                    comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                    comando.Parameters.AddWithValue("@Que", Cifrar(p));
                    comando.ExecuteNonQuery();
                }
            }

            return bajaron;
        }

        /// <summary>
        /// Lo que falta asignar en el horario (y la condicion: " AND p.carga_origen
        /// = @Carga"), de lo mas antiguo a lo mas nuevo, sin los nombres de los
        /// laboratorios. Con bloquear, FOR UPDATE. Lo que no se entiende se salta.
        /// </summary>
        private static List<PorAsignarCiclo> LeerPorAsignar(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, string condicion,
            Action<MySqlCommand> parametros, bool bloquear)
        {
            List<PorAsignarCiclo> lista = new List<PorAsignarCiclo>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT p.idcarga, p.carga_origen, p.horario_origen FROM ciclo_cargas p
                 WHERE p.idciclo = @C AND p.quitada = @PorAsignar" + condicion + @"
                 ORDER BY p.idcarga" + (bloquear ? " FOR UPDATE" : ""), conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                if (parametros != null)
                {
                    parametros(comando);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int? carga = Entero(reader, "carga_origen");
                        PorAsignarCiclo p = carga.HasValue ? Descifrar(reader.GetInt32("idcarga"), carga.Value, Texto(reader, "horario_origen")) : null;
                        if (p != null)
                        {
                            lista.Add(p);
                        }
                        else
                        {
                            Log.Aviso("CicloPreliminarDatos: el renglon por asignar " + reader.GetInt32("idcarga") + " del horario " + idCiclo + " no se entiende; no se usa.");
                        }
                    }
                }
            }

            return lista;
        }

        /// <summary>"desplazada|60|3|1|08:00|09:00": lo que va en horario_origen de un renglon de QuitadaPorAsignar.</summary>
        private static string Cifrar(PorAsignarCiclo p)
        {
            return string.Join("|", p.Motivo, p.Minutos.ToString(CultureInfo.InvariantCulture), p.LaboratorioId.ToString(CultureInfo.InvariantCulture),
                p.DiaSemana.ToString(CultureInfo.InvariantCulture), HorariosDatos.Hora(p.Inicio), HorariosDatos.Hora(p.Fin));
        }

        /// <summary>Lo que dice horario_origen de un renglon de QuitadaPorAsignar, o null si no se entiende.</summary>
        private static PorAsignarCiclo Descifrar(int id, int idCarga, string texto)
        {
            string[] partes = (texto ?? "").Split('|');
            int minutos;
            int laboratorio;
            int dia;
            TimeSpan inicio;
            TimeSpan fin;
            if (partes.Length != 6
                || !int.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutos) || minutos <= 0
                || !int.TryParse(partes[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out laboratorio)
                || !int.TryParse(partes[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out dia) || dia < 1 || dia > 7
                || !TimeSpan.TryParse(partes[4], CultureInfo.InvariantCulture, out inicio)
                || !TimeSpan.TryParse(partes[5], CultureInfo.InvariantCulture, out fin))
            {
                return null;
            }

            return new PorAsignarCiclo
            {
                Id = id,
                CargaId = idCarga,
                Motivo = partes[0] == HorariosDatos.MotivoQuitada ? HorariosDatos.MotivoQuitada : HorariosDatos.MotivoDesplazada,
                Minutos = minutos,
                LaboratorioId = laboratorio,
                DiaSemana = dia,
                Inicio = inicio,
                Fin = fin
            };
        }

        /// <summary>Lo que duran esas franjas, en minutos.</summary>
        private static int MinutosDe(IEnumerable<FranjaCiclo> franjas)
        {
            return (franjas ?? new List<FranjaCiclo>()).Sum(f => HorariosDatos.MinutosEntre(f.Inicio, f.Fin));
        }

        /// <summary>Lo que duran ahora las horas de una clase del horario, en minutos.</summary>
        private static int MinutosDeLaCarga(MySqlConnection conexion, MySqlTransaction transaccion, int idCarga)
        {
            int minutos = 0;
            using (MySqlCommand comando = new MySqlCommand("SELECT hora_inicio, hora_fin FROM ciclo_franjas WHERE idcarga = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idCarga);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        minutos += HorariosDatos.MinutosEntre(reader.GetTimeSpan("hora_inicio"), reader.GetTimeSpan("hora_fin"));
                    }
                }
            }

            return minutos;
        }

        // ---------------------------------------------------------------------
        // Leer una configuracion
        // ---------------------------------------------------------------------

        /// <summary>Una configuracion para verla o editarla (sin fechas: sin YaProgramadas).</summary>
        public CicloLeido Leer(int idCiclo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return Leer(conexion, null, idCiclo);
            }
        }

        /// <summary>
        /// La configuracion, sus clases con sus franjas y sus docentes. Con
        /// fechas, ademas, las franjas de las clases que ya estan en Clases y
        /// tambien valen en esas fechas (regla de 3.5 b). En solo consulta no se
        /// lee la que esta en preparacion.
        /// </summary>
        internal static CicloLeido Leer(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, DatosDeFechas fechas = null)
        {
            CicloLeido leido = new CicloLeido
            {
                Hoy = Hoy(conexion, transaccion),
                ConBajas = Esquema.TieneBajas(conexion, transaccion),
                ConLaboratoriosDesactivables = Esquema.LaboratoriosDesactivables(conexion, transaccion),
                Fechas = fechas
            };
            leido.Ciclo = LeerCiclos(conexion, transaccion, "WHERE cp.idciclo = @C", c => c.Parameters.AddWithValue("@C", idCiclo)).FirstOrDefault();
            if (leido.Ciclo == null)
            {
                throw new ErrorDeDatos("Esa plantilla ya no está: vuelve a entrar a la pestaña.");
            }

            if (leido.Ciclo.EnPreparacion && Permisos.SoloConsulta)
            {
                throw new ErrorDeDatos("El horario en preparación solo lo ve la administración: aquí se ven las plantillas guardadas y los horarios aplicados.");
            }

            string activoProfesor = leido.ConBajas ? "u.activo" : "1";
            string activoLaboratorio = leido.ConLaboratoriosDesactivables ? "l.activo" : "1";

            // Las clases, con los nombres de como eran en lo que se copio.
            Dictionary<int, CargaCiclo> porId = new Dictionary<int, CargaCiclo>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT cc.idcarga, cc.profesor, cc.grupo, cc.materia, cc.quitada, cc.clase_origen, cc.profesor_origen,
                       cc.grupo_origen, cc.materia_origen, cc.horario_origen, cc.clase_publicada, cc.carga_origen,
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
                WHERE cc.idciclo = @C AND cc.quitada <> @PorAsignar
                ORDER BY cc.profesor IS NOT NULL, u.apellido_paterno, u.nombre, m.nombre_materia, g.nombre_grupo, cc.idcarga", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
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
                            ClasePublicada = Entero(reader, "clase_publicada"),
                            CargaOrigen = Entero(reader, "carga_origen")
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

            // Las personas docentes de sus clases, para la leyenda y el filtro.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT DISTINCT u.idusuarios, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS nombre_completo,
                       u.nombre AS p_nombre, u.apellido_paterno AS p_paterno, " + activoProfesor + @" AS activo
                FROM ciclo_cargas cc
                INNER JOIN usuarios u ON u.idusuarios = cc.profesor
                WHERE cc.idciclo = @C AND cc.quitada = 0
                ORDER BY u.apellido_paterno, u.nombre", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        leido.Profesores.Add(new ProfesorDelCiclo
                        {
                            Id = reader.GetInt32("idusuarios"),
                            Nombre = Texto(reader, "nombre_completo"),
                            Corto = HorariosDatos.NombreCorto(Texto(reader, "p_nombre"), Texto(reader, "p_paterno")),
                            Activo = Convert.ToInt32(reader["activo"]) == 1
                        });
                    }
                }
            }

            // "Falta asignar": solo de clases que siguen en ella, con el nombre de su laboratorio.
            leido.PorAsignar = LeerPorAsignar(conexion, transaccion, idCiclo, "", null, false)
                .Where(p => porId.ContainsKey(p.CargaId) && !porId[p.CargaId].Quitada).ToList();
            if (leido.PorAsignar.Count > 0)
            {
                Dictionary<int, string> laboratorios = new Dictionary<int, string>();
                using (MySqlCommand comando = new MySqlCommand("SELECT idlaboratorios, nombre_laboratorio FROM laboratorios", conexion, transaccion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        laboratorios[reader.GetInt32("idlaboratorios")] = Texto(reader, "nombre_laboratorio");
                    }
                }

                foreach (PorAsignarCiclo p in leido.PorAsignar)
                {
                    string nombre;
                    p.Laboratorio = laboratorios.TryGetValue(p.LaboratorioId, out nombre) ? nombre : "";
                }
            }

            if (fechas != null)
            {
                // Las clases que ya estan en Clases, empiezan despues del ultimo dia del
                // horario de ahora y tambien valen en esas fechas (db/20: solo sus
                // franjas que se dan algun dia de ellas).
                leido.YaProgramadas = LeerFranjasReales(conexion, transaccion,
                    "c.activa = 1 AND c.vigente_desde > @Fin AND c.vigente_desde <= @Hasta AND c.vigente_hasta >= @Desde AND "
                    + VigenciaFranjas.SeCruzaCon(Esquema.VigenciaDeFranjas(conexion, transaccion), "@Desde", "@Hasta"), c =>
                    {
                        c.Parameters.AddWithValue("@Desde", fechas.Desde.Date);
                        c.Parameters.AddWithValue("@Hasta", fechas.Hasta.Date);
                        c.Parameters.AddWithValue("@Fin", fechas.Fin.Date);
                    });
            }

            return leido;
        }

        /// <summary>
        /// Para despues (punto 8, "No se copiaron"): las clases activas que valen
        /// algun dia entre el que se copio y la vispera de desde y que ninguna
        /// clase de la configuracion tiene como origen. Hoy no se llama.
        /// </summary>
        private static void LeerNoCopiadas(MySqlConnection conexion, MySqlTransaction transaccion, CicloLeido leido, DateTime desde)
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
                comando.Parameters.AddWithValue("@Desde", desde.Date);
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
                nc.PorQue = !ciclo.CopiadoDel.HasValue ? "Empezaste en blanco"
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
