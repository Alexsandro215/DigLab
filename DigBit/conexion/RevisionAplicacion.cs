using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigBit.conexion
{
    /// <summary>Que es un renglon de "Antes de aplicar": rojo no deja aplicar, ambar avisa, verde esta bien y gris informa.</summary>
    public enum NivelRevision
    {
        Rojo,
        Ambar,
        Verde,
        Gris
    }

    /// <summary>Un renglon de "Antes de aplicar" (pestana Aplicacion).</summary>
    public class RenglonRevision
    {
        public NivelRevision Nivel;
        public string Texto = "";
        /// <summary>Con que filtro abre la configuracion en Clases el enlace "Verlas" (Ninguno: sin enlace).</summary>
        public FiltroClases Ver;

        /// <summary>No deja aplicar.</summary>
        public bool Bloquea
        {
            get { return Nivel == NivelRevision.Rojo; }
        }
    }

    /// <summary>
    /// Las clases de hoy del horario nuevo (sus franjas de hoy) a una hora, para
    /// "Ahora mismo": las que ya terminaron (no cuentan), las que estan en curso
    /// y las que faltan (cuentan con el horario nuevo).
    /// </summary>
    public class ClasesDeHoy
    {
        public int Terminadas;
        public int EnCurso;
        public int PorVenir;

        public int Total
        {
            get { return Terminadas + EnCurso + PorVenir; }
        }
    }

    /// <summary>
    /// Los renglones de "Antes de aplicar" de la pestana Aplicacion
    /// (especificacion v2, 3.6), en memoria: lo que no deja aplicar (en rojo, o
    /// en verde si esta bien), lo que solo avisa (ambar) y lo que informa (gris).
    /// Sale de RevisionCiclo.Revisar (ya en DatosDeAplicacion.Revision) mas lo de
    /// las fechas. Aplicar lo vuelve a pasar dentro de su transaccion: con algo
    /// en rojo no aplica. Con "Ahora mismo" (DatosDeFechas.EmpiezaAhora) dice
    /// tambien que pasa hoy: las clases de hoy que ya terminaron no cuentan, la
    /// que esta en curso y las que faltan si, lo ya registrado se queda, y como
    /// toman el horario nuevo los equipos del laboratorio.
    /// </summary>
    internal static class RevisionAplicacion
    {
        private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

        /// <summary>
        /// Como y cuando toman el horario nuevo los equipos del laboratorio (lo
        /// que hacen Alumno_Principal, AppContexto.VigilarConexion y
        /// CacheHorario): con conexion, el codigo se comprueba en el servidor al
        /// escribirlo y la pantalla del estudiante vuelve a leer la clase de ahora
        /// cada minuto (cada 30 s sin codigo fijo); la copia local, la de sin
        /// conexion, se renueva al abrir DigBit con servidor, cada 10 minutos con
        /// conexion y al minuto de volver la red. La bitacora que se registro sin
        /// red con la copia de antes se queda en la cola y se envia al volver
        /// (ColaBitacoras: si la clase de antes ya no se da, va sin enlace).
        /// </summary>
        public const string AvisoEquipos = "Equipos del laboratorio: con conexión usan el horario nuevo en cuanto lo apliques "
            + "(el código se comprueba en el servidor y la clase de ahora se vuelve a leer cada minuto). Su copia para trabajar "
            + "sin conexión se renueva al abrir DigBit y cada 10 minutos mientras hay conexión: un equipo que se quede sin red "
            + "antes de renovarla registra con el horario de antes, y esas bitácoras no se pierden: se envían al volver la red.";

        /// <summary>
        /// Las franjas de hoy de las clases del horario nuevo a esa hora (del
        /// servidor), con el mismo criterio que Aplicar: ya termino la que tiene
        /// su fin hasta esa hora. Pasada la medianoche, todas las de "hoy" ya
        /// terminaron (la pagina vuelve a leer).
        /// </summary>
        public static ClasesDeHoy ContarDeHoy(DatosDeAplicacion datos, DateTime ahora)
        {
            ClasesDeHoy cuenta = new ClasesDeHoy();
            if (datos == null || datos.Leido == null)
            {
                return cuenta;
            }

            DateTime hoy = datos.Hoy.Date;
            int dia = ((int)hoy.DayOfWeek + 6) % 7 + 1;
            TimeSpan hora = ahora.Date > hoy ? TimeSpan.FromDays(1)
                : ahora.Date < hoy ? TimeSpan.Zero
                : new TimeSpan(ahora.Hour, ahora.Minute, ahora.Second);
            foreach (FranjaCiclo franja in datos.Leido.Cargas.Where(c => !c.Quitada).SelectMany(c => c.Franjas).Where(f => f.DiaSemana == dia))
            {
                if (franja.Fin <= hora)
                {
                    cuenta.Terminadas++;
                }
                else if (franja.Inicio <= hora)
                {
                    cuenta.EnCurso++;
                }
                else
                {
                    cuenta.PorVenir++;
                }
            }

            return cuenta;
        }

        /// <summary>Los renglones para esas fechas y ese nombre en el registro, ordenados rojo, ambar, verde y gris.</summary>
        public static List<RenglonRevision> Revisar(DatosDeAplicacion datos, DateTime desde, DateTime hasta, DateTime fin, string nombre)
        {
            if (datos == null || datos.Leido == null)
            {
                throw new ArgumentNullException("datos");
            }

            desde = desde.Date;
            hasta = hasta.Date;
            fin = fin.Date;
            bool ahora = datos.Fechas != null && datos.Fechas.EmpiezaAhora;
            List<RenglonRevision> renglones = new List<RenglonRevision>();
            CicloLeido leido = datos.Leido;
            RevisionDelCiclo revision = datos.Revision ?? RevisionCiclo.Revisar(leido);
            List<CargaCiclo> vivas = leido.Cargas.Where(c => !c.Quitada).ToList();

            // 1. Docente.
            renglones.Add(revision.SinProfesor == 0
                ? Verde("Todas las clases tienen docente.")
                : Rojo(revision.SinProfesor == 1
                    ? "1 clase sin docente: dásela a alguien o quítala."
                    : revision.SinProfesor + " clases sin docente: dáselas a alguien o quítalas.", FiltroClases.SinDocente));

            // 2. Docentes de baja (db/08).
            if (leido.ConBajas)
            {
                renglones.Add(revision.DeBaja == 0
                    ? Verde("Ninguna persona docente está de baja.")
                    : Rojo(revision.DeBaja == 1
                        ? "1 clase es de una persona docente de baja (" + RevisionCiclo.Enumerar(revision.DocentesDeBaja) + "): pásala a otra persona docente."
                        : revision.DeBaja + " clases son de docentes de baja (" + RevisionCiclo.Enumerar(revision.DocentesDeBaja) + "): pásalas a otra persona docente.",
                        FiltroClases.DeBaja));
            }

            // 3. Laboratorios desactivados (db/08).
            if (leido.ConLaboratoriosDesactivables)
            {
                renglones.Add(revision.EnLaboratorioDesactivado == 0
                    ? Verde("Todos sus laboratorios están activos.")
                    : Rojo((revision.EnLaboratorioDesactivado == 1 ? "1 clase tiene" : revision.EnLaboratorioDesactivado + " clases tienen")
                        + (revision.LaboratoriosDesactivados.Count == 1 ? " horas en un laboratorio desactivado (" : " horas en laboratorios desactivados (")
                        + RevisionCiclo.Enumerar(revision.LaboratoriosDesactivados)
                        + (revision.EnLaboratorioDesactivado == 1 ? "): cámbiale el horario." : "): cámbiales el horario."), FiltroClases.LaboratorioDesactivado));
            }

            // 4. Choques (la misma persona docente o el mismo grupo a la misma hora en
            // otro laboratorio), tambien con las clases que ya estan en Clases en esas fechas.
            int choques = revision.Choques.Count;
            int conProgramadas = revision.Choques.Count(c => c.Programada != null);
            renglones.Add(choques == 0
                ? Verde("No hay choques.")
                : Rojo((choques == 1 ? "1 choque: corrígelo antes de aplicar" : choques + " choques: corrígelos antes de aplicar")
                    + (conProgramadas == 0 ? "."
                        : conProgramadas == choques
                            ? (choques == 1 ? " (es con una clase que ya está en Clases en esas fechas)." : " (son con clases que ya están en Clases en esas fechas).")
                            : " (" + conProgramadas + " con clases que ya están en Clases en esas fechas)."), FiltroClases.Choques));

            // 4b. Dos clases a la misma hora en el mismo laboratorio (datos viejos o
            // copiados: al ponerlas, una reemplaza a la otra). No es un choque, pero
            // asi no pueden llegar al horario en uso. Solo si las hay.
            if (revision.Encimadas.Count > 0)
            {
                ChoqueCiclo primera = revision.Encimadas[0];
                string otra = primera.Programada != null
                    ? primera.Programada.Materia + " con " + primera.Programada.Grupo + " (" + primera.Programada.ProfesorCorto + ", ya está en Clases en esas fechas)"
                    : primera.CargaB.Nombre + " (" + (primera.CargaB.ProfesorId.HasValue ? primera.CargaB.ProfesorCorto : "sin docente") + ")";
                renglones.Add(Rojo("Se enciman en el mismo laboratorio: " + primera.CargaA.Nombre + " ("
                    + (primera.CargaA.ProfesorId.HasValue ? primera.CargaA.ProfesorCorto : "sin docente") + ") y " + otra
                    + " el " + HorariosDatos.NombresDia[primera.Dia] + " de " + HorariosDatos.Hora(primera.Inicio) + " a " + HorariosDatos.Hora(primera.Fin)
                    + " en el " + EstiloHorarios.NombreLaboratorio(primera.FranjaA.Laboratorio) + ". Quítale esas horas a una de las dos."
                    + Mas(revision.Encimadas.Count - 1), FiltroClases.Encimadas));
            }

            // 5. Repetidas: la misma persona docente con la misma materia y grupo dos veces.
            List<CargaCiclo> repetidas = revision.Repetidas
                .GroupBy(c => new { c.ProfesorId, c.GrupoId, c.MateriaId })
                .Select(g => g.First())
                .ToList();
            renglones.Add(repetidas.Count == 0
                ? Verde("No hay clases repetidas.")
                : Rojo("Repetida: " + repetidas[0].ProfesorCorto + " tiene dos veces " + repetidas[0].Nombre + ". Quita una."
                    + Mas(repetidas.Count - 1), FiltroClases.Repetidas));

            // 6. R6: la misma clase ya esta en Clases en esas fechas.
            if (datos.TernasCruzadas.Count == 0)
            {
                renglones.Add(Verde("Ninguna clase se cruza con otra igual que ya está en Clases."));
            }
            else
            {
                // Con "Ahora mismo", la que empezo hoy no se recorta (el horario de
                // ahora termina ayer y una clase no puede terminar antes de empezar):
                // con "Otro dia" (desde manana) si, hasta hoy.
                TernaCruzada primera = datos.TernasCruzadas[0];
                bool empezoHoy = ahora && primera.Desde <= desde;
                renglones.Add(Rojo(primera.Materia + " con " + primera.Grupo + " (" + primera.Profesor + ") ya está en Clases "
                    + HistorialCambios.Vigencia(primera.Desde, primera.Hasta)
                    + (empezoHoy
                        ? ": empezó hoy y «Ahora mismo» no la recorta. Quítala de aquí o aplícalo con «Otro día» (desde mañana termina hoy)."
                        : ": quítala de aquí o termina antes la otra.")
                    + Mas(datos.TernasCruzadas.Select(t => t.CargaId).Distinct().Count() - 1)));
            }

            // 7. Que tenga clases.
            renglones.Add(vivas.Count > 0 ? Verde("Tiene clases.") : Rojo("No tiene ninguna clase."));

            // 8. Fechas.
            string errorFechas = ErrorDeFechas(datos.Hoy, desde, hasta, fin, ahora);
            renglones.Add(errorFechas == null ? Verde("Las fechas están en orden.") : Rojo(errorFechas));

            // 9. Ninguna clase de ahora termina antes de su ultima sesion. Con
            // "Ahora mismo", lo de hoy no cuenta: paso antes de aplicar y se queda.
            SesionPosterior sesion = datos.SesionPosterior;
            if (ahora)
            {
                renglones.Add(sesion == null
                    ? Verde("Las clases de ahora no tienen sesiones después de hoy.")
                    : Rojo("No puede empezar ahora mismo: el " + Fecha(sesion.Fecha) + " ya "
                        + (sesion.EsAsistenciaDada ? "se dio asistencia en " : "hay una sesión de ") + sesion.Clase
                        + " con el horario de ahora. Aplícalo con «Otro día», después de esa fecha."));
            }
            else
            {
                renglones.Add(sesion == null
                    ? Verde("Las clases de ahora no tienen sesiones después del " + Fecha(fin) + ".")
                    : Rojo("El último día del horario de ahora no puede ser antes del " + Fecha(sesion.Fecha) + ": ese día "
                        + (sesion.EsAsistenciaDada ? "se dio asistencia en " : "hubo una sesión de ") + sesion.Clase + "."));
            }

            // 10. El nombre en el registro (solo si falla).
            string errorNombre = ErrorDeNombre(nombre, datos.NombresAplicadas);
            if (errorNombre != null)
            {
                renglones.Add(Rojo(errorNombre));
            }

            // 11. Sin horario: solo avisa.
            if (revision.SinHorario > 0)
            {
                renglones.Add(Ambar(revision.SinHorario == 1
                    ? "1 clase sin horario: se crea sin horas en ningún laboratorio."
                    : revision.SinHorario + " clases sin horario: se crean sin horas en ningún laboratorio.", FiltroClases.SinHorario));
            }

            // 11b. Lo que falta asignar: se aplica sin ello y pasa a Horario semanal (db/25).
            string faltaAsignar = TextoFaltaAsignar(leido, datos.ConHorasPorAsignar);
            if (faltaAsignar != null)
            {
                renglones.Add(Ambar(faltaAsignar));
            }

            // 12. La misma materia con el mismo grupo con dos personas docentes (o dos veces sin docente).
            List<List<CargaCiclo>> dobles = vivas
                .GroupBy(c => new { c.GrupoId, c.MateriaId })
                .Where(g => g.Count() > 1 && (g.Select(c => c.ProfesorId).Distinct().Count() > 1 || g.Any(c => !c.ProfesorId.HasValue)))
                .Select(g => g.ToList())
                .ToList();
            if (dobles.Count > 0)
            {
                List<CargaCiclo> doble = dobles[0];
                List<string> quienes = doble.Select(c => c.ProfesorId.HasValue ? c.ProfesorCorto : "sin docente").Distinct().ToList();
                renglones.Add(Ambar(doble.All(c => c.ProfesorId.HasValue)
                    ? doble[0].Nombre + " la tienen " + Cuantas(doble.Count) + " personas docentes (" + RevisionCiclo.Enumerar(quienes) + ")." + Mas(dobles.Count - 1)
                    : doble[0].Nombre + " está " + doble.Count + " veces (" + RevisionCiclo.Enumerar(quienes) + ")." + Mas(dobles.Count - 1),
                    FiltroClases.DosDocentes));
            }

            // 13. Cambios "desde una fecha" (db/20) que ya no llegan a darse.
            if (datos.CambiosQueNoLlegan.Count > 0)
            {
                CambioQueNoLlega primero = datos.CambiosQueNoLlegan[0];
                int n = datos.CambiosQueNoLlegan.Count;
                renglones.Add(Ambar((n == 1
                        ? "1 cambio de horario que pusiste desde una fecha posterior al " + Fecha(fin) + " no llegará a darse: "
                        : n + " cambios de horario que pusiste desde una fecha posterior al " + Fecha(fin) + " no llegarán a darse: ")
                    + primero.Materia + " con " + primero.Grupo + " desde el " + Fecha(primero.Desde)
                    + (n > 1 ? " y " + (n - 1) + " más." : ".")));
            }

            // 14. Sesiones extra despues del ultimo dia. Con "Ahora mismo" no hay
            // ultimo dia que cambiar: las de hoy y despues ya no cuentan.
            if (datos.ExtrasFuera.Count > 0)
            {
                ExtraFuera primera = datos.ExtrasFuera[0];
                int n = datos.ExtrasFuera.Count;
                string queHacer = ahora
                    ? (n == 1 ? " Ya no cuenta; lo que ya se registró en ella se queda." : " Ya no cuentan; lo que ya se registró en ellas se queda.")
                    : (n == 1 ? " Quítala en Cambios de un día o cambia el último día." : " Quítalas en Cambios de un día o cambia el último día.");
                string termina = ahora ? "ayer" : "el " + Fecha(fin);
                renglones.Add(Ambar(n == 1
                    ? "1 sesión extra queda fuera: " + primera.Materia + " con " + primera.Grupo + " el " + Fecha(primera.Fecha)
                      + " (su clase termina " + termina + ")." + queHacer
                    : n + " sesiones extra quedan fuera: " + primera.Materia + " con " + primera.Grupo + " el " + Fecha(primera.Fecha)
                      + " y " + (n - 1) + " más (sus clases terminan " + termina + ")." + queHacer));
            }

            // 15. Un ultimo dia que ya paso (con "Ahora mismo" es ayer: lo dicen los de abajo).
            if (!ahora && fin < datos.Hoy.Date && errorFechas == null)
            {
                renglones.Add(Ambar((fin.AddDays(1) >= datos.Hoy.Date
                        ? "Esa fecha ya pasó: desde hoy esas clases dejan de contar en Faltas y en Alertas"
                        : "Esa fecha ya pasó: del " + Fecha(fin.AddDays(1)) + " a hoy esas clases dejan de contar en Faltas y en Alertas")
                    + ", y hasta el " + Fecha(desde) + " sus códigos ya no abren sesión."));
            }

            // 16. Empieza manana.
            if (!ahora && desde == datos.Hoy.Date.AddDays(1))
            {
                renglones.Add(Ambar("Empieza mañana: aplícalo al menos un día de clases antes, para que cada equipo guarde el horario nuevo en su copia local."));
            }

            // 16b. "Ahora mismo": como lo toman los equipos (ambar), que pasa con las
            // clases de hoy y que lo ya registrado se queda (gris).
            if (ahora && errorFechas == null)
            {
                // Las de hoy que ya terminaron dejan de contar: eso avisa (ambar); si no hay, informa.
                DateTime momento = datos.AhoraEstimado;
                string deHoyTexto = TextoDeHoy(datos, momento);
                renglones.Add(ContarDeHoy(datos, momento).Terminadas > 0 ? Ambar(deHoyTexto) : Gris(deHoyTexto));
                renglones.Add(Ambar(AvisoEquipos));
                if (datos.LaboratoriosConGeneracion.Count > 0)
                {
                    renglones.Add(Ambar(TextoCodigosGenerados(datos.LaboratoriosConGeneracion)));
                }

                renglones.Add(Gris(TextoRegistradoHoy(datos, momento)));
            }

            // 17 y 19. Los cambios a mitad del ciclo desde una fecha (VigenciaFranjas.ConFecha):
            // desde 2026-10-31 siempre con db/20, ya sin casilla en Ajustes. Sin la 20, que falta.
            renglones.Add(datos.ConFecha
                ? Verde("Los cambios a mitad del ciclo aplican desde una fecha: lo que ya pasó no se reescribe.")
                : Ambar("Los cambios a mitad del ciclo todavía no aplican desde una fecha (falta aplicar db/20_vigencia_franjas.sql en la base): si a mitad del ciclo cambias una hora, también cambian las semanas que ya pasaron."));

            // 18. Las clases que ya estan en Clases en esas fechas (informa). Con
            // "Ahora mismo", las que empezaron hoy no se recortan y siguen junto al
            // horario nuevo: eso avisa (ambar).
            List<FranjaInfo> programadas = leido.YaProgramadas.GroupBy(f => f.ClaseId).Select(g => g.First()).ToList();
            List<FranjaInfo> deHoy = ahora ? programadas.Where(f => f.ClaseDesde.Date <= desde).ToList() : new List<FranjaInfo>();
            if (deHoy.Count > 0)
            {
                programadas = programadas.Except(deHoy).ToList();
                List<string> nombres = deHoy.Take(2).Select(f => f.Materia + " con " + f.Grupo + " (" + f.ProfesorCorto + ")").ToList();
                renglones.Add(Ambar((deHoy.Count == 1
                        ? "1 clase que ya está en Clases empezó hoy y «Ahora mismo» no la recorta: sigue junto con el horario nuevo: "
                        : deHoy.Count + " clases que ya están en Clases empezaron hoy y «Ahora mismo» no las recorta: siguen junto con el horario nuevo: ")
                    + RevisionCiclo.Enumerar(nombres) + (deHoy.Count > 2 ? " y " + (deHoy.Count - 2) + " más." : ".")));
            }

            if (programadas.Count > 0)
            {
                List<string> nombres = programadas.Take(2).Select(f => f.Materia + " con " + f.Grupo + " (" + f.ProfesorCorto + ")").ToList();
                renglones.Add(Gris((programadas.Count == 1
                        ? "1 clase que ya está en Clases también vale en esas fechas: "
                        : programadas.Count + " clases que ya están en Clases también valen en esas fechas: ")
                    + RevisionCiclo.Enumerar(nombres) + (programadas.Count > 2 ? " y " + (programadas.Count - 2) + " más." : ".")));
            }

            // Rojo, ambar, verde y gris; dentro de cada uno, en el orden de arriba (OrderBy es estable).
            return renglones.OrderBy(r => (int)r.Nivel).ToList();
        }

        /// <summary>
        /// El primer error de las fechas, o null: el primer dia del horario nuevo
        /// despues de hoy (con "Ahora mismo", hoy, y el ultimo del de ahora ayer),
        /// el ultimo no antes del primero y el ultimo dia del horario de ahora
        /// antes del primero del nuevo.
        /// </summary>
        public static string ErrorDeFechas(DateTime hoy, DateTime desde, DateTime hasta, DateTime fin, bool empiezaAhora = false)
        {
            if (empiezaAhora)
            {
                if (desde.Date != hoy.Date || fin.Date != hoy.Date.AddDays(-1))
                {
                    return "«Ahora mismo» empieza hoy (" + Fecha(hoy) + "), y esta revisión era de otro día: vuelve a revisar.";
                }
            }
            else if (desde.Date <= hoy.Date)
            {
                return "El primer día del nuevo horario tiene que ser después de hoy (" + Fecha(hoy) + "). Para que empiece hoy, elige «Ahora mismo».";
            }

            if (hasta.Date < desde.Date)
            {
                return "El último día del nuevo horario no puede ser antes del primero.";
            }

            if (fin.Date >= desde.Date)
            {
                return "El último día del horario de ahora tiene que ser antes del primer día del nuevo (" + Fecha(desde) + ").";
            }

            return null;
        }

        /// <summary>
        /// "Ahora mismo": que pasa hoy con el horario nuevo a esa hora. "A las
        /// 12:40 de hoy (domingo 01/11/2026), 3 clases del horario nuevo ya
        /// terminaron: ... La que esta en curso y las 2 que faltan cuentan ..."
        /// </summary>
        public static string TextoDeHoy(DatosDeAplicacion datos, DateTime ahora)
        {
            ClasesDeHoy hoy = ContarDeHoy(datos, ahora);
            string dia = HistorialCambios.DiaYFecha(datos.Hoy);
            if (hoy.Total == 0)
            {
                return "Hoy, " + dia + ", el horario nuevo no tiene clases: empieza a contar desde su próximo día de clases.";
            }

            string texto = "A las " + ahora.ToString("HH:mm", Invariante) + " de hoy (" + dia + "), "
                + (hoy.Terminadas == 0 ? "ninguna clase de hoy del horario nuevo ha terminado."
                    : hoy.Terminadas == 1
                    ? "1 clase de hoy del horario nuevo ya terminó: no cuenta como falta ni sale en Alertas "
                      + "(queda como «no tomar en cuenta»; en Faltas, «Volver a contar» la devuelve)."
                    : hoy.Terminadas + " clases de hoy del horario nuevo ya terminaron: no cuentan como faltas ni salen en Alertas "
                      + "(quedan como «no tomar en cuenta»; en Faltas, «Volver a contar» las devuelve).");
            string cuentan = QuienesCuentan(hoy);
            return cuentan == null
                ? texto + " Hoy ya no le quedan clases: empieza a contar desde su próximo día de clases."
                : texto + " " + cuentan + " con el horario nuevo: quien llegue registra en la clase nueva.";
        }

        /// <summary>
        /// "La que esta en curso y las 2 que faltan cuentan", "La que falta
        /// cuenta"...; null si hoy ya no le queda ninguna.
        /// </summary>
        public static string QuienesCuentan(ClasesDeHoy hoy)
        {
            List<string> cuentan = new List<string>();
            if (hoy.EnCurso > 0)
            {
                cuentan.Add(hoy.EnCurso == 1 ? "la que está en curso" : "las " + hoy.EnCurso + " que están en curso");
            }

            if (hoy.PorVenir > 0)
            {
                cuentan.Add(hoy.PorVenir == 1 ? "la que falta" : "las " + hoy.PorVenir + " que faltan");
            }

            if (cuentan.Count == 0)
            {
                return null;
            }

            string sujeto = RevisionCiclo.Enumerar(cuentan);
            return char.ToUpper(sujeto[0], Invariante) + sujeto.Substring(1) + (hoy.EnCurso + hoy.PorVenir == 1 ? " cuenta" : " cuentan");
        }

        /// <summary>
        /// "Ahora mismo": lo ya registrado hoy con el horario de ahora, que se
        /// queda y cuenta una sola vez en las listas de asistencia (AsistenciaDatos):
        /// lo de una clase cuya misma persona docente, grupo y materia sigue en el
        /// horario nuevo, en la lista de su clase nueva; lo de las demas, en la de
        /// su clase de antes. Si a esa hora hay una clase en curso, desde que se
        /// aplique se registra con el horario nuevo.
        /// </summary>
        public static string TextoRegistradoHoy(DatosDeAplicacion datos, DateTime ahora)
        {
            if (datos.SesionesDeHoy == 0 && datos.DadasDeHoy == 0)
            {
                return "Hoy todavía no hay nada registrado con el horario de ahora.";
            }

            string enCurso = datos.SesionesDeHoy > 0 && ContarDeHoy(datos, ahora).EnCurso > 0
                ? " Si alguna es de una clase que sigue en curso, desde que lo apliques se registra con el horario nuevo."
                : "";
            string siguen = Registrado(datos.SesionesDeHoySiguen, datos.BitacorasDeHoySiguen, datos.DadasDeHoySiguen);
            string noSiguen = Registrado(datos.SesionesDeHoy - datos.SesionesDeHoySiguen, datos.BitacorasDeHoy - datos.BitacorasDeHoySiguen,
                datos.DadasDeHoy - datos.DadasDeHoySiguen);
            const string EnLaNueva = "en la lista de su clase nueva (la misma persona docente, grupo y materia)";
            string donde = noSiguen == null ? ", " + EnLaNueva + ": " + siguen
                : siguen == null ? ", en la lista de su clase de antes (no sigue en el horario nuevo): " + noSiguen
                : ": " + siguen + ", " + EnLaNueva + "; " + noSiguen + ", en la de su clase de antes, que no sigue en el horario nuevo";

            // Sus clases ya terminan ayer: en Faltas, su sesion de hoy cuenta en la clase
            // nueva de su terna a esa hora (HorariosDatos.EmparejarSesiones), aunque este
            // en otro laboratorio; si no hay (no sigue, o ya termino), va suelta.
            string enFaltas = datos.SesionesDeHoy == 0 ? ""
                : ". En Faltas, " + (datos.SesionesDeHoy == 1 ? "cuenta" : "cuentan") + " en la clase nueva de su misma persona docente, grupo y materia si se cruza con esas horas y aún no termina; "
                  + "si no, " + (datos.SesionesDeHoy == 1 ? "sale como «sesión fuera del horario»" : "salen como «sesiones fuera del horario»");
            return "Lo registrado hoy con el horario de ahora se queda y cuenta una sola vez" + donde + enFaltas + "." + enCurso;
        }

        /// <summary>
        /// "Falta asignar" de la plantilla, con cuanto y de que clases (hasta dos), y
        /// que pasa al aplicar; o null si no falta nada.
        /// </summary>
        private static string TextoFaltaAsignar(CicloLeido leido, bool conHorasPorAsignar)
        {
            if (leido.PorAsignar.Count == 0)
            {
                return null;
            }

            List<string> porClase = leido.PorAsignar.GroupBy(p => p.CargaId)
                .Select(g =>
                {
                    CargaCiclo carga = leido.Carga(g.Key);
                    return (carga == null ? "una clase" : carga.Nombre) + " (" + HorariosDatos.Duracion(g.Sum(p => p.Minutos)) + ")";
                })
                .ToList();
            int resto = porClase.Count - 2;
            return "Falta asignar " + HorariosDatos.Duracion(leido.MinutosPorAsignar) + " en este horario: "
                + RevisionCiclo.Enumerar(porClase.Take(2).ToList()) + (resto > 0 ? " y " + resto + " más" : "")
                + (conHorasPorAsignar
                    ? ". Al aplicarlo, esas horas pasan a «Falta asignar» de Horario semanal."
                    : ". Esta base aún no tiene «Falta asignar» en Horario semanal (falta db/25): al aplicarlo, esas horas ya no se verán.");
        }

        /// <summary>
        /// "Ahora mismo" en laboratorios con generacion de codigos: los codigos salen
        /// de la franja y las clases nuevas tienen las suyas.
        /// </summary>
        private static string TextoCodigosGenerados(List<string> laboratorios)
        {
            List<string> nombres = laboratorios.Select(EstiloHorarios.NombreLaboratorio).ToList();
            return "Generación de códigos en " + RevisionCiclo.Enumerar(nombres.Take(2).ToList()) + (nombres.Count > 2 ? " y " + (nombres.Count - 2) + " más" : "")
                + ": en cuanto lo apliques, los códigos ya dictados dejan de valer, también el de una clase en curso. Hay que dictar los nuevos.";
        }

        /// <summary>"2 sesiones con 3 bitácoras y 1 asistencia dada", o null si no hay nada.</summary>
        private static string Registrado(int sesiones, int bitacoras, int dadas)
        {
            List<string> partes = new List<string>();
            if (sesiones > 0)
            {
                partes.Add((sesiones == 1 ? "1 sesión" : sesiones + " sesiones")
                    + (bitacoras <= 0 ? " sin bitácoras" : bitacoras == 1 ? " con 1 bitácora" : " con " + bitacoras + " bitácoras"));
            }

            if (dadas > 0)
            {
                partes.Add(dadas == 1 ? "1 asistencia dada" : dadas + " asistencias dadas");
            }

            return partes.Count == 0 ? null : RevisionCiclo.Enumerar(partes);
        }

        /// <summary>El error del nombre en el registro, o null: vacio, largo o igual que otra aplicada (sin acentos ni mayusculas).</summary>
        private static string ErrorDeNombre(string nombre, IEnumerable<string> otrasAplicadas)
        {
            string limpio = (nombre ?? "").Trim();
            if (limpio.Length == 0)
            {
                return "Escribe el nombre con que queda en el registro.";
            }

            if (limpio.Length > CicloPreliminarDatos.LargoNombre)
            {
                return "El nombre en el registro puede tener hasta " + CicloPreliminarDatos.LargoNombre + " letras.";
            }

            string clave = Clave(limpio);
            string igual = (otrasAplicadas ?? Enumerable.Empty<string>()).FirstOrDefault(o => Clave(o) == clave);
            return igual == null ? null : "Ya hay una plantilla aplicada que se llama «" + igual + "»: cambia el nombre con que se guarda.";
        }

        /// <summary>Como CicloPreliminarDatos compara los nombres: sin acentos, sin mayusculas y con un solo espacio.</summary>
        private static string Clave(string nombre)
        {
            return Regex.Replace(EstiloAdmin.Normalizar((nombre ?? "").Trim()), @"\s+", " ");
        }

        private static string Fecha(DateTime fecha)
        {
            return fecha.ToString("dd/MM/yyyy", Invariante);
        }

        /// <summary>" Hay 2 mas." (con su acento; vacio si no hay mas), detras de una frase que ya acaba en punto.</summary>
        private static string Mas(int cuantas)
        {
            return cuantas <= 0 ? "" : " Hay " + cuantas + " más.";
        }

        private static string Cuantas(int n)
        {
            switch (n)
            {
                case 2:
                    return "dos";
                case 3:
                    return "tres";
                case 4:
                    return "cuatro";
                default:
                    return n.ToString(Invariante);
            }
        }

        private static RenglonRevision Rojo(string texto, FiltroClases ver = FiltroClases.Ninguno)
        {
            return new RenglonRevision { Nivel = NivelRevision.Rojo, Texto = texto, Ver = ver };
        }

        private static RenglonRevision Ambar(string texto, FiltroClases ver = FiltroClases.Ninguno)
        {
            return new RenglonRevision { Nivel = NivelRevision.Ambar, Texto = texto, Ver = ver };
        }

        private static RenglonRevision Verde(string texto)
        {
            return new RenglonRevision { Nivel = NivelRevision.Verde, Texto = texto };
        }

        private static RenglonRevision Gris(string texto)
        {
            return new RenglonRevision { Nivel = NivelRevision.Gris, Texto = texto };
        }
    }
}
