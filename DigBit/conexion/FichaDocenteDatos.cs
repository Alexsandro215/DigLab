using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DigBit.conexion
{
    // =========================================================================
    // La ficha del docente ("Ver todo lo suyo", 2026-10-31) sin pantalla: arma lo
    // que se ensena desde UN resultado de Estadisticas calculado con Docente = el
    // (EstadisticasDatos.Resolver con docenteFijo). No lee ni escribe nada en la
    // base: cada numero es el mismo de Estadisticas, Horarios > Faltas y Mis grupos.
    // =========================================================================

    /// <summary>El color de la barrita de un renglon de "Para revisar".</summary>
    internal enum TonoPendiente
    {
        Rojo,
        Ambar,
        Naranja,
        Gris,
        Verde
    }

    /// <summary>A donde lleva un renglon de "Para revisar" (doble clic, Enter o "Ver ›").</summary>
    internal enum DestinoPendiente
    {
        Ninguno,
        /// <summary>Sus clases y faltas, con "Sin registro".</summary>
        SinRegistro,
        /// <summary>Sus clases y faltas, con "Canceladas".</summary>
        Canceladas,
        /// <summary>Sus clases y faltas, con "Aun no pasan o no cuentan".</summary>
        NoCuentan,
        /// <summary>Sus grupos, con esa clase elegida.</summary>
        Clase,
        /// <summary>La lista de "Falta asignar" (solo para ver).</summary>
        FaltaAsignar,
        Fallas,
        Dadas
    }

    /// <summary>Lo que ensena la tabla de "Sus clases dia por dia" (el "Mostrar" de arriba).</summary>
    internal enum MostrarDias
    {
        Todas,
        SinRegistro,
        Canceladas,
        SeDieron,
        AunNoONoCuentan
    }

    /// <summary>Un renglon de "Para revisar" (reglas R1 a R10 de la especificacion), en tercera persona y sin juicios.</summary>
    internal sealed class PendienteDeDocente
    {
        /// <summary>"R1" a "R10".</summary>
        public string Regla = "";
        public TonoPendiente Tono;
        public string Texto = "";
        /// <summary>La ayuda del renglon, o null.</summary>
        public string Ayuda;
        public DestinoPendiente Destino;
        /// <summary>Con Destino = Clase, cual.</summary>
        public int ClaseId;
        /// <summary>El numero del renglon (sin registro, bajo el 80 %, minutos...): la prueba lo compara con su fuente.</summary>
        public int Numero;

        public bool SeAbre
        {
            get { return Destino != DestinoPendiente.Ninguno; }
        }
    }

    /// <summary>Todo lo de la ficha de un docente en un periodo, ya armado (FichaDocenteDatos.Armar).</summary>
    internal sealed class DatosDeLaFicha
    {
        public int DocenteId;
        public LecturaEstadisticas Lectura;
        /// <summary>Calculado con Docente = el (nunca de toda la escuela: Armar lo comprueba).</summary>
        public ResultadoEstadisticas R;
        /// <summary>Su fila de la pestana Docentes de Estadisticas, o null sin clases del horario en el periodo.</summary>
        public FilaDocente Fila;
        /// <summary>Sus clases dia por dia (Fila.SusClasesDia), de la mas reciente a la mas vieja.</summary>
        public List<ClaseDiaDocente> Dias = new List<ClaseDiaDocente>();
        /// <summary>El mes de Faltas: sus clases-dia de cada fecha, con ResumenDia.Contar (todos sus laboratorios).</summary>
        public Dictionary<DateTime, ResumenDia> Mes = new Dictionary<DateTime, ResumenDia>();
        /// <summary>Las cuentas del "Mostrar".</summary>
        public int Todas;
        public int SinRegistro;
        public int Canceladas;
        public int SeDieron;
        public int AunNoONoCuentan;
        /// <summary>Lo que reporto el desde su panel (db/15), lo mas reciente primero.</summary>
        public List<FilaReporteDocente> SusReportes = new List<FilaReporteDocente>();
        /// <summary>Las asistencias que dio (db/22), lo mas reciente primero.</summary>
        public List<FilaAsistenciaDada> SusDadas = new List<FilaAsistenciaDada>();
        /// <summary>Lo suyo de "Falta asignar" (db/25) si el periodo llega a hoy; null si no aplica.</summary>
        public List<HoraPorAsignar> PorAsignar;
        /// <summary>El periodo llega a hoy (no es un ciclo ni unas fechas que ya pasaron).</summary>
        public bool LlegaAHoy;
        public List<PendienteDeDocente> Pendientes = new List<PendienteDeDocente>();
        public string LineaClases = "";
        public string LineaGrupos = "";
        public string LineaFallas = "";
        public string LineaDadas = "";
        /// <summary>La linea de abajo: los dias, el fin del ciclo y la hora de la lectura.</summary>
        public string LineaPeriodo = "";
        /// <summary>La misma, corta (cuando la larga no cabe; la larga va en su ayuda).</summary>
        public string LineaPeriodoCorta = "";
        /// <summary>Solo los dias (cuando tampoco cabe la corta).</summary>
        public string LineaPeriodoMinima = "";
        /// <summary>Sin clases del horario en el periodo: lo que dice el Resumen; null si las tiene.</summary>
        public string SinClases;

        public int MinutosPorAsignar
        {
            get { return PorAsignar == null ? 0 : PorAsignar.Sum(p => p.Minutos); }
        }
    }

    internal static class FichaDocenteDatos
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Lo de la ficha desde un resultado de Estadisticas calculado con Docente = docente.
        /// Lanza si r es de otro filtro: nunca se pintan cifras de la escuela con su nombre.
        /// </summary>
        /// <param name="porAsignar">LeerHorasPorAsignar (todo; aqui se toma lo suyo), o null sin db/25 o si no se leyo.</param>
        public static DatosDeLaFicha Armar(LecturaEstadisticas lectura, ResultadoEstadisticas r, int docente, ProfesorInfo profesor, LecturaEnlaces enlaces,
            List<HoraPorAsignar> porAsignar)
        {
            if (lectura == null || r == null)
            {
                throw new ArgumentNullException(lectura == null ? "lectura" : "r");
            }

            if (r.Filtro == null || r.Filtro.Docente != docente || docente == 0 || r.Filtro.Laboratorio != 0 || r.Filtro.Grupo != 0)
            {
                throw new InvalidOperationException("La ficha del docente " + docente + " recibió cifras de otro filtro ("
                    + (r.Filtro == null ? "sin filtro" : r.Filtro.Laboratorio + "/" + r.Filtro.Docente + "/" + r.Filtro.Grupo) + "): no se ensenan.");
            }

            DatosDeLaFicha d = new DatosDeLaFicha { DocenteId = docente, Lectura = lectura, R = r };
            d.Fila = r.Docentes.FirstOrDefault(f => f.DocenteId == docente);
            d.Dias = d.Fila == null ? new List<ClaseDiaDocente>() : d.Fila.SusClasesDia.ToList();
            d.Mes = Mes(d.Dias);
            d.Todas = d.Dias.Count;
            d.SinRegistro = d.Dias.Count(x => Pasa(x, MostrarDias.SinRegistro));
            d.Canceladas = d.Dias.Count(x => Pasa(x, MostrarDias.Canceladas));
            d.SeDieron = d.Dias.Count(x => Pasa(x, MostrarDias.SeDieron));
            d.AunNoONoCuentan = d.Dias.Count(x => Pasa(x, MostrarDias.AunNoONoCuentan));
            d.SusReportes = r.ReportesDocentes.Where(x => x.DocenteId == docente)
                .OrderByDescending(x => x.Dia).ThenByDescending(x => x.RegistradoEn ?? DateTime.MinValue).ThenByDescending(x => x.Id).ToList();
            d.SusDadas = r.AsistenciasDadas.Where(x => x.DocenteId == docente)
                .OrderByDescending(x => x.Fecha).ThenByDescending(x => x.DadaEn).ThenByDescending(x => x.Id).ToList();
            d.LlegaAHoy = r.Hasta == lectura.Hoy;
            d.PorAsignar = porAsignar == null || !d.LlegaAHoy ? null : porAsignar.Where(p => p.ProfesorId == docente).ToList();
            d.LineaClases = LineaClases(d);
            d.LineaGrupos = LineaGrupos(r);
            d.LineaFallas = LineaFallas(d);
            d.LineaDadas = LineaDadas(d.SusDadas);
            d.LineaPeriodo = LineaPeriodo(r, lectura);
            d.LineaPeriodoCorta = EstadisticasDatos.DelAl(r.Desde, r.Hasta) + (r.Hasta == lectura.Hoy ? " (hoy)" : "") + ". Leído a las "
                + lectura.AhoraServidor.ToString("HH:mm", Inv) + ".";
            d.LineaPeriodoMinima = EstadisticasDatos.DelAl(r.Desde, r.Hasta) + ".";
            if (r.Clases.Count == 0)
            {
                d.SinClases = "No tuvo clases del horario en estas fechas."
                    + (profesor != null && profesor.UltimaSesion.HasValue ? " Su última sesión fue el " + EstadisticasDatos.Fecha(profesor.UltimaSesion.Value) + "."
                        : profesor != null ? " Aún no tiene sesiones." : "")
                    + (r.Cifras.BitacorasRegistradas > 0
                        ? " En sus sesiones de estas fechas fuera del horario hay " + EstadisticasDatos.Contar(r.Cifras.BitacorasRegistradas, "bitácora", "bitácoras") + "."
                        : "");
            }

            d.Pendientes = Pendientes(d, profesor, enlaces);
            return d;
        }

        /// <summary>Esa clase-dia entra con ese "Mostrar". Las cuatro de abajo suman Todas.</summary>
        public static bool Pasa(ClaseDiaDocente dia, MostrarDias cual)
        {
            switch (cual)
            {
                case MostrarDias.SinRegistro:
                    return dia.SinRegistro;
                case MostrarDias.Canceladas:
                    return dia.Cancelada;
                case MostrarDias.SeDieron:
                    return dia.SeDio;
                case MostrarDias.AunNoONoCuentan:
                    return !dia.SinRegistro && !dia.Cancelada && !dia.SeDio;
                default:
                    return true;
            }
        }

        /// <summary>El mes de Faltas con sus clases: cada fecha, con ResumenDia.Contar de sus estados (todos sus laboratorios juntos).</summary>
        public static Dictionary<DateTime, ResumenDia> Mes(IEnumerable<ClaseDiaDocente> dias)
        {
            Dictionary<DateTime, ResumenDia> mes = new Dictionary<DateTime, ResumenDia>();
            foreach (ClaseDiaDocente dia in dias)
            {
                ResumenDia resumen;
                if (!mes.TryGetValue(dia.Fecha.Date, out resumen))
                {
                    resumen = new ResumenDia();
                    mes[dia.Fecha.Date] = resumen;
                }

                resumen.Contar(dia.Estado);
            }

            return mes;
        }

        // =====================================================================
        // Lineas
        // =====================================================================

        private static string LineaClases(DatosDeLaFicha d)
        {
            FilaDocente f = d.Fila;
            if (f == null)
            {
                return "No tuvo clases del horario en estas fechas.";
            }

            string canceladas = f.Canceladas > 0 ? "; " + EstadisticasDatos.Contar(f.Canceladas, "cancelada", "canceladas") : "";
            if (f.QueContaban == 0)
            {
                return "Ninguna de sus clases del horario ya pasó y contaba en estas fechas" + canceladas + ".";
            }

            return "Se dieron " + EstadisticasDatos.XdeN(f.SeDieron, f.QueContaban) + " (" + EstadisticasDatos.Porcentaje(f.FraccionSeDieron * 100.0) + "): "
                + EstadisticasDatos.Miles(f.ConBitacoras) + " con bitácoras"
                + (f.ConDadas > 0 ? " y " + EstadisticasDatos.Miles(f.ConDadas) + " con asistencias que dio" : "")
                + "; " + EstadisticasDatos.Miles(f.SinRegistro) + " sin registro" + canceladas + ". "
                + "Faltas = clases sin registro, como en Horarios > Faltas: puede que nadie registrara.";
        }

        private static string LineaGrupos(ResultadoEstadisticas r)
        {
            CifrasEstadisticas c = r.Cifras;
            string asistencia = c.Veces == 0 ? "aún sin días de clase que contar" : "asistencia de sus estudiantes " + EstadisticasDatos.Porcentaje(100.0 * c.Entraron / c.Veces);
            return EstadisticasDatos.Contar(r.Clases.Count, "clase", "clases") + "; " + asistencia + ". "
                + (r.CicloEntero ? "La última columna es el derecho a evaluación. " : "Cuentan solo los días de estas fechas; el derecho a evaluación es del ciclo entero. ")
                + "Solo cuentan quienes registraron alguna vez: DigBit no tiene la lista oficial del grupo.";
        }

        private static string LineaFallas(DatosDeLaFicha d)
        {
            CifrasEstadisticas c = d.R.Cifras;
            if (c.FallasReportadas == 0)
            {
                return "Ninguna falla reportada en sus clases en estas fechas"
                    + (d.R.ConReportesDocentes ? "" : " (sin lo que reportan los docentes desde su panel: falta una actualización de la base, db/15)") + ".";
            }

            string deDocentes = !d.R.ConReportesDocentes ? ""
                : c.FallasDeDocentes == 0 ? "; desde su panel no reportó ninguna"
                : c.FallasDeDocentes == d.SusReportes.Count ? " y " + EstadisticasDatos.Miles(c.FallasDeDocentes) + " que reportó desde su panel"
                : " y " + EstadisticasDatos.Contar(c.FallasDeDocentes, "reporte de docente", "reportes de docentes") + " (" + EstadisticasDatos.Miles(d.SusReportes.Count) + " suyos)";
            return EstadisticasDatos.Contar(c.FallasReportadas, "falla", "fallas") + " en sus clases: " + EstadisticasDatos.Miles(c.FallasDeEstudiantes)
                + " en bitácoras de sus estudiantes" + deDocentes + ".";
        }

        private static string LineaDadas(List<FilaAsistenciaDada> dadas)
        {
            if (dadas.Count == 0)
            {
                return "No dio asistencias sin bitácora en estas fechas.";
            }

            int estudiantes = dadas.Select(x => x.AlumnoId).Distinct().Count();
            int sinInternet = dadas.Count(x => x.Motivo == AsistenciasDadas.SinInternet);
            return EstadisticasDatos.Contar(dadas.Count, "asistencia", "asistencias") + " a " + EstadisticasDatos.Contar(estudiantes, "estudiante", "estudiantes")
                + (sinInternet > 0 ? "; " + EstadisticasDatos.Miles(sinInternet) + " porque su equipo no tenía internet" : "") + ". Las anuladas no cuentan.";
        }

        /// <summary>"Del 30/09/2026 al 31/10/2026 (hoy); el ciclo termina el 20/01/2027. Leido a las 02:15 (hora del servidor)."</summary>
        public static string LineaPeriodo(ResultadoEstadisticas r, LecturaEstadisticas l)
        {
            string fin = r.Filtro.Tipo == TipoPeriodo.Ciclo && r.Ciclo != null ? "; " + EstadisticasDatos.FinDeEsteCiclo(r.Ciclo, l.Hoy)
                : r.Filtro.Tipo == TipoPeriodo.CicloPasado ? (r.Recortado ? " (sus últimos días)" : " (ciclo completo)")
                : r.Filtro.Tipo == TipoPeriodo.Plantilla ? "; " + EstadisticasDatos.MinusculaInicial(EstadisticasDatos.NombreDelPeriodo(r))
                : "";
            return EstadisticasDatos.DelAl(r.Desde, r.Hasta) + (r.Hasta == l.Hoy ? " (hoy)" : "") + fin + "."
                + (r.Filtro.AvisoDelPeriodo != null ? " " + r.Filtro.AvisoDelPeriodo : "")
                + " Leído a las " + l.AhoraServidor.ToString("HH:mm", Inv) + " (hora del servidor).";
        }

        /// <summary>
        /// La ayuda de "bitacoras registradas" en la ficha. La de Estadisticas (CifrasEstadisticas.Bitacoras)
        /// dice que entran el uso libre y lo de fuera del horario, pero con un docente el uso libre nunca
        /// entra (no tiene docente; ElegirClasesYSesiones): aqui, solo lo suyo.
        /// </summary>
        public static string AyudaBitacoras(ResultadoEstadisticas r)
        {
            return "Cada vez que un estudiante registró su bitácora en una de sus sesiones del periodo, también fuera del horario. "
                + "El uso libre no entra: no tiene docente."
                + (r != null && r.ConDadas ? " No cuentan las asistencias que dio: no son bitácoras." : "");
        }

        /// <summary>
        /// Lo que dice cada cifra de la ficha debajo de su ayuda. No "las mismas de Estadisticas": arriba,
        /// Estadisticas ensena siempre las de todo el TESCHI (no cambian con los filtros de sus apartados).
        /// </summary>
        public static string ComoEnEstadisticas(string nombre)
        {
            return "Se calcula igual que en Estadísticas, con Docente = " + nombre + " y este periodo. "
                + "Las cinco cifras de arriba de Estadísticas son de todo el TESCHI: no cambian con ese filtro.";
        }

        /// <summary>
        /// "No. de empleado 104523 · Cuenta activa · Enlazado con el SIIA desde el 12/10/2026 ·
        /// 4 clases vigentes en Computo 1 y MULTI. F5 actualiza." Sin correo ni codigo.
        /// </summary>
        public static string Subtitulo(ProfesorInfo p, LecturaEnlaces enlaces)
        {
            List<string> partes = new List<string>();
            partes.Add("No. de empleado " + (string.IsNullOrWhiteSpace(p.Matricula) ? EstadisticasDatos.SinDato : p.Matricula.Trim()));
            partes.Add(p.Anonimizada ? "Cuenta anonimizada"
                : p.Activo ? "Cuenta activa"
                : "Cuenta de baja" + (p.BajaEn.HasValue ? " desde el " + EstadisticasDatos.Fecha(p.BajaEn.Value) : ""));
            DateTime enlazado;
            if (enlaces != null && enlaces.Disponible)
            {
                if (enlaces.Enlazados.TryGetValue(p.Id, out enlazado))
                {
                    partes.Add("Enlazado con el SIIA desde el " + EstadisticasDatos.Fecha(enlazado));
                }
                else if (PidioEnlace(p, enlaces))
                {
                    partes.Add("Pidió enlace con el SIIA");
                }
            }

            partes.Add(p.ClasesActivas == 0 ? "Sin clases vigentes"
                : EstadisticasDatos.Contar(p.ClasesActivas, "clase vigente", "clases vigentes")
                    + (string.IsNullOrWhiteSpace(p.Laboratorios) ? "" : " en " + p.Laboratorios.Trim()));
            return string.Join(" · ", partes) + ". F5 actualiza.";
        }

        private static bool PidioEnlace(ProfesorInfo p, LecturaEnlaces enlaces)
        {
            return enlaces != null && enlaces.Disponible && p.Activo && !enlaces.Enlazados.ContainsKey(p.Id)
                && enlaces.Pendientes.Any(s => EnlacesSiia.Coincidencia(s, p).Length > 0);
        }

        // =====================================================================
        // Para revisar (R1 a R10)
        // =====================================================================

        public static List<PendienteDeDocente> Pendientes(DatosDeLaFicha d, ProfesorInfo profesor, LecturaEnlaces enlaces)
        {
            List<PendienteDeDocente> lista = new List<PendienteDeDocente>();
            ResultadoEstadisticas r = d.R;
            FilaDocente f = d.Fila;

            // R1: clases sin registro, y si muchas son del mismo bloque de la semana.
            if (f != null && f.SinRegistro > 0)
            {
                List<ClaseDiaDocente> sinRegistro = d.Dias.Where(x => x.SinRegistro).OrderByDescending(x => x.Fecha).ThenByDescending(x => x.Inicio).ToList();
                DateTime ultima = sinRegistro.Count > 0 ? sinRegistro[0].Fecha : r.Hasta;
                string texto = f.SinRegistro == 1 ? "1 clase sin registro: la del " + EstadisticasDatos.Fecha(ultima) + "."
                    : EstadisticasDatos.Miles(f.SinRegistro) + " clases sin registro; la última, el " + EstadisticasDatos.Fecha(ultima) + ".";
                IGrouping<string, ClaseDiaDocente> mismo = sinRegistro
                    .GroupBy(x => ((int)x.Fecha.DayOfWeek).ToString(Inv) + "|" + x.Inicio.Ticks.ToString(Inv) + "|" + x.Fin.Ticks.ToString(Inv) + "|" + x.LaboratorioId.ToString(Inv))
                    .OrderByDescending(g => g.Count()).FirstOrDefault();
                if (mismo != null && mismo.Count() >= 2 && 2 * mismo.Count() >= f.SinRegistro && mismo.Count() < f.SinRegistro)
                {
                    ClaseDiaDocente una = mismo.First();
                    texto += " " + EstadisticasDatos.Miles(mismo.Count()) + " son " + LosDias(una.Fecha.DayOfWeek) + " de " + HorariosDatos.Hora(una.Inicio)
                        + " a " + HorariosDatos.Hora(una.Fin) + " (" + una.Laboratorio + ").";
                }
                else if (mismo != null && mismo.Count() == f.SinRegistro && f.SinRegistro >= 2)
                {
                    ClaseDiaDocente una = mismo.First();
                    texto += " Todas son " + LosDias(una.Fecha.DayOfWeek) + " de " + HorariosDatos.Hora(una.Inicio) + " a " + HorariosDatos.Hora(una.Fin)
                        + " (" + una.Laboratorio + ").";
                }

                lista.Add(new PendienteDeDocente
                {
                    Regla = "R1",
                    Tono = TonoPendiente.Rojo,
                    Texto = texto,
                    Ayuda = EstadisticasDatos.AyudaClasesQueSeDieron,
                    Destino = DestinoPendiente.SinRegistro,
                    Numero = f.SinRegistro
                });
            }

            // R2: hasta 3 clases con estudiantes bajo el 80 %.
            foreach (FilaClase c in r.Clases.Where(x => x.Bajo > 0).OrderByDescending(x => x.Bajo).ThenBy(x => x.Materia, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Grupo, StringComparer.CurrentCultureIgnoreCase).Take(3))
            {
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R2",
                    Tono = TonoPendiente.Rojo,
                    Texto = c.Materia + " (" + c.Grupo + "): " + EstadisticasDatos.Miles(c.Bajo) + " de " + EstadisticasDatos.Contar(c.Estudiantes, "estudiante", "estudiantes")
                        + (r.CicloEntero ? " sin derecho a evaluación (bajo el 80 %)." : " bajo el 80 % en estas fechas."),
                    Ayuda = r.CicloEntero ? "Con el ciclo entero, el 80 % es el derecho a evaluación, como en Mis grupos."
                        : "Cuentan solo los días de estas fechas: el derecho a evaluación es del ciclo entero.",
                    Destino = DestinoPendiente.Clase,
                    ClaseId = c.ClaseId,
                    Numero = c.Bajo
                });
            }

            // R3: lo que le falta por asignar en el horario en uso (db/25), si el periodo llega a hoy.
            if (d.PorAsignar != null && d.PorAsignar.Count > 0)
            {
                List<string> clases = d.PorAsignar.Select(p => p.Materia + " " + p.Grupo).Distinct().ToList();
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R3",
                    Tono = TonoPendiente.Ambar,
                    Texto = "Le faltan " + HorariosDatos.Duracion(d.MinutosPorAsignar) + " por asignar en el horario en uso (" + EstiloAdminTexto.Enumerar(clases) + ").",
                    Ayuda = "Horas que otra clase desplazó o que se quitaron para darlas en otro horario (Horarios > Horario semanal, «Falta asignar»).",
                    Destino = DestinoPendiente.FaltaAsignar,
                    Numero = d.MinutosPorAsignar
                });
            }

            // R4: equipos que fallaron en dos dias o mas en sus clases.
            List<FilaEquipo> repetidos = r.Equipos.Where(e => e.Tipo == TipoFilaEquipo.Equipo && e.Repetido).ToList();
            if (repetidos.Count > 0)
            {
                List<string> nombres = repetidos.Take(3).Select(e => e.Equipo + " (" + e.Laboratorio + ")").ToList();
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R4",
                    Tono = TonoPendiente.Naranja,
                    Texto = (repetidos.Count == 1 ? "1 equipo falló en dos días o más en sus clases: " : EstadisticasDatos.Miles(repetidos.Count) + " equipos fallaron en dos días o más en sus clases: ")
                        + string.Join(", ", nombres) + (repetidos.Count > 3 ? " y " + EstadisticasDatos.Miles(repetidos.Count - 3) + " más" : "") + ".",
                    Ayuda = "Fallas desde ese equipo, reportes de docentes que lo nombran o avisos de que no tenía internet, en dos días distintos o más.",
                    Destino = DestinoPendiente.Fallas,
                    Numero = repetidos.Count
                });
            }

            // R5: lo que reporto desde su panel (db/15).
            if (r.ConReportesDocentes && d.SusReportes.Count > 0)
            {
                List<string> areas = d.SusReportes.GroupBy(x => NombreDeArea(x.Area)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => g.Key + " " + EstadisticasDatos.Miles(g.Count())).ToList();
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R5",
                    Tono = TonoPendiente.Naranja,
                    Texto = (d.SusReportes.Count == 1 ? "Reportó 1 falla desde su panel: " : "Reportó " + EstadisticasDatos.Miles(d.SusReportes.Count) + " fallas desde su panel: ")
                        + string.Join(", ", areas) + ".",
                    Destino = DestinoPendiente.Fallas,
                    Numero = d.SusReportes.Count
                });
            }

            // R6: las asistencias que dio sin bitacora (db/22).
            if (r.ConDadas && f != null && f.AsistenciasQueDio > 0)
            {
                int estudiantes = d.SusDadas.Select(x => x.AlumnoId).Distinct().Count();
                int sinInternet = d.SusDadas.Count(x => x.Motivo == AsistenciasDadas.SinInternet);
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R6",
                    Tono = TonoPendiente.Gris,
                    Texto = "Dio " + EstadisticasDatos.Contar(f.AsistenciasQueDio, "asistencia", "asistencias") + " sin bitácora a "
                        + EstadisticasDatos.Contar(estudiantes, "estudiante", "estudiantes")
                        + (sinInternet > 0 ? "; " + EstadisticasDatos.Miles(sinInternet) + " porque su equipo no tenía internet" : "") + ".",
                    Ayuda = "A quien estaba en el salón y no pudo registrar su bitácora. Las anuladas no cuentan.",
                    Destino = DestinoPendiente.Dadas,
                    Numero = f.AsistenciasQueDio
                });
            }

            // R7: clases canceladas y su motivo mas comun.
            if (f != null && f.Canceladas > 0)
            {
                IGrouping<string, string> motivo = d.Dias.Where(x => x.Cancelada && x.Calendario != null && !string.IsNullOrWhiteSpace(x.Calendario.Motivo))
                    .Select(x => x.Calendario.Motivo.Trim()).GroupBy(m => m, StringComparer.CurrentCultureIgnoreCase)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();
                string texto = f.Canceladas == 1
                    ? "1 clase cancelada" + (motivo != null ? ": «" + motivo.Key + "»." : ".")
                    : EstadisticasDatos.Miles(f.Canceladas) + " clases canceladas"
                        + (motivo != null ? "; el motivo más común: «" + motivo.Key + "» (" + EstadisticasDatos.Miles(motivo.Count()) + ")." : ".");
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R7",
                    Tono = TonoPendiente.Gris,
                    Texto = texto,
                    Ayuda = "Las canceladas (un festivo, un aviso del docente) no cuentan.",
                    Destino = DestinoPendiente.Canceladas,
                    Numero = f.Canceladas
                });
            }

            // R8: las que no cuentan (un laboratorio que aun no registraba, docente de baja...).
            List<ClaseDiaDocente> noCuentan = d.Dias.Where(x => x.Estado == EstadoEnCalendario.NoContaba).ToList();
            if (noCuentan.Count > 0)
            {
                IGrouping<string, ClaseDiaDocente> porque = noCuentan.GroupBy(x => x.Ayuda ?? "").OrderByDescending(g => g.Count()).First();
                string razon = porque.Key.Length == 0 ? "como en Faltas" : EstadisticasDatos.MinusculaInicial(porque.Key.TrimEnd('.'));
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R8",
                    Tono = TonoPendiente.Gris,
                    Texto = (noCuentan.Count == 1 ? "1 clase no cuenta: " : EstadisticasDatos.Miles(noCuentan.Count) + " clases no cuentan: ")
                        + (porque.Count() == noCuentan.Count ? razon : razon + " (y otras razones)") + ".",
                    Ayuda = string.Join("\n", noCuentan.Select(x => x.Ayuda).Where(a => !string.IsNullOrEmpty(a)).Distinct().Take(4)),
                    Destino = DestinoPendiente.NoCuentan,
                    Numero = noCuentan.Count
                });
            }

            // R9: su cuenta y su enlace con el SIIA.
            if (profesor != null)
            {
                if (profesor.Anonimizada)
                {
                    lista.Add(new PendienteDeDocente { Regla = "R9", Tono = TonoPendiente.Gris, Texto = "Su cuenta está anonimizada: ya no tiene datos personales." });
                }
                else if (!profesor.Activo)
                {
                    lista.Add(new PendienteDeDocente
                    {
                        Regla = "R9",
                        Tono = TonoPendiente.Gris,
                        Texto = "Su cuenta está de baja" + (profesor.BajaEn.HasValue ? " desde el " + EstadisticasDatos.Fecha(profesor.BajaEn.Value) : "") + "."
                    });
                }

                if (PidioEnlace(profesor, enlaces))
                {
                    lista.Add(new PendienteDeDocente { Regla = "R9", Tono = TonoPendiente.Gris, Texto = "Pidió enlace con el SIIA: se enlaza en la franja azul de Docentes." });
                }
            }

            // R10: nada en rojo ni en naranja, con clases que ya contaban.
            if (f != null && f.QueContaban > 0 && !lista.Any(p => p.Tono == TonoPendiente.Rojo || p.Tono == TonoPendiente.Naranja))
            {
                lista.Add(new PendienteDeDocente
                {
                    Regla = "R10",
                    Tono = TonoPendiente.Verde,
                    Texto = "En estas fechas se dieron todas sus clases que contaban y ninguno de sus grupos tiene estudiantes bajo el 80 %.",
                    Numero = f.SeDieron
                });
            }

            return lista;
        }

        /// <summary>"los martes", "los sabados".</summary>
        private static string LosDias(DayOfWeek dia)
        {
            string nombre = HorariosDatos.NombresDia[((int)dia + 6) % 7 + 1].ToLowerInvariant();
            return "los " + (dia == DayOfWeek.Saturday || dia == DayOfWeek.Sunday ? nombre + "s" : nombre);
        }

        /// <summary>"red", "hardware", "software" u "otra".</summary>
        internal static string NombreDeArea(string area)
        {
            switch ((area ?? "").Trim().ToLowerInvariant())
            {
                case "red":
                    return "red";
                case "hardware":
                    return "hardware";
                case "software":
                    return "software";
                default:
                    return "otra";
            }
        }
    }

    /// <summary>"a", "a y b", "a, b y c" (como EstiloAdmin.Enumerar, sin depender de la pantalla).</summary>
    internal static class EstiloAdminTexto
    {
        public static string Enumerar(IList<string> partes)
        {
            if (partes == null || partes.Count == 0)
            {
                return "";
            }

            return partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }
    }
}
