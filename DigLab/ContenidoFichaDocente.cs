using System;
using System.Collections.Generic;
using System.Linq;
using DigLab.conexion;

namespace DigLab
{
    /// <summary>
    /// Las secciones del "Reporte del docente (PDF)" de su ficha (2026-10-31), con el motor
    /// del reporte para auditoria (ReporteAuditoriaPdf): las de Graficas en modo de un solo
    /// docente (ContenidoGraficas.Armar con unDocente), renumeradas y con lo suyo. Todo sale
    /// del mismo resultado que la pantalla. Sin conListas no lleva ningun nombre ni matricula
    /// de estudiante: ni la lista de cada grupo ni a quien dio cada asistencia.
    /// </summary>
    internal static class ContenidoFichaDocente
    {
        /// <summary>Las tres notas del final de "Como se calcula cada numero" (las pide la especificacion).</summary>
        public static List<string> NotasFinales(DateTime generadoEn)
        {
            return new List<string>
            {
                "Sin registro no siempre es falta del docente: puede que nadie registrara.",
                "La lista se arma con quienes registran; no es la lista oficial de inscritos.",
                "Calculado el " + EstadisticasDatos.Fecha(generadoEn) + " con lo registrado; no sustituye el cierre oficial del ciclo."
            };
        }

        /// <param name="conListas">La casilla "Incluir la lista de cada grupo": con ella, la lista de cada clase y a quien dio cada asistencia.</param>
        public static List<SeccionGraficas> Armar(DatosDeLaFicha d, AgrupacionGraficas agrupacion, bool conListas)
        {
            ResultadoEstadisticas r = d.R;
            List<SeccionGraficas> graficas = ContenidoGraficas.Armar(r, agrupacion, true, true);
            SeccionGraficas uso = graficas[0];
            SeccionGraficas clases = graficas[1];
            SeccionGraficas asistencia = graficas[2];
            SeccionGraficas fallas = graficas[3];

            List<SeccionGraficas> secciones = new List<SeccionGraficas>();
            secciones.Add(SusClases(clases, d));
            secciones.Add(Asistencia(asistencia, r));
            if (conListas)
            {
                secciones.Add(Listas(r));
            }

            secciones.Add(Fallas(fallas, d));
            if (r.ConDadas)
            {
                secciones.Add(Dadas(d, conListas));
            }

            uso.Titulo = "Uso de los laboratorios en sus clases";
            uso.Descripcion = "Las horas de clase de su horario frente a las horas en que sí se dio clase, y la ocupación de sus horas.";
            secciones.Add(uso);
            for (int i = 0; i < secciones.Count; i++)
            {
                secciones[i].Numero = i + 1;
            }

            return secciones;
        }

        /// <summary>1. Sus clases: la seccion 2 de un solo docente y, si las hay, la tabla dia por dia de las que ya pasaron.</summary>
        private static SeccionGraficas SusClases(SeccionGraficas s, DatosDeLaFicha d)
        {
            List<ClaseDiaDocente> pasadas = d.Dias.Where(x => x.Estado != EstadoEnCalendario.PorVenir).OrderBy(x => x.Fecha).ThenBy(x => x.Inicio).ToList();
            if (pasadas.Count == 0)
            {
                return s;
            }

            TablaDeCifras dia = new TablaDeCifras { Titulo = "Día por día (las que ya pasaron)" }
                .Columna("Fecha", false).Columna("Materia", false).Columna("Grupo", false).Columna("Horario", false).Columna("Laboratorio", false).Columna("¿Se dio?", false);
            foreach (ClaseDiaDocente x in pasadas)
            {
                FilaDeTabla fila = dia.Fila(x.FechaTexto, x.Materia, x.Grupo, x.Horario, x.Laboratorio, x.SeDioTexto);
                if (x.SinRegistro)
                {
                    fila.Colores[5] = ColoresGrafica.Rojo;
                }
            }

            if (s.SinDatos != null)
            {
                // Sin clases que contaran: la tabla va igual (canceladas, las que no cuentan).
                s.Lectura = s.SinDatos;
                s.SinDatos = null;
                s.Cifras.Clear();
                s.Graficas.Clear();
            }

            s.Tablas.Add(dia);
            return s;
        }

        /// <summary>2. Asistencia de sus estudiantes: la seccion 3 de un solo docente y la tabla por clase (como Sus grupos).</summary>
        private static SeccionGraficas Asistencia(SeccionGraficas s, ResultadoEstadisticas r)
        {
            if (r.Clases.Count == 0)
            {
                return s;
            }

            TablaDeCifras porClase = new TablaDeCifras { Titulo = "Sus clases" }
                .Columna("Materia", false).Columna("Grupo", false).Columna("Se dieron").Columna("Sin registro").Columna("Estudiantes").Columna("Asistencia del grupo")
                .Columna(r.CicloEntero ? "Sin derecho (bajo el 80 %)" : "Bajo el 80 %");
            foreach (FilaClase c in r.Clases)
            {
                FilaDeTabla fila = porClase.Fila(c.Materia, c.Grupo, EstadisticasDatos.XdeN(c.SeDieron, c.QueContaban),
                    c.QueContaban == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(c.SinRegistro), EstadisticasDatos.Miles(c.Estudiantes),
                    ContenidoGraficas.Pct(c.Asistencia), c.Veces == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(c.Bajo));
                if (c.SinRegistro > 0)
                {
                    fila.Colores[3] = ColoresGrafica.Rojo;
                }

                if (c.Asistencia.HasValue && !ResumenAsistencia.TieneDerecho(c.Asistencia.Value))
                {
                    fila.Colores[5] = ColoresGrafica.Rojo;
                }
            }

            if (s.SinDatos != null)
            {
                s.Lectura = s.SinDatos;
                s.SinDatos = null;
            }

            s.Tablas.Add(porClase);
            return s;
        }

        /// <summary>3. La lista de cada grupo (solo con la casilla): No., matricula, estudiante, entro, %, faltas y el 80 %.</summary>
        private static SeccionGraficas Listas(ResultadoEstadisticas r)
        {
            SeccionGraficas s = new SeccionGraficas
            {
                Titulo = "Lista de cada grupo",
                Descripcion = "Quienes registraron alguna vez en cada clase y su asistencia en el periodo"
                    + (r.CicloEntero ? "; la última columna es el derecho a evaluación." : "; el derecho a evaluación es del ciclo entero.")
            };
            foreach (FilaClase c in r.Clases)
            {
                TablaDeCifras lista = new TablaDeCifras { Titulo = c.Materia + " (" + c.Grupo + "): " + c.LineaDeLaLista }
                    .Columna("No.").Columna("Matrícula", false).Columna("Estudiante", false).Columna("Entró").Columna("%").Columna("Faltas").Columna(r.TituloDerecho, false);
                int numero = 0;
                foreach (ParDeAsistencia p in c.Lista)
                {
                    numero++;
                    FilaDeTabla fila = lista.Fila(numero.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Matricula, p.Nombre + (p.Activo ? "" : " (de baja)"),
                        EstadisticasDatos.XdeN(p.Entro, p.Veces), EstadisticasDatos.Porcentaje(p.Porcentaje), p.Veces == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(p.Faltas),
                        p.TextoDerecho(r.CicloEntero));
                    if (p.Bajo)
                    {
                        fila.Colores[4] = ColoresGrafica.Rojo;
                        fila.Colores[6] = ColoresGrafica.Rojo;
                    }
                }

                if (c.Lista.Count == 0)
                {
                    lista.Fila("", "", "Nadie ha registrado su bitácora en esta clase.", "", "", "", "");
                }

                s.Tablas.Add(lista);
            }

            if (r.Clases.Count == 0)
            {
                s.SinDatos = "No tuvo clases del horario en estas fechas.";
            }

            s.Notas.Add("Solo cuentan quienes registraron al menos una vez: DigLab no tiene la lista oficial del grupo. Quien llega después entra con falta en las clases anteriores, como en Mis grupos.");
            return s;
        }

        /// <summary>4. Fallas: la seccion 4 (ya de sus clases) y lo que reporto desde su panel (db/15).</summary>
        private static SeccionGraficas Fallas(SeccionGraficas s, DatosDeLaFicha d)
        {
            s.Titulo = "Fallas en sus clases";
            if (!d.R.ConReportesDocentes)
            {
                s.Notas.Add("La base no tiene los reportes de los docentes desde su panel (db/15).");
                return s;
            }

            if (d.SusReportes.Count == 0)
            {
                s.Notas.Add("No reportó fallas desde su panel en estas fechas.");
                return s;
            }

            TablaDeCifras reportes = new TablaDeCifras { Titulo = "Lo que reportó desde su panel" }
                .Columna("Fecha y hora", false).Columna("Laboratorio", false).Columna("Clase", false).Columna("Equipo", false).Columna("Área", false).Columna("Falla", false);
            foreach (FilaReporteDocente x in d.SusReportes.OrderBy(x => x.Dia).ThenBy(x => x.RegistradoEn ?? DateTime.MinValue))
            {
                reportes.Fila(FechaYHora(x.Dia, x.RegistradoEn), x.Laboratorio, x.Clase, x.Equipo ?? "Todo el laboratorio",
                    ReportesProfesorDatos.NombreDeArea(x.Area), x.Falla);
            }

            if (s.SinDatos != null)
            {
                s.Lectura = s.SinDatos;
                s.SinDatos = null;
            }

            s.Tablas.Add(reportes);
            return s;
        }

        /// <summary>5. Asistencias que dio (db/22): sin conListas, sin a quien (solo el dia, la clase y el motivo).</summary>
        private static SeccionGraficas Dadas(DatosDeLaFicha d, bool conListas)
        {
            SeccionGraficas s = new SeccionGraficas
            {
                Titulo = "Asistencias que dio",
                Descripcion = "Las que dio en sus clases a quien estaba en el salón y no pudo registrar su bitácora. Las anuladas no cuentan.",
                Lectura = d.LineaDadas
            };
            if (d.SusDadas.Count == 0)
            {
                s.SinDatos = "No dio asistencias sin bitácora en estas fechas.";
                return s;
            }

            TablaDeCifras tabla = new TablaDeCifras { Titulo = conListas ? "Cada asistencia que dio" : "Cada asistencia que dio (sin los datos de cada estudiante)" }
                .Columna("Fecha", false).Columna("Clase", false);
            if (conListas)
            {
                tabla.Columna("Matrícula", false).Columna("Estudiante", false);
            }

            tabla.Columna("Motivo", false).Columna("Equipo", false);
            if (conListas)
            {
                tabla.Columna("Detalle", false);
            }

            tabla.Columna("La dio a las", false);
            foreach (FilaAsistenciaDada x in d.SusDadas.OrderBy(x => x.Fecha).ThenBy(x => x.DadaEn))
            {
                List<string> celdas = new List<string> { EstadisticasDatos.Fecha(x.Fecha), x.Clase };
                if (conListas)
                {
                    celdas.Add(x.Matricula);
                    celdas.Add(x.Nombre);
                }

                celdas.Add(AsistenciasDadas.NombreDeMotivo(x.Motivo));
                celdas.Add(AsistenciasDadas.ConEquipo(x.Motivo) ? (x.Equipo ?? "").Trim() : "");
                if (conListas)
                {
                    celdas.Add((x.Comentario ?? "").Trim());
                }

                celdas.Add(x.DadaEn.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
                tabla.Fila(celdas.ToArray());
            }

            s.Tablas.Add(tabla);
            return s;
        }

        private static string FechaYHora(DateTime dia, DateTime? cuando)
        {
            return EstadisticasDatos.Fecha(dia) + (cuando.HasValue ? " " + cuando.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "");
        }
    }
}
