using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using DigLab.conexion;

namespace DigLab
{
    // =========================================================================
    // Lo que ensenan la pestana Graficas y el reporte para auditoria, armado UNA
    // vez desde el resultado de Estadisticas (EstadisticasDatos.Calcular): las
    // mismas cuatro secciones, con las mismas cifras, en pantalla y en el PDF.
    // Nada se vuelve a leer de la base: todo sale del ResultadoEstadisticas.
    // =========================================================================

    /// <summary>Una cifra de la fila de arriba de una seccion: "80 %" y "ocupacion del horario".</summary>
    internal sealed class CifraResumen
    {
        public string Valor = "";
        public string Etiqueta = "";
        /// <summary>Verde, rojo o naranja como las cifras grandes; null = tinta.</summary>
        public Color? Color;
    }

    /// <summary>Una columna de una tabla de cifras.</summary>
    internal sealed class ColumnaDeTabla
    {
        public string Titulo = "";
        public bool Derecha;
    }

    /// <summary>Una fila: sus celdas, si es la del total (en negrita) y el color de alguna celda.</summary>
    internal sealed class FilaDeTabla
    {
        public string[] Celdas = new string[0];
        public bool Total;
        /// <summary>Por indice de columna: el color de su texto (rojo bajo el 80 %, por ejemplo).</summary>
        public Dictionary<int, Color> Colores = new Dictionary<int, Color>();
    }

    /// <summary>La tablita con las cifras exactas de una seccion (en una auditoria piden numeros).</summary>
    internal sealed class TablaDeCifras
    {
        public string Titulo = "";
        public List<ColumnaDeTabla> Columnas = new List<ColumnaDeTabla>();
        public List<FilaDeTabla> Filas = new List<FilaDeTabla>();

        public TablaDeCifras Columna(string titulo, bool derecha = true)
        {
            Columnas.Add(new ColumnaDeTabla { Titulo = titulo, Derecha = derecha });
            return this;
        }

        public FilaDeTabla Fila(params string[] celdas)
        {
            FilaDeTabla fila = new FilaDeTabla { Celdas = celdas };
            Filas.Add(fila);
            return fila;
        }
    }

    /// <summary>Una de las cuatro secciones.</summary>
    internal sealed class SeccionGraficas
    {
        public int Numero;
        public string Titulo = "";
        /// <summary>La pregunta que contesta, en un renglon (gris).</summary>
        public string Descripcion = "";
        /// <summary>Lo que dicen sus numeros, en una o dos frases (antes de las cifras): el total y los extremos.</summary>
        public string Lectura = "";
        public List<CifraResumen> Cifras = new List<CifraResumen>();
        public List<Grafica> Graficas = new List<Grafica>();
        public List<TablaDeCifras> Tablas = new List<TablaDeCifras>();
        public List<string> Notas = new List<string>();
        /// <summary>Sin nada que graficar en el periodo: el texto que va en lugar de las graficas (y las tablas).</summary>
        public string SinDatos;
    }

    /// <summary>Un termino y como se cuenta (la nota del final del reporte).</summary>
    internal sealed class Definicion
    {
        public string Termino = "";
        public string ComoSeCuenta = "";
    }

    internal static class ContenidoGraficas
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int EquiposEnLaGrafica = 10;

        /// <summary>
        /// Las cuatro secciones con el periodo, los filtros y la agrupacion de r. Sin
        /// conDocentes (reporte de la persona responsable), la seccion 2 no lleva lo
        /// de cada docente (EstadisticasDatos.ReporteConDocentes).
        /// </summary>
        public static List<SeccionGraficas> Armar(ResultadoEstadisticas r, AgrupacionGraficas agrupacion, bool conDocentes)
        {
            return Armar(r, agrupacion, conDocentes, false);
        }

        /// <summary>
        /// Con unDocente (la ficha del docente: r es de un solo docente), la seccion 2 se llama
        /// "Sus clases", sin la tabla "Por docente" de una sola fila, y lo de "todas las clases
        /// del filtro" dice "sus clases". Las cifras son las mismas.
        /// </summary>
        public static List<SeccionGraficas> Armar(ResultadoEstadisticas r, AgrupacionGraficas agrupacion, bool conDocentes, bool unDocente)
        {
            List<TramoDelPeriodo> tramos = r.Graficas.Tramos(agrupacion);
            string porTramo = PorTramo(agrupacion);
            string tramo = agrupacion == AgrupacionGraficas.Mes ? "Mes" : "Semana";
            return new List<SeccionGraficas>
            {
                UsoDeLaboratorios(r, tramos, porTramo, tramo, unDocente),
                ClasesYDocentes(r, tramos, porTramo, tramo, conDocentes, unDocente),
                AsistenciaDeEstudiantes(r, tramos, porTramo, tramo, conDocentes, unDocente),
                FallasYEquipos(r)
            };
        }

        /// <summary>"por semana" o "por mes".</summary>
        internal static string PorTramo(AgrupacionGraficas agrupacion)
        {
            return agrupacion == AgrupacionGraficas.Mes ? "por mes" : "por semana";
        }

        /// <summary>
        /// "Clases del horario por semana": las columnas verde (con bitacoras), ambar (con
        /// asistencias que dio su docente) y roja (sin registro) de cada tramo, con su leyenda.
        /// La misma grafica en Graficas, en el PDF de auditoria y en el Resumen de la ficha del docente.
        /// </summary>
        internal static ColumnasPorTramo ColumnasDeClases(List<TramoDelPeriodo> tramos, string porTramo, bool hayDadas)
        {
            List<MuestraLeyenda> leyenda = new List<MuestraLeyenda> { new MuestraLeyenda { Color = ColoresGrafica.Verde, Texto = "Se dio, con bitácoras" } };
            if (hayDadas)
            {
                leyenda.Add(new MuestraLeyenda { Color = ColoresGrafica.Ambar, Texto = "Se dio, con asistencias que dio su docente" });
            }

            leyenda.Add(new MuestraLeyenda { Color = ColoresGrafica.Rojo, Texto = "Sin registro" });
            ColumnasPorTramo porSemana = new ColumnasPorTramo
            {
                Titulo = "Clases del horario " + porTramo,
                Subtitulo = "Clases que ya pasaron y contaban (cada bloque y cada sesión extra, como en Faltas); las canceladas no cuentan.",
                Leyenda = leyenda
            };
            foreach (TramoDelPeriodo t in tramos)
            {
                porSemana.Columnas.Add(new ColumnaDeTramo
                {
                    Etiqueta = t.Etiqueta,
                    Segmentos =
                    {
                        new Segmento(t.ConBitacoras, ColoresGrafica.Verde),
                        new Segmento(t.ConDadas, ColoresGrafica.Ambar),
                        new Segmento(t.SinRegistro, ColoresGrafica.Rojo)
                    }
                });
            }

            return porSemana;
        }

        // =====================================================================
        // 1. Uso de los laboratorios
        // =====================================================================

        private static SeccionGraficas UsoDeLaboratorios(ResultadoEstadisticas r, List<TramoDelPeriodo> tramos, string porTramo, string tramo, bool unDocente)
        {
            GraficasEstadisticas g = r.Graficas;
            UsoDeLaboratorio total = g.Total;
            SeccionGraficas s = new SeccionGraficas
            {
                Numero = 1,
                Titulo = "Uso de los laboratorios",
                Descripcion = "Las horas de clase de su horario frente a las horas en que sí se dio clase, y la ocupación del horario."
            };

            if (g.SinDigLab.Count > 0)
            {
                s.Notas.Add((g.SinDigLab.Count == 1 ? "Sin contar: " + g.SinDigLab[0] + " aún no registra" : "Sin contar: " + Unir(g.SinDigLab) + " aún no registran")
                    + " sus clases con DigLab; sus clases sin bitácoras no cuentan, como en Faltas.");
            }

            if (total.HorasDelHorario <= 0 && total.HorasCanceladas <= 0)
            {
                s.SinDatos = "Aún no hay clases del horario que ya pasaron y contaban en este periodo.";
                return s;
            }

            s.Cifras.Add(new CifraResumen { Valor = Horas(total.HorasDelHorario), Etiqueta = "del horario (ya pasaron y contaban)" });
            s.Cifras.Add(new CifraResumen { Valor = Horas(total.HorasUsadas), Etiqueta = "en que se dio clase", Color = ColoresGrafica.Verde });
            s.Cifras.Add(new CifraResumen { Valor = Pct(total.Ocupacion), Etiqueta = "ocupación del horario" });
            if (total.HorasCanceladas > 0)
            {
                s.Cifras.Add(new CifraResumen { Valor = Horas(total.HorasCanceladas), Etiqueta = "de clases canceladas (no cuentan)" });
            }

            List<MuestraLeyenda> leyenda = new List<MuestraLeyenda>
            {
                new MuestraLeyenda { Color = ColoresGrafica.VerdeClaro, Texto = "Horas del horario" },
                new MuestraLeyenda { Color = ColoresGrafica.Verde, Texto = "Horas en que se dio clase" }
            };

            List<UsoDeLaboratorio> conHoras = g.Laboratorios.Where(u => u.HorasDelHorario > 0).ToList();
            s.Lectura = total.HorasDelHorario <= 0
                ? "Las clases del horario de este periodo se cancelaron: " + Horas(total.HorasCanceladas) + " que no cuentan."
                : "Se dio clase en " + Horas(total.HorasUsadas) + " de las " + Horas(total.HorasDelHorario) + " del horario: " + Pct(total.Ocupacion) + " de ocupación.";
            if (conHoras.Count >= 2)
            {
                UsoDeLaboratorio masUsado = conHoras.OrderByDescending(u => u.Ocupacion ?? 0).ThenBy(u => u.Laboratorio, StringComparer.CurrentCultureIgnoreCase).First();
                UsoDeLaboratorio menosUsado = conHoras.OrderBy(u => u.Ocupacion ?? 0).ThenBy(u => u.Laboratorio, StringComparer.CurrentCultureIgnoreCase).First();
                if (Pct(masUsado.Ocupacion) != Pct(menosUsado.Ocupacion))
                {
                    s.Lectura += " El más usado: " + masUsado.Laboratorio + " (" + Pct(masUsado.Ocupacion) + "); el menos: "
                        + menosUsado.Laboratorio + " (" + Pct(menosUsado.Ocupacion) + ").";
                }
            }

            if (conHoras.Count >= 2)
            {
                BarrasHorizontales porLaboratorio = new BarrasHorizontales
                {
                    Titulo = "Horas de clase por laboratorio",
                    Subtitulo = "En el periodo; al final, las horas en que se dio clase de las del horario y la ocupación.",
                    Leyenda = leyenda
                };
                foreach (UsoDeLaboratorio u in conHoras)
                {
                    porLaboratorio.Filas.Add(new FilaDeBarras
                    {
                        Etiqueta = u.Laboratorio,
                        Barras = { new BarraDeFila { Pista = u.HorasDelHorario, Segmentos = { new Segmento(u.HorasUsadas, ColoresGrafica.Verde) } } },
                        Valor = Grafica.Numero(u.HorasUsadas) + " de " + Horas(u.HorasDelHorario) + " · " + Pct(u.Ocupacion)
                    });
                }

                s.Graficas.Add(porLaboratorio);
            }

            ColumnasPorTramo porSemana = new ColumnasPorTramo
            {
                Titulo = "Horas de clase " + porTramo,
                Subtitulo = (unDocente ? "Sus clases en todos sus laboratorios" : total.LaboratorioId == 0 ? "Todos los laboratorios del filtro" : total.Laboratorio) + ", en horas.",
                Unidad = " h",
                Leyenda = leyenda
            };
            foreach (TramoDelPeriodo t in tramos)
            {
                porSemana.Columnas.Add(new ColumnaDeTramo
                {
                    Etiqueta = t.Etiqueta,
                    Pista = t.HorasDelHorario,
                    Segmentos = { new Segmento(t.HorasUsadas, ColoresGrafica.Verde) }
                });
            }

            s.Graficas.Add(porSemana);

            TablaDeCifras labs = new TablaDeCifras { Titulo = "Por laboratorio" }
                .Columna("Laboratorio", false).Columna("Horas del horario").Columna("Horas en que se dio clase").Columna("Ocupación")
                .Columna("Clases que se dieron").Columna("Horas canceladas");
            foreach (UsoDeLaboratorio u in g.Laboratorios)
            {
                labs.Fila(u.Laboratorio + (u.AunNo ? " (aún no registra con DigLab)" : ""), Horas(u.HorasDelHorario), Horas(u.HorasUsadas), Pct(u.Ocupacion),
                    EstadisticasDatos.XdeN(u.SeDieron, u.QueContaban), Horas(u.HorasCanceladas));
            }

            if (g.Laboratorios.Count >= 2)
            {
                labs.Fila("Total", Horas(total.HorasDelHorario), Horas(total.HorasUsadas), Pct(total.Ocupacion),
                    EstadisticasDatos.XdeN(total.SeDieron, total.QueContaban), Horas(total.HorasCanceladas)).Total = true;
            }

            s.Tablas.Add(labs);

            TablaDeCifras semanas = new TablaDeCifras { Titulo = Capital(porTramo) }
                .Columna(tramo, false).Columna("Horas del horario").Columna("Horas en que se dio clase").Columna("Ocupación").Columna("Horas canceladas");
            foreach (TramoDelPeriodo t in tramos)
            {
                semanas.Fila(t.EtiquetaLarga, Horas(t.HorasDelHorario), Horas(t.HorasUsadas), Pct(t.Ocupacion), Horas(t.HorasCanceladas));
            }

            s.Tablas.Add(semanas);
            s.Notas.Add("Ocupación = horas en que se dio clase ÷ horas del horario. Mide el horario de clases: el uso libre y las sesiones fuera del horario no suman.");
            return s;
        }

        // =====================================================================
        // 2. Clases y docentes
        // =====================================================================

        private static SeccionGraficas ClasesYDocentes(ResultadoEstadisticas r, List<TramoDelPeriodo> tramos, string porTramo, string tramo, bool conDocentes, bool unDocente)
        {
            CifrasEstadisticas c = r.Cifras;
            SeccionGraficas s = new SeccionGraficas
            {
                Numero = 2,
                Titulo = unDocente ? "Sus clases" : "Clases y docentes",
                Descripcion = unDocente ? "Sus clases del horario que se dieron frente a las programadas que ya pasaron."
                    : "Las clases del horario que se dieron frente a las programadas que ya pasaron"
                    + (conDocentes ? ", y la asistencia de cada docente a sus clases." : ".")
            };

            if (c.ClasesQueContaban == 0)
            {
                s.SinDatos = "Aún no hay clases del horario que ya pasaron y contaban en este periodo"
                    + (c.Canceladas > 0 ? " (" + EstadisticasDatos.Contar(c.Canceladas, "cancelada", "canceladas") + ")." : ".");
                return s;
            }

            bool hayDadas = c.ClasesConDadas > 0;
            s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.XdeN(c.ClasesQueSeDieron, c.ClasesQueContaban), Etiqueta = "clases que se dieron", Color = ColoresGrafica.Verde });
            s.Cifras.Add(new CifraResumen { Valor = Pct(c.FraccionClasesQueSeDieron * 100.0), Etiqueta = "de las programadas" });
            s.Cifras.Add(new CifraResumen
            {
                Valor = EstadisticasDatos.Miles(c.ClasesSinRegistro),
                Etiqueta = "sin registro",
                Color = c.ClasesSinRegistro > 0 ? ColoresGrafica.Rojo : (Color?)null
            });
            if (c.Canceladas > 0)
            {
                s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.Miles(c.Canceladas), Etiqueta = c.Canceladas == 1 ? "cancelada (no cuenta)" : "canceladas (no cuentan)" });
            }

            ColumnasPorTramo porSemana = ColumnasDeClases(tramos, porTramo, hayDadas);
            List<MuestraLeyenda> leyenda = porSemana.Leyenda;
            s.Graficas.Add(porSemana);

            List<FilaDocente> docentes = r.Docentes.Where(d => d.QueContaban > 0)
                .OrderBy(d => d.FraccionSeDieron ?? 0).ThenByDescending(d => d.SinRegistro).ThenBy(d => d.Docente, StringComparer.CurrentCultureIgnoreCase).ToList();
            // Sin nombres: cuantos docentes, no quienes (eso lo dice la grafica por docente).
            s.Lectura = "Se dieron " + EstadisticasDatos.XdeN(c.ClasesQueSeDieron, c.ClasesQueContaban) + " clases del horario (" + Pct(c.FraccionClasesQueSeDieron * 100.0) + ")"
                + (c.ClasesSinRegistro == 0 ? ": todas con registro."
                    : "; " + EstadisticasDatos.Miles(c.ClasesSinRegistro) + (c.ClasesSinRegistro == 1 ? " quedó sin registro." : " quedaron sin registro."));
            int sinNinguna = docentes.Count(d => d.SeDieron == 0);
            if (conDocentes && docentes.Count >= 2 && sinNinguna > 0)
            {
                s.Lectura += " " + EstadisticasDatos.Miles(sinNinguna) + " de los " + EstadisticasDatos.Miles(docentes.Count) + " docentes con clases "
                    + (sinNinguna == 1 ? "no tiene" : "no tienen") + " ninguna con registro.";
            }

            if (conDocentes && docentes.Count >= 2)
            {
                BarrasHorizontales porDocente = new BarrasHorizontales
                {
                    Titulo = "Clases que se dieron por docente",
                    Subtitulo = "De sus clases que ya pasaron y contaban, en porcentaje; al final, X de N. Primero quien dio menos.",
                    Leyenda = leyenda,
                    Maximo = 1.0
                };
                foreach (FilaDocente d in docentes)
                {
                    double n = d.QueContaban;
                    porDocente.Filas.Add(new FilaDeBarras
                    {
                        Etiqueta = d.Docente,
                        Barras =
                        {
                            new BarraDeFila
                            {
                                Segmentos =
                                {
                                    new Segmento(d.ConBitacoras / n, ColoresGrafica.Verde),
                                    new Segmento(d.ConDadas / n, ColoresGrafica.Ambar),
                                    new Segmento(d.SinRegistro / n, ColoresGrafica.Rojo)
                                }
                            }
                        },
                        Valor = EstadisticasDatos.XdeN(d.SeDieron, d.QueContaban) + " · " + Pct(d.FraccionSeDieron * 100.0)
                    });
                }

                s.Graficas.Add(porDocente);
            }

            TablaDeCifras semanas = new TablaDeCifras { Titulo = Capital(porTramo) }
                .Columna(tramo, false).Columna("Se dieron").Columna("Con bitácoras");
            if (hayDadas)
            {
                semanas.Columna("Con asistencias dadas");
            }

            semanas.Columna("Sin registro").Columna("Canceladas");
            foreach (TramoDelPeriodo t in tramos)
            {
                List<string> celdas = new List<string> { t.EtiquetaLarga, EstadisticasDatos.XdeN(t.SeDieron, t.QueContaban), EstadisticasDatos.Miles(t.ConBitacoras) };
                if (hayDadas)
                {
                    celdas.Add(EstadisticasDatos.Miles(t.ConDadas));
                }

                celdas.Add(EstadisticasDatos.Miles(t.SinRegistro));
                celdas.Add(EstadisticasDatos.Miles(t.Canceladas));
                FilaDeTabla fila = semanas.Fila(celdas.ToArray());
                if (t.SinRegistro > 0)
                {
                    fila.Colores[celdas.Count - 2] = ColoresGrafica.Rojo;
                }
            }

            List<string> totales = new List<string>
            {
                "Total", EstadisticasDatos.XdeN(c.ClasesQueSeDieron, c.ClasesQueContaban), EstadisticasDatos.Miles(c.ClasesConBitacorasX)
            };
            if (hayDadas)
            {
                totales.Add(EstadisticasDatos.Miles(c.ClasesConDadas));
            }

            totales.Add(EstadisticasDatos.Miles(c.ClasesSinRegistro));
            totales.Add(EstadisticasDatos.Miles(c.Canceladas));
            semanas.Fila(totales.ToArray()).Total = true;
            s.Tablas.Add(semanas);

            if (conDocentes && unDocente)
            {
                // Una sola fila (la suya) no dice nada que no digan las cifras de arriba.
            }
            else if (conDocentes)
            {
                TablaDeCifras porDocente = new TablaDeCifras { Titulo = "Por docente" }
                    .Columna("Docente", false).Columna("Materias").Columna("Se dieron").Columna("%").Columna("Sin registro").Columna("Canceladas");
                if (r.ConDadas)
                {
                    porDocente.Columna("Asistencias que dio");
                }

                if (r.ConReportesDocentes)
                {
                    porDocente.Columna("Sus reportes");
                }

                foreach (FilaDocente d in docentes.Concat(r.Docentes.Where(x => x.QueContaban == 0).OrderBy(x => x.Docente, StringComparer.CurrentCultureIgnoreCase)))
                {
                    List<string> celdas = new List<string>
                    {
                        d.Docente, EstadisticasDatos.Miles(d.Materias), EstadisticasDatos.XdeN(d.SeDieron, d.QueContaban), Pct(d.FraccionSeDieron * 100.0),
                        d.QueContaban == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(d.SinRegistro), EstadisticasDatos.Miles(d.Canceladas)
                    };
                    if (r.ConDadas)
                    {
                        celdas.Add(EstadisticasDatos.Miles(d.AsistenciasQueDio));
                    }

                    if (r.ConReportesDocentes)
                    {
                        celdas.Add(EstadisticasDatos.Miles(d.SusReportes));
                    }

                    FilaDeTabla fila = porDocente.Fila(celdas.ToArray());
                    if (d.SinRegistro > 0)
                    {
                        fila.Colores[4] = ColoresGrafica.Rojo;
                    }
                }

                s.Tablas.Add(porDocente);
            }
            else
            {
                s.Notas.Add(EstadisticasDatos.SinDocentesEnElReporte);
            }

            s.Notas.Add(EstadisticasDatos.AyudaClasesQueSeDieron);
            return s;
        }

        // =====================================================================
        // 3. Asistencia de los estudiantes
        // =====================================================================

        private static SeccionGraficas AsistenciaDeEstudiantes(ResultadoEstadisticas r, List<TramoDelPeriodo> tramos, string porTramo, string tramo, bool conDocentes, bool unDocente)
        {
            CifrasEstadisticas c = r.Cifras;
            SeccionGraficas s = new SeccionGraficas
            {
                Numero = 3,
                Titulo = unDocente ? "Asistencia de sus estudiantes" : "Asistencia de los estudiantes",
                Descripcion = "Las veces que entraron a sus clases de las que contaban (como Mis grupos), por grupo y por materia, y cómo cambió en el periodo."
            };

            if (c.Veces == 0)
            {
                s.SinDatos = "Aún no hay días de clase que contar en este periodo.";
                return s;
            }

            double asistencia = 100.0 * c.Entraron / c.Veces;
            s.Cifras.Add(new CifraResumen
            {
                Valor = Pct(asistencia),
                Etiqueta = "asistencia de los estudiantes",
                Color = ResumenAsistencia.TieneDerecho(asistencia) ? ColoresGrafica.Verde : ColoresGrafica.Rojo
            });
            s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.XdeN(c.Entraron, c.Veces), Etiqueta = "veces que entraron" });
            s.Cifras.Add(new CifraResumen
            {
                Valor = EstadisticasDatos.Miles(c.EstudiantesBajo),
                Etiqueta = c.EstudiantesBajo == 1 ? "estudiante bajo el 80 % en alguna clase" : "estudiantes bajo el 80 % en alguna clase",
                Color = c.EstudiantesBajo > 0 ? ColoresGrafica.Rojo : (Color?)null
            });

            LineaPorTramo linea = new LineaPorTramo
            {
                Titulo = "Asistencia " + porTramo,
                Subtitulo = "Entraron de las veces que contaban, en porcentaje, " + (unDocente ? "en sus clases." : "en todas las clases del filtro."),
                Marca = ResumenAsistencia.Minimo,
                Leyenda =
                {
                    new MuestraLeyenda { Color = ColoresGrafica.Verde, Texto = "Asistencia", EsLinea = true },
                    new MuestraLeyenda { Color = ColoresGrafica.Rojo, Texto = "Mínimo: " + ResumenAsistencia.Minimo + " %", EsLinea = true, Discontinua = true }
                }
            };
            foreach (TramoDelPeriodo t in tramos)
            {
                linea.Puntos.Add(new PuntoDeTramo { Etiqueta = t.Etiqueta, Valor = t.Asistencia });
            }

            s.Graficas.Add(linea);

            List<FilaGrupo> grupos = r.Grupos.Where(x => x.Veces > 0).OrderBy(x => x.Asistencia ?? 0).ThenBy(x => x.Grupo, StringComparer.CurrentCultureIgnoreCase).ToList();
            s.Lectura = "Los estudiantes entraron " + EstadisticasDatos.XdeN(c.Entraron, c.Veces) + " veces: " + Pct(asistencia)
                + (ResumenAsistencia.TieneDerecho(asistencia) ? " (el mínimo es " + ResumenAsistencia.Minimo + " %)." : ", bajo el mínimo de " + ResumenAsistencia.Minimo + " %.");
            if (grupos.Count >= 2 && Pct(grupos[0].Asistencia) != Pct(grupos[grupos.Count - 1].Asistencia))
            {
                FilaGrupo masAlto = grupos.OrderByDescending(x => x.Asistencia ?? 0).ThenBy(x => x.Grupo, StringComparer.CurrentCultureIgnoreCase).First();
                s.Lectura += " El grupo más bajo: " + grupos[0].Grupo + " (" + Pct(grupos[0].Asistencia) + "); el más alto: " + masAlto.Grupo + " (" + Pct(masAlto.Asistencia) + ").";
            }

            if (grupos.Count >= 2)
            {
                BarrasHorizontales porGrupo = new BarrasHorizontales
                {
                    Titulo = "Asistencia por grupo",
                    Subtitulo = "En porcentaje, sumadas todas sus clases; en rojo, bajo el mínimo. Primero el más bajo.",
                    Maximo = 100,
                    Marca = ResumenAsistencia.Minimo,
                    TextoMarca = ResumenAsistencia.Minimo + " %",
                    ConPistaCompleta = true,
                    Leyenda =
                    {
                        new MuestraLeyenda { Color = ColoresGrafica.Verde, Texto = ResumenAsistencia.Minimo + " % o más" },
                        new MuestraLeyenda { Color = ColoresGrafica.Rojo, Texto = "Menos del " + ResumenAsistencia.Minimo + " %" }
                    }
                };
                foreach (FilaGrupo grupo in grupos)
                {
                    double valor = grupo.Asistencia.Value;
                    porGrupo.Filas.Add(new FilaDeBarras
                    {
                        Etiqueta = grupo.Grupo,
                        Barras = { new BarraDeFila { Segmentos = { new Segmento(valor, ResumenAsistencia.TieneDerecho(valor) ? ColoresGrafica.Verde : ColoresGrafica.Rojo) } } },
                        Valor = Pct(valor)
                    });
                }

                s.Graficas.Add(porGrupo);
            }

            TablaDeCifras porTramoTabla = new TablaDeCifras { Titulo = Capital(porTramo) }
                .Columna(tramo, false).Columna("Entraron").Columna("Asistencia");
            foreach (TramoDelPeriodo t in tramos)
            {
                FilaDeTabla fila = porTramoTabla.Fila(t.EtiquetaLarga, EstadisticasDatos.XdeN(t.Entraron, t.Veces), Pct(t.Asistencia));
                ColorBajo(fila, 2, t.Asistencia);
            }

            FilaDeTabla totalTramos = porTramoTabla.Fila("Total", EstadisticasDatos.XdeN(c.Entraron, c.Veces), Pct(asistencia));
            totalTramos.Total = true;
            ColorBajo(totalTramos, 2, asistencia);
            s.Tablas.Add(porTramoTabla);

            TablaDeCifras porGrupoTabla = new TablaDeCifras { Titulo = "Por grupo" }
                .Columna("Grupo", false).Columna("Clases").Columna("Estudiantes").Columna("Entraron").Columna("Asistencia").Columna("Bajo el 80 %");
            foreach (FilaGrupo grupo in r.Grupos)
            {
                FilaDeTabla fila = porGrupoTabla.Fila(grupo.Grupo, EstadisticasDatos.Miles(grupo.Clases), EstadisticasDatos.Miles(grupo.Estudiantes),
                    EstadisticasDatos.XdeN(grupo.Entro, grupo.Veces), Pct(grupo.Asistencia), grupo.Veces == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(grupo.Bajo));
                ColorBajo(fila, 4, grupo.Asistencia);
            }

            s.Tablas.Add(porGrupoTabla);

            // Por materia y grupo (cada clase). Con el docente si el reporte lo lleva: la misma
            // materia con el mismo grupo puede tener dos docentes (dos clases).
            List<FilaClase> clases = r.Clases.Where(x => x.Veces > 0).ToList();
            TablaDeCifras porMateria = new TablaDeCifras { Titulo = "Por materia y grupo" }.Columna("Materia", false).Columna("Grupo", false);
            // De un solo docente: la columna Docente diria lo mismo en todas las filas.
            bool conColumnaDocente = conDocentes && !unDocente;
            if (conColumnaDocente)
            {
                porMateria.Columna("Docente", false);
            }

            porMateria.Columna("Estudiantes").Columna("Entraron").Columna("Asistencia").Columna("Bajo el 80 %");
            foreach (FilaClase clase in clases)
            {
                List<string> celdas = new List<string> { clase.Materia, clase.Grupo };
                if (conColumnaDocente)
                {
                    celdas.Add(clase.Docente);
                }

                celdas.AddRange(new[] { EstadisticasDatos.Miles(clase.Estudiantes), EstadisticasDatos.XdeN(clase.Entro, clase.Veces), Pct(clase.Asistencia), EstadisticasDatos.Miles(clase.Bajo) });
                FilaDeTabla fila = porMateria.Fila(celdas.ToArray());
                ColorBajo(fila, celdas.Count - 2, clase.Asistencia);
            }

            s.Tablas.Add(porMateria);
            int sinDias = r.Clases.Count - clases.Count;
            if (sinDias > 0)
            {
                s.Notas.Add(EstadisticasDatos.Contar(sinDias, "clase", "clases") + " del filtro sin días que contar en el periodo no salen en «Por materia y grupo».");
            }

            s.Notas.Add("Solo cuentan quienes han registrado al menos una vez: DigLab no tiene la lista oficial de cada grupo. Quien llega después entra con falta en las clases anteriores, como en Mis grupos.");
            return s;
        }

        // =====================================================================
        // 4. Fallas y equipos
        // =====================================================================

        private static SeccionGraficas FallasYEquipos(ResultadoEstadisticas r)
        {
            CifrasEstadisticas c = r.Cifras;
            int lab = r.Filtro.Laboratorio;
            SeccionGraficas s = new SeccionGraficas
            {
                Numero = 4,
                Titulo = "Fallas y equipos",
                Descripcion = "Las fallas reportadas en las bitácoras y por los docentes, por tipo y por laboratorio, y los equipos con más reportes."
            };

            if (c.FallasReportadas == 0 && c.AvisosSinInternet == 0)
            {
                s.SinDatos = "No se reportaron fallas en este periodo.";
                return s;
            }

            s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.Miles(c.FallasReportadas), Etiqueta = "fallas reportadas", Color = c.FallasReportadas > 0 ? ColoresGrafica.Fallas : (Color?)null });
            s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.Miles(c.Hardware), Etiqueta = "de hardware" });
            s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.Miles(c.Software), Etiqueta = "de software" });
            s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.Miles(c.Red), Etiqueta = "de red" });
            if (c.EquiposSinInternet.HasValue)
            {
                s.Cifras.Add(new CifraResumen { Valor = EstadisticasDatos.Miles(c.EquiposSinInternet.Value), Etiqueta = "equipos avisados sin internet" });
            }

            List<FilaLaboratorio> laboratorios = r.Laboratorios.Where(l => !l.EsTodos && (lab == 0 || l.LaboratorioId == lab)).ToList();
            List<FilaLaboratorio> conFallas = laboratorios.Where(l => l.Fallas > 0).OrderByDescending(l => l.Fallas).ThenBy(l => l.Laboratorio, StringComparer.CurrentCultureIgnoreCase).ToList();
            s.Lectura = c.FallasReportadas == 0
                ? "No se reportaron fallas" + (c.EquiposSinInternet.HasValue && c.EquiposSinInternet.Value > 0
                    ? "; " + EstadisticasDatos.Contar(c.EquiposSinInternet.Value, "equipo fue avisado", "equipos fueron avisados") + " sin internet." : ".")
                : "Se reportaron " + EstadisticasDatos.Contar(c.FallasReportadas, "falla", "fallas") + ": " + EstadisticasDatos.Miles(c.Hardware) + " de hardware, "
                    + EstadisticasDatos.Miles(c.Software) + " de software y " + EstadisticasDatos.Miles(c.Red) + " de red.";
            if (conFallas.Count >= 2 && conFallas[0].Fallas > conFallas[1].Fallas)
            {
                s.Lectura += " El laboratorio con más: " + conFallas[0].Laboratorio + " (" + EstadisticasDatos.Contar(conFallas[0].Fallas, "falla", "fallas") + ").";
            }
            if (conFallas.Count > 0)
            {
                BarrasHorizontales porTipo = new BarrasHorizontales
                {
                    Titulo = conFallas.Count == 1 ? "Fallas por tipo" : "Fallas por laboratorio y tipo",
                    Subtitulo = "Bitácoras con falla y reportes de docentes; al final, el total. Una bitácora puede reportar más de un tipo.",
                    Leyenda =
                    {
                        new MuestraLeyenda { Color = ColoresGrafica.Hardware, Texto = "Hardware" },
                        new MuestraLeyenda { Color = ColoresGrafica.Software, Texto = "Software" },
                        new MuestraLeyenda { Color = ColoresGrafica.Red, Texto = "Red" }
                    }
                };
                foreach (FilaLaboratorio l in conFallas)
                {
                    porTipo.Filas.Add(new FilaDeBarras
                    {
                        Etiqueta = l.Laboratorio,
                        Barras =
                        {
                            new BarraDeFila { Segmentos = { new Segmento(l.Hardware, ColoresGrafica.Hardware) } },
                            new BarraDeFila { Segmentos = { new Segmento(l.Software, ColoresGrafica.Software) } },
                            new BarraDeFila { Segmentos = { new Segmento(l.Red, ColoresGrafica.Red) } }
                        },
                        Valor = EstadisticasDatos.Contar(l.Fallas, "falla", "fallas")
                    });
                }

                s.Graficas.Add(porTipo);
            }

            List<FilaEquipo> equipos = r.Equipos.Where(e => e.Tipo == TipoFilaEquipo.Equipo && e.Fallas + e.Avisos > 0).Take(EquiposEnLaGrafica).ToList();
            List<FilaEquipo> porReportes = equipos.OrderByDescending(e => e.Fallas + e.Avisos).ToList();
            if (porReportes.Count == 1 || (porReportes.Count >= 2 && porReportes[0].Fallas + porReportes[0].Avisos > porReportes[1].Fallas + porReportes[1].Avisos))
            {
                FilaEquipo primero = porReportes[0];
                s.Lectura += (porReportes.Count == 1 ? " El único equipo con reportes: " : " El equipo con más reportes: ") + primero.Equipo
                    + (lab == 0 ? " de " + primero.Laboratorio : "") + " (" + EstadisticasDatos.Contar(primero.Fallas + primero.Avisos, "reporte", "reportes") + ").";
            }

            if (equipos.Count > 0)
            {
                BarrasHorizontales porEquipo = new BarrasHorizontales
                {
                    Titulo = "Equipos con más reportes",
                    Subtitulo = "Fallas reportadas desde cada equipo o que lo nombran"
                        + (r.ConAvisos ? ", y avisos de otros estudiantes de que no tenía internet" : "")
                        + (equipos.Count == EquiposEnLaGrafica ? "; los " + EquiposEnLaGrafica + " con más." : ".")
                };
                foreach (FilaEquipo e in equipos)
                {
                    porEquipo.Filas.Add(new FilaDeBarras
                    {
                        Etiqueta = e.Equipo + (lab == 0 ? " · " + e.Laboratorio : ""),
                        Barras = { new BarraDeFila { Segmentos = { new Segmento(e.Fallas + e.Avisos, ColoresGrafica.Fallas) } } },
                        Valor = e.Avisos == 0 ? EstadisticasDatos.Contar(e.Fallas, "falla", "fallas")
                            : e.Fallas == 0 ? EstadisticasDatos.Contar(e.Avisos, "aviso", "avisos")
                            : EstadisticasDatos.Miles(e.Fallas + e.Avisos) + ": " + EstadisticasDatos.Contar(e.Fallas, "falla", "fallas") + " y "
                                + EstadisticasDatos.Contar(e.Avisos, "aviso", "avisos")
                    });
                }

                s.Graficas.Add(porEquipo);
            }

            TablaDeCifras porLaboratorio = new TablaDeCifras { Titulo = "Por laboratorio" }
                .Columna("Laboratorio", false).Columna("Fallas reportadas").Columna("Hardware").Columna("Software").Columna("Red");
            if (r.ConReportesDocentes)
            {
                porLaboratorio.Columna("De docentes");
            }

            if (r.ConAvisos)
            {
                porLaboratorio.Columna("Equipos sin internet").Columna("Avisos sin internet");
            }

            foreach (FilaLaboratorio l in laboratorios.OrderByDescending(x => x.Fallas).ThenBy(x => x.Laboratorio, StringComparer.CurrentCultureIgnoreCase))
            {
                porLaboratorio.Fila(FilaDeFallas(r, l).ToArray());
            }

            if (lab == 0 && laboratorios.Count >= 2)
            {
                FilaLaboratorio todos = r.Laboratorios.FirstOrDefault(l => l.EsTodos);
                if (todos != null)
                {
                    List<string> celdas = FilaDeFallas(r, todos);
                    celdas[0] = "Total";
                    porLaboratorio.Fila(celdas.ToArray()).Total = true;
                }
            }

            s.Tablas.Add(porLaboratorio);

            if (equipos.Count > 0)
            {
                TablaDeCifras porEquipo = new TablaDeCifras { Titulo = "Equipos con más reportes" }
                    .Columna("Equipo", false).Columna("Laboratorio", false).Columna("Fallas reportadas");
                if (r.ConAvisos)
                {
                    porEquipo.Columna("Avisos sin internet");
                }

                porEquipo.Columna("Falló en", false).Columna("Último reporte", false);
                foreach (FilaEquipo e in equipos)
                {
                    List<string> celdas = new List<string> { e.Equipo, e.Laboratorio, EstadisticasDatos.Miles(e.Fallas) };
                    if (r.ConAvisos)
                    {
                        celdas.Add(EstadisticasDatos.Miles(e.Avisos));
                    }

                    celdas.Add(e.FalloEnTexto);
                    celdas.Add(string.IsNullOrEmpty(e.UltimoReporte) ? EstadisticasDatos.SinDato : e.UltimoReporte);
                    porEquipo.Fila(celdas.ToArray());
                }

                s.Tablas.Add(porEquipo);
            }

            int sinEquipo = r.Equipos.Where(e => e.Tipo != TipoFilaEquipo.Equipo).Sum(e => e.Fallas);
            if (sinEquipo > 0)
            {
                s.Notas.Add(EstadisticasDatos.Contar(sinEquipo, "falla", "fallas") + " sin equipo (de todo el laboratorio o sin el nombre del equipo) cuentan en el total y en su laboratorio, pero no en los equipos.");
            }

            s.Notas.Add("Una bitácora puede reportar más de un tipo y un reporte de docente puede ser de «otro»: los tres tipos no siempre suman el total.");
            return s;
        }

        private static List<string> FilaDeFallas(ResultadoEstadisticas r, FilaLaboratorio l)
        {
            List<string> celdas = new List<string>
            {
                l.Laboratorio, EstadisticasDatos.Miles(l.Fallas), EstadisticasDatos.Miles(l.Hardware), EstadisticasDatos.Miles(l.Software), EstadisticasDatos.Miles(l.Red)
            };
            if (r.ConReportesDocentes)
            {
                celdas.Add(EstadisticasDatos.Miles(l.DeDocentes));
            }

            if (r.ConAvisos)
            {
                celdas.Add(l.EquiposSinInternet.HasValue ? EstadisticasDatos.Miles(l.EquiposSinInternet.Value) : EstadisticasDatos.SinDato);
                celdas.Add(EstadisticasDatos.Miles(l.AvisosSinInternet));
            }

            return celdas;
        }

        // =====================================================================
        // Como se calcula cada numero (la nota del final del reporte)
        // =====================================================================

        public static List<Definicion> Definiciones(ResultadoEstadisticas r)
        {
            List<Definicion> lista = new List<Definicion>
            {
                Def("Periodo y filtros", "Todas las cifras son las de Estadísticas con el mismo periodo y los mismos filtros: las de las gráficas suman lo mismo que las cifras "
                    + "grandes y que cada pestaña. Las semanas van de lunes a domingo y los meses del día 1 al último; el primero y el último se recortan al periodo."),
                Def("Horas del horario", "La duración de cada bloque del horario y de cada sesión extra que ya pasó y contaba en el periodo: las mismas clases que «Clases que se dieron», "
                    + "contadas como en la pestaña Faltas de Horarios. No cuentan las canceladas, la clase en curso ni las de un laboratorio que aún no registraba sus clases con DigLab."),
                Def("Horas en que se dio clase", "De esas, las de las clases que se dieron: con bitácoras o con asistencias que dio su docente a quien no pudo registrar."),
                Def("Ocupación del horario", "Horas en que se dio clase ÷ horas del horario. DigLab no guarda a qué hora abre cada laboratorio: la ocupación es la de su horario de clases, "
                    + "sin el uso libre ni las sesiones fuera del horario."),
                Def("Clases que se dieron", "Cada bloque del horario y cada sesión extra que ya pasó y contaba es una clase (dos bloques el mismo día son dos clases). "
                    + EstadisticasDatos.AyudaClasesQueSeDieron),
                Def("Asistencia de los estudiantes", "Las veces que entraron de las que contaban, sumadas en todas las listas, como «Asistencia del grupo» de Mis grupos: la lista de cada clase "
                    + "se arma con quienes registran su bitácora, quien llega después entra con falta en las clases anteriores y cuentan los días de clase con al menos una bitácora"
                    + (r.ConDadas ? " o una asistencia que dio su docente" : "") + ". El mínimo para el derecho a evaluación es " + ResumenAsistencia.Minimo + " %; los porcentajes se truncan "
                    + "(79.6 % se ve 79 %)."),
                Def("Fallas reportadas", "Bitácoras de estudiantes con alguna falla"
                    + (r.ConReportesDocentes ? " y reportes de docentes desde su panel" : "") + ". Cada tipo (hardware, software, red) suma las de ese tipo. "
                    + "No cuentan «Todo bien», «Traje mi propio equipo» ni los avisos de equipos sin internet."),
                Def("Equipos con más reportes", "Las fallas reportadas desde cada equipo o que lo nombran"
                    + (r.ConAvisos ? " y los avisos de otros estudiantes de que no tenía internet (uno por bitácora)" : "")
                    + "; «7» y «PC-07» se juntan cuando en el laboratorio no hay otro equipo con ese número.")
            };
            Definicion delPeriodo = DefinicionDelPeriodo(r);
            if (delPeriodo != null)
            {
                lista.Insert(1, delPeriodo);
            }

            return lista;
        }

        /// <summary>
        /// Que dias y que clases cuentan en un ciclo ("Este ciclo" o "Ciclo completo"), en unas
        /// fechas elegidas o en una plantilla aplicada ("Horario aplicado", como unas fechas),
        /// justo despues de "Periodo y filtros"; null con el mes, la semana o
        /// los ultimos 30 dias. Con un laboratorio elegido, el 80 % no es el derecho a evaluacion
        /// (CicloEntero, como el titulo de la columna); con filtros, cuantas clases son de ellos.
        /// Con mas de 400 dias (Recortado), que solo cuentan los ultimos 400.
        /// </summary>
        private static Definicion DefinicionDelPeriodo(ResultadoEstadisticas r)
        {
            string listaEntera = ": la lista de cada una es la de la clase completa, como en Mis grupos, "
                + (r.CicloEntero ? "y el 80 % es el derecho a evaluación."
                    : "pero con un laboratorio elegido solo cuentan sus días en ese laboratorio: el 80 % es el de esos días, no el derecho a evaluación, que es de todo el ciclo.");
            const string PorDia = " Las bitácoras, las fallas y los equipos se cuentan por día.";
            string cuantas = ClasesDeLaDefinicion(r);
            string recorte = "DigLab cuenta como mucho " + HorariosDatos.DiasMaximosCalendario + " días seguidos: se cuenta del " + EstadisticasDatos.Fecha(r.Desde);
            CicloEscolar c = r.Ciclo;
            switch (r.Filtro.Tipo)
            {
                case TipoPeriodo.Ciclo:
                    {
                        string dias = c == null ? "Del " + EstadisticasDatos.Fecha(r.Desde) + " a hoy."
                            : r.Recortado ? "Empezó el " + EstadisticasDatos.Fecha(c.Desde) + ", pero " + recorte + " a hoy; " + EstadisticasDatos.FinDeEsteCiclo(c, r.Hasta) + "."
                            : "Del " + EstadisticasDatos.Fecha(c.Desde) + " a hoy; " + EstadisticasDatos.FinDeEsteCiclo(c, r.Hasta) + ".";
                        return Def("Este ciclo", dias
                            + (c != null && c.NombreAplicado ? " Su nombre es el que se le puso en Horarios > Aplicación." : "")
                            + " Solo cuentan las clases del horario que empezaron en este ciclo (" + cuantas + ")"
                            + listaEntera + PorDia + " Sigue en curso: las cifras cambian mientras avanza.");
                    }

                case TipoPeriodo.CicloPasado:
                    {
                        string texto = (r.Recortado
                                ? "Empezó el " + EstadisticasDatos.Fecha(r.Filtro.Desde) + " y terminó el " + EstadisticasDatos.Fecha(r.Hasta) + ", pero " + recorte
                                    + " al " + EstadisticasDatos.Fecha(r.Hasta) + ": los días de antes no entran."
                                : "Del " + EstadisticasDatos.Fecha(r.Desde) + " al " + EstadisticasDatos.Fecha(r.Hasta) + ".")
                            + " Solo cuentan las clases del horario que empezaron en el ciclo (" + cuantas + ")" + listaEntera + PorDia;
                        if (c != null && c.SiguenDespues > 0)
                        {
                            texto += " " + EstadisticasDatos.Contar(c.SiguenDespues, "clase siguió", "clases siguieron") + " hasta el " + EstadisticasDatos.Fecha(c.HastaDeSusClases)
                                + ": esos días no entran.";
                        }

                        texto += c != null && c.NombreAplicado
                            ? " Su nombre es el que se le puso en Horarios > Aplicación."
                            : " Su nombre sale de sus meses: DigLab lo deduce de las fechas de sus clases.";
                        texto += " Se calcula con los datos de hoy (bajas, nombres y el grupo de cada estudiante como están ahora): no es un cierre congelado del ciclo.";
                        return Def(r.Recortado ? "Ciclo (sus últimos " + HorariosDatos.DiasMaximosCalendario + " días)" : "Ciclo completo", texto);
                    }

                case TipoPeriodo.Fechas:
                    return Def("Fechas elegidas", "Cuentan todas las clases del horario que tocan esas fechas, con la lista de la clase completa, como en Mis grupos; "
                        + "el 80 % es el de esos días, no el derecho a evaluación de un ciclo." + (r.Filtro.HastaHoy ? " El último día es siempre hoy." : "")
                        + (r.Recortado ? " Se eligió desde el " + EstadisticasDatos.Fecha(r.Filtro.Desde) + ", pero " + recorte + " al " + EstadisticasDatos.Fecha(r.Hasta) + "." : ""));
                case TipoPeriodo.Plantilla:
                    {
                        // Solo los dias en que valio esa plantilla, contados como unas fechas elegidas.
                        PlantillaAplicada p = r.Plantilla;
                        string nombre = p == null ? "el horario aplicado" : "el horario «" + p.Nombre + "»";
                        string cuando = p == null ? ""
                            : p.EnCurso ? " Sigue en curso: el último día es hoy y las cifras cambian mientras avanza."
                            : p.SustituidaPor.Length > 0 ? " Dejó de valer el " + EstadisticasDatos.Fecha(p.Hasta) + ", cuando se aplicó «" + p.SustituidaPor + "»."
                            : " Su horario terminó el " + EstadisticasDatos.Fecha(p.Hasta) + ".";
                        return Def("Horario aplicado", "Solo los días en que valió " + nombre + ", aplicado en Horarios > Aplicación: del "
                            + EstadisticasDatos.Fecha(r.Filtro.Desde) + " al " + EstadisticasDatos.Fecha(r.Filtro.Hasta) + "." + cuando
                            + " Se cuenta como unas fechas elegidas: todas las clases del horario que tocan esos días, con la lista de la clase completa, como en Mis grupos; "
                            + "el 80 % es el de esos días, no el derecho a evaluación de un ciclo."
                            + (r.Recortado ? " Pero " + recorte + " al " + EstadisticasDatos.Fecha(r.Hasta) + "." : ""));
                    }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Las clases de la definicion del periodo: "27 clases" o, con un laboratorio, un docente o
        /// un grupo elegido, las de esos filtros (las filas de Clases) y de cuantas son: "7 clases
        /// con estos filtros, de 27".
        /// </summary>
        private static string ClasesDeLaDefinicion(ResultadoEstadisticas r)
        {
            bool conFiltros = r.Filtro.Laboratorio != 0 || r.Filtro.Docente != 0 || r.Filtro.Grupo != 0;
            return conFiltros
                ? EstadisticasDatos.Contar(r.Clases.Count, "clase", "clases") + " con estos filtros, de " + EstadisticasDatos.Miles(r.ClasesDelPeriodo)
                : EstadisticasDatos.Contar(r.ClasesDelPeriodo, "clase", "clases");
        }

        private static Definicion Def(string termino, string comoSeCuenta)
        {
            return new Definicion { Termino = termino, ComoSeCuenta = comoSeCuenta };
        }

        // =====================================================================
        // Formatos
        // =====================================================================

        /// <summary>"120 h", "1.5 h" o "0 h".</summary>
        internal static string Horas(double horas)
        {
            return Grafica.Numero(horas) + " h";
        }

        /// <summary>"84 %" (truncado, como en todas las pestanas) o "-".</summary>
        internal static string Pct(double? valor)
        {
            return EstadisticasDatos.Porcentaje(valor);
        }

        private static void ColorBajo(FilaDeTabla fila, int columna, double? valor)
        {
            if (valor.HasValue && !ResumenAsistencia.TieneDerecho(valor.Value))
            {
                fila.Colores[columna] = ColoresGrafica.Rojo;
            }
        }

        private static string Capital(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0], CultureInfo.CurrentCulture) + texto.Substring(1);
        }

        private static string Unir(IList<string> partes)
        {
            return partes.Count <= 1 ? string.Join("", partes) : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }
    }
}
