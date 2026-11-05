using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using DigLab.conexion;
using iText = iTextSharp.text;
using iTextPdf = iTextSharp.text.pdf;

namespace DigLab
{
    /// <summary>Lo que el reporte dice de como se genero (la ficha de la primera hoja).</summary>
    internal sealed class DatosDelReporte
    {
        /// <summary>El nombre del periodo sin sus fechas (EstadisticasDatos.NombreDelPeriodo): "Ciclo abril-septiembre 2026, completo".</summary>
        public string Periodo = "";
        /// <summary>El nombre corto del subtitulo (EstadisticasDatos.NombreCortoDelPeriodo): "Este ciclo (en curso)", el de un ciclo pasado o "".</summary>
        public string NombreCorto = "";
        /// <summary>Con un ciclo pasado, lo que va al final de "Generado el" (EstadisticasDatos.NotaDelPeriodoParaElReporte); null en lo demas.</summary>
        public string NotaDelPeriodo;
        /// <summary>Los dias que de verdad cuentan.</summary>
        public DateTime Desde;
        public DateTime Hasta;
        public string Laboratorio = "";
        public string Docente = "";
        public string Grupo = "";
        public AgrupacionGraficas Agrupacion;
        /// <summary>La cuenta de la sesion y su papel: "ADMPRUEBA (administración de DigLab)".</summary>
        public string GeneradoPor = "";
        /// <summary>Hora del servidor al generarlo (la de este equipo con el desfase de la lectura).</summary>
        public DateTime GeneradoEn;
        /// <summary>Hora del servidor de la lectura (lo que dice lblResumen).</summary>
        public DateTime LeidoEn;
        /// <summary>EstadisticasDatos.ReporteConDocentes: sin el, la seccion 2 va solo con totales.</summary>
        public bool ConDocentes = true;
        /// <summary>
        /// Las cinco cifras de todo el TESCHI en el periodo, si hay un filtro (como arriba de
        /// la ventana: generales arriba, las secciones con los filtros); null sin filtros.
        /// </summary>
        public CifrasEstadisticas Generales;
        /// <summary>El titulo del reporte; null: ReporteAuditoriaPdf.Titulo ("Reporte de uso de laboratorios").</summary>
        public string Titulo;
        /// <summary>Las secciones ya armadas (el reporte del docente, ContenidoFichaDocente); null: ContenidoGraficas.Armar, como siempre.</summary>
        public List<SeccionGraficas> Secciones;
        /// <summary>Notas que van al final de "Como se calcula cada numero" (el reporte del docente); null: ninguna.</summary>
        public List<string> NotasFinales;
    }

    /// <summary>
    /// "Reporte para auditoria (PDF)" de Estadisticas (2026-10-31): el encabezado
    /// institucional (los logos y los tres renglones de la escuela, como el PDF del
    /// horario y los de bitacoras), la ficha del periodo y los filtros, las cinco
    /// cifras grandes, las cuatro secciones de Graficas (sus graficas, al triple de
    /// puntos, y sus tablas con las cifras exactas), como se calcula cada numero y
    /// los renglones de firma. Cada hoja lleva al pie el periodo y "Pagina X de Y".
    /// Todo sale de ContenidoGraficas: las cifras son las de la pantalla.
    /// </summary>
    internal static class ReporteAuditoriaPdf
    {
        public const string Titulo = "Reporte de uso de laboratorios";
        // Las graficas, al triple de los puntos de pantalla: unos 290 ppp en la hoja.
        private const float Escala = 3f;

        private static readonly iText.BaseColor Verde = Base(EstiloHorarios.VerdeTeschi);
        private static readonly iText.BaseColor Tinta = Base(ColoresGrafica.Tinta);
        private static readonly iText.BaseColor Gris = Base(EstiloAdmin.TextoSecundario);
        private static readonly iText.BaseColor GrisLinea = Base(EstiloHorarios.LineaSuave);
        private static readonly iText.BaseColor FondoEncabezado = new iText.BaseColor(240, 248, 240);
        private static readonly iText.BaseColor TextoEncabezado = new iText.BaseColor(0, 100, 0);
        private static readonly iText.BaseColor FondoTotal = Base(EstiloHorarios.VerdeSobre);

        public static byte[] Crear(ResultadoEstadisticas r, DatosDelReporte datos)
        {
            if (r == null || datos == null)
            {
                throw new ArgumentNullException(r == null ? "r" : "datos");
            }

            List<SeccionGraficas> secciones = datos.Secciones ?? ContenidoGraficas.Armar(r, datos.Agrupacion, datos.ConDocentes);
            string titulo = string.IsNullOrEmpty(datos.Titulo) ? Titulo : datos.Titulo;
            // "Ciclo abril-septiembre 2026 · Del 07/04/2026 al 04/09/2026", "Este ciclo (en curso) · Del ..." o "Del ... al ...".
            string nombreCorto = datos.NombreCorto ?? "";
            string subtitulo = (nombreCorto.Length > 0 ? nombreCorto + " · " : "") + EstadisticasDatos.DelAl(datos.Desde, datos.Hasta);
            using (MemoryStream salida = new MemoryStream())
            using (EstiloGrafica estilo = EstiloGrafica.Pdf())
            {
                iText.Document documento = new iText.Document(iText.PageSize.A4, 36f, 36f, 28f, 46f);
                iTextPdf.PdfWriter escritor = iTextPdf.PdfWriter.GetInstance(documento, salida);
                escritor.PageEvent = new PieDePagina(titulo + " · " + EstadisticasDatos.DelAl(datos.Desde, datos.Hasta)
                    + " · Generado con DigLab el " + datos.GeneradoEn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    + " a las " + datos.GeneradoEn.ToString("HH:mm", CultureInfo.InvariantCulture));
                documento.Open();
                documento.AddTitle(titulo);
                documento.AddSubject(subtitulo);
                documento.AddAuthor("DigLab");
                documento.AddCreator("DigLab");

                float ancho = documento.PageSize.Width - documento.LeftMargin - documento.RightMargin;
                documento.Add(Encabezado());
                documento.Add(new iText.Paragraph(titulo, Letra(iText.FontFactory.HELVETICA_BOLD, 16f, Verde))
                {
                    Alignment = iText.Element.ALIGN_CENTER,
                    SpacingBefore = 6f
                });
                documento.Add(new iText.Paragraph(subtitulo, Letra(iText.FontFactory.HELVETICA, 10.5f, Tinta))
                {
                    Alignment = iText.Element.ALIGN_CENTER,
                    SpacingAfter = 8f
                });
                documento.Add(Ficha(datos));
                if (datos.Generales != null)
                {
                    documento.Add(new iText.Paragraph("Cifras generales del periodo en todo el TESCHI, sin los filtros de la ficha; las secciones van con los filtros.",
                        Letra(iText.FontFactory.HELVETICA, 8.5f, Gris)) { SpacingAfter = 3f });
                }

                documento.Add(Resumen(datos.Generales ?? r.Cifras));

                foreach (SeccionGraficas seccion in secciones)
                {
                    AgregarSeccion(documento, escritor, seccion, estilo, ancho);
                }

                AgregarDefiniciones(documento, escritor, r, datos.NotasFinales);
                AgregarFirmas(documento, escritor);
                documento.Close();
                return salida.ToArray();
            }
        }

        /// <summary>"Reporte de uso de laboratorios_2026-04-07 al 2026-10-31_Laboratorio de Computo 1.pdf".</summary>
        public static string NombreArchivo(DatosDelReporte datos, bool conLaboratorio)
        {
            return PdfBitacora.NombreArchivo(Titulo, datos.Desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " al "
                + datos.Hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), conLaboratorio ? datos.Laboratorio : null);
        }

        // =====================================================================
        // Primera hoja
        // =====================================================================

        /// <summary>Los logos a los lados y los tres renglones de la escuela en medio (como PdfHorario.Encabezado).</summary>
        private static iTextPdf.PdfPTable Encabezado()
        {
            iTextPdf.PdfPTable tabla = new iTextPdf.PdfPTable(3) { WidthPercentage = 100f };
            tabla.SetWidths(new float[] { 20f, 60f, 20f });
            tabla.AddCell(CeldaLogo(Properties.Resources.estadoMexico, iText.Element.ALIGN_LEFT));
            iTextPdf.PdfPCell escuela = new iTextPdf.PdfPCell(new iText.Phrase(
                "TECNOLÓGICO DE ESTUDIOS SUPERIORES DE CHIMALHUACÁN\nORGANISMO PÚBLICO DESCENTRALIZADO DEL ESTADO DE MÉXICO\n"
                + "DIVISIÓN ACADÉMICA DE SISTEMAS COMPUTACIONALES", Letra(iText.FontFactory.HELVETICA, 9.5f, iText.BaseColor.BLACK)))
            {
                Border = iText.Rectangle.NO_BORDER,
                HorizontalAlignment = iText.Element.ALIGN_CENTER,
                VerticalAlignment = iText.Element.ALIGN_MIDDLE
            };
            escuela.SetLeading(0f, 1.3f);
            tabla.AddCell(escuela);
            tabla.AddCell(CeldaLogo(Properties.Resources.logoteschi, iText.Element.ALIGN_RIGHT));
            return tabla;
        }

        private static iTextPdf.PdfPCell CeldaLogo(Bitmap recurso, int alineacion)
        {
            // GetInstance copia la imagen: el Bitmap del recurso (uno nuevo en cada lectura) se libera ya.
            using (recurso)
            {
                iText.Image logo = iText.Image.GetInstance(recurso, System.Drawing.Imaging.ImageFormat.Png);
                logo.ScaleToFit(110f, 50f);
                return new iTextPdf.PdfPCell(logo, false)
                {
                    Border = iText.Rectangle.NO_BORDER,
                    HorizontalAlignment = alineacion,
                    VerticalAlignment = iText.Element.ALIGN_MIDDLE
                };
            }
        }

        /// <summary>El periodo, los filtros, la agrupacion, quien lo genero y cuando.</summary>
        private static iTextPdf.PdfPTable Ficha(DatosDelReporte datos)
        {
            iTextPdf.PdfPTable tabla = new iTextPdf.PdfPTable(2) { WidthPercentage = 100f, SpacingAfter = 10f };
            tabla.SetWidths(new float[] { 18f, 82f });
            iText.Font rotulo = Letra(iText.FontFactory.HELVETICA_BOLD, 8.5f, Gris);
            iText.Font valor = Letra(iText.FontFactory.HELVETICA, 8.5f, Tinta);
            string[][] renglones =
            {
                new[] { "Periodo", datos.Periodo + (datos.Periodo.Length > 0 ? ": " : "") + EstadisticasDatos.DelAl(datos.Desde, datos.Hasta).ToLower(CultureInfo.CurrentCulture) + "." },
                new[] { "Laboratorio", datos.Laboratorio },
                new[] { "Docente", datos.Docente },
                new[] { "Grupo", datos.Grupo },
                new[] { "Agrupado", datos.Agrupacion == AgrupacionGraficas.Mes ? "Por mes (del día 1 al último)." : "Por semana (de lunes a domingo)." },
                new[] { "Generado por", datos.GeneradoPor },
                new[]
                {
                    "Generado el", datos.GeneradoEn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " a las " + datos.GeneradoEn.ToString("HH:mm", CultureInfo.InvariantCulture)
                        + " (hora del servidor), con los datos leídos a las " + datos.LeidoEn.ToString("HH:mm", CultureInfo.InvariantCulture) + "."
                        + (datos.NotaDelPeriodo ?? "")
                }
            };
            foreach (string[] renglon in renglones)
            {
                tabla.AddCell(new iTextPdf.PdfPCell(new iText.Phrase(renglon[0], rotulo)) { Border = iText.Rectangle.NO_BORDER, PaddingBottom = 2.5f });
                tabla.AddCell(new iTextPdf.PdfPCell(new iText.Phrase(renglon[1] ?? "", valor)) { Border = iText.Rectangle.NO_BORDER, PaddingBottom = 2.5f });
            }

            return tabla;
        }

        /// <summary>Las cinco cifras grandes de Estadisticas, como arriba de la ventana.</summary>
        private static iTextPdf.PdfPTable Resumen(CifrasEstadisticas cifras)
        {
            CifraEstadistica[] cinco = { cifras.Asistencia, cifras.ClasesQueSeDieronCifra, cifras.Bitacoras, cifras.Fallas, cifras.SinInternet };
            iTextPdf.PdfPTable tabla = new iTextPdf.PdfPTable(cinco.Length) { WidthPercentage = 100f, SpacingAfter = 4f };
            foreach (CifraEstadistica cifra in cinco)
            {
                iTextPdf.PdfPCell celda = new iTextPdf.PdfPCell
                {
                    BackgroundColor = new iText.BaseColor(244, 250, 244),
                    BorderColor = new iText.BaseColor(205, 225, 205),
                    BorderWidth = 0.6f,
                    Padding = 6f,
                    PaddingBottom = 8f
                };
                celda.AddElement(new iText.Paragraph(cifra.Valor, Letra(iText.FontFactory.HELVETICA_BOLD, 14f, Base(ColorDe(cifra.Tono)))));
                celda.AddElement(new iText.Paragraph(cifra.Etiqueta, Letra(iText.FontFactory.HELVETICA_BOLD, 7.5f, Tinta)) { SpacingBefore = 1f });
                if (cifra.Detalle.Length > 0)
                {
                    celda.AddElement(new iText.Paragraph(cifra.Detalle, Letra(iText.FontFactory.HELVETICA, 7f, Gris)) { SpacingBefore = 1f });
                }

                tabla.AddCell(celda);
            }

            return tabla;
        }

        private static Color ColorDe(TonoCifra tono)
        {
            switch (tono)
            {
                case TonoCifra.Verde:
                    return CifraGrande.ColorVerde;
                case TonoCifra.Rojo:
                    return CifraGrande.ColorRojo;
                case TonoCifra.Naranja:
                    return CifraGrande.ColorNaranja;
                default:
                    return CifraGrande.ColorGris;
            }
        }

        // =====================================================================
        // Secciones
        // =====================================================================

        private static void AgregarSeccion(iText.Document documento, iTextPdf.PdfWriter escritor, SeccionGraficas seccion, EstiloGrafica estilo, float ancho)
        {
            float anchoLogico = ancho * 96f / 72f;
            // El titulo no se queda solo al pie de una hoja: con el, sus cifras y su primera grafica.
            float primera = seccion.SinDatos == null && seccion.Graficas.Count > 0 ? Math.Min(380f, seccion.Graficas[0].Alto(estilo, anchoLogico) * 72f / 96f) : 40f;
            AsegurarEspacio(documento, escritor, 80f + (string.IsNullOrEmpty(seccion.Lectura) ? 0f : 28f) + primera);
            documento.Add(new iText.Paragraph(seccion.Numero.ToString(CultureInfo.InvariantCulture) + ". " + seccion.Titulo, Letra(iText.FontFactory.HELVETICA_BOLD, 13f, Verde))
            {
                SpacingBefore = 14f,
                SpacingAfter = 1f
            });
            documento.Add(new iText.Paragraph(seccion.Descripcion, Letra(iText.FontFactory.HELVETICA, 8.5f, Gris)) { SpacingAfter = 6f });
            if (seccion.SinDatos != null)
            {
                documento.Add(new iText.Paragraph(seccion.SinDatos, Letra(iText.FontFactory.HELVETICA_OBLIQUE, 9.5f, Tinta)) { SpacingAfter = 4f });
                AgregarNotas(documento, seccion);
                return;
            }

            if (!string.IsNullOrEmpty(seccion.Lectura))
            {
                documento.Add(new iText.Paragraph(seccion.Lectura, Letra(iText.FontFactory.HELVETICA, 9.5f, Tinta)) { SpacingAfter = 6f });
            }

            documento.Add(Cifras(seccion.Cifras));
            foreach (Grafica grafica in seccion.Graficas)
            {
                AgregarGrafica(documento, escritor, grafica, estilo, ancho);
            }

            foreach (TablaDeCifras tabla in seccion.Tablas)
            {
                AgregarTabla(documento, escritor, tabla, ancho);
            }

            AgregarNotas(documento, seccion);
        }

        private static iTextPdf.PdfPTable Cifras(List<CifraResumen> cifras)
        {
            iTextPdf.PdfPTable tabla = new iTextPdf.PdfPTable(Math.Max(1, cifras.Count)) { WidthPercentage = 100f, SpacingAfter = 2f };
            foreach (CifraResumen cifra in cifras)
            {
                iTextPdf.PdfPCell celda = new iTextPdf.PdfPCell { Border = iText.Rectangle.NO_BORDER, PaddingLeft = 0f, PaddingBottom = 4f };
                celda.AddElement(new iText.Paragraph(cifra.Valor, Letra(iText.FontFactory.HELVETICA_BOLD, 12.5f, cifra.Color.HasValue ? Base(cifra.Color.Value) : Tinta)));
                celda.AddElement(new iText.Paragraph(cifra.Etiqueta, Letra(iText.FontFactory.HELVETICA, 7.5f, Gris)));
                tabla.AddCell(celda);
            }

            if (cifras.Count == 0)
            {
                tabla.AddCell(new iTextPdf.PdfPCell(new iText.Phrase(" ")) { Border = iText.Rectangle.NO_BORDER });
            }

            return tabla;
        }

        /// <summary>La grafica como imagen al ancho de la hoja; si no cabe en una hoja, partida por filas.</summary>
        private static void AgregarGrafica(iText.Document documento, iTextPdf.PdfWriter escritor, Grafica grafica, EstiloGrafica estilo, float ancho)
        {
            float anchoLogico = ancho * 96f / 72f;
            float altoHoja = documento.PageSize.Height - documento.TopMargin - documento.BottomMargin - 20f;
            IEnumerable<Grafica> partes = grafica is BarrasHorizontales
                ? ((BarrasHorizontales)grafica).EnPartes(estilo, anchoLogico, altoHoja * 96f / 72f).Cast<Grafica>()
                : new[] { grafica };
            foreach (Grafica parte in partes)
            {
                using (Bitmap dibujo = parte.ComoImagen(estilo, anchoLogico, Escala))
                {
                    float alto = dibujo.Height / Escala * 72f / 96f;
                    iText.Image imagen = iText.Image.GetInstance(dibujo, System.Drawing.Imaging.ImageFormat.Png);
                    imagen.ScaleAbsolute(ancho, alto);
                    imagen.SpacingBefore = 8f;
                    imagen.SpacingAfter = 2f;
                    AsegurarEspacio(documento, escritor, alto + 12f);
                    documento.Add(imagen);
                }
            }
        }

        /// <summary>
        /// Una tabla con su titulo (que se repite si pasa a otra hoja con el
        /// encabezado), al ancho que piden sus columnas y como mucho el de la hoja.
        /// </summary>
        private static void AgregarTabla(iText.Document documento, iTextPdf.PdfWriter escritor, TablaDeCifras datos, float anchoHoja)
        {
            if (datos.Columnas.Count == 0)
            {
                return;
            }

            const float Tamano = 7.5f;
            const float Relleno = 4f;
            iText.Font normal = Letra(iText.FontFactory.HELVETICA, Tamano, Tinta);
            iText.Font negrita = Letra(iText.FontFactory.HELVETICA_BOLD, Tamano, Tinta);
            iText.Font encabezado = Letra(iText.FontFactory.HELVETICA_BOLD, Tamano, TextoEncabezado);
            iTextPdf.BaseFont medida = negrita.GetCalculatedBaseFont(false);
            int columnas = datos.Columnas.Count;
            float[] anchos = new float[columnas];
            for (int i = 0; i < columnas; i++)
            {
                float pide = medida.GetWidthPoint(datos.Columnas[i].Titulo, Tamano);
                foreach (FilaDeTabla fila in datos.Filas)
                {
                    if (i < fila.Celdas.Length)
                    {
                        pide = Math.Max(pide, medida.GetWidthPoint(fila.Celdas[i] ?? "", Tamano));
                    }
                }

                // Los nombres largos (docentes, materias) se parten en dos renglones antes que estirar la tabla.
                anchos[i] = Math.Min(datos.Columnas[i].Derecha ? 120f : 190f, pide) + 2f * Relleno + 2f;
            }

            float total = anchos.Sum();
            iTextPdf.PdfPTable tabla = new iTextPdf.PdfPTable(columnas)
            {
                TotalWidth = Math.Min(anchoHoja, total),
                LockedWidth = true,
                HorizontalAlignment = iText.Element.ALIGN_LEFT,
                SpacingBefore = 9f,
                HeaderRows = 2
            };
            tabla.SetWidths(anchos);
            tabla.AddCell(new iTextPdf.PdfPCell(new iText.Phrase(datos.Titulo, Letra(iText.FontFactory.HELVETICA_BOLD, 9f, Tinta)))
            {
                Colspan = columnas,
                Border = iText.Rectangle.NO_BORDER,
                PaddingLeft = 0f,
                PaddingBottom = 4f
            });
            foreach (ColumnaDeTabla columna in datos.Columnas)
            {
                tabla.AddCell(new iTextPdf.PdfPCell(new iText.Phrase(columna.Titulo, encabezado))
                {
                    BackgroundColor = FondoEncabezado,
                    Border = iText.Rectangle.NO_BORDER,
                    Padding = Relleno,
                    HorizontalAlignment = columna.Derecha ? iText.Element.ALIGN_RIGHT : iText.Element.ALIGN_LEFT,
                    VerticalAlignment = iText.Element.ALIGN_BOTTOM
                });
            }

            foreach (FilaDeTabla fila in datos.Filas)
            {
                for (int i = 0; i < columnas; i++)
                {
                    string texto = i < fila.Celdas.Length ? fila.Celdas[i] ?? "" : "";
                    Color color;
                    iText.Font letra = fila.Colores.TryGetValue(i, out color)
                        ? Letra(fila.Total ? iText.FontFactory.HELVETICA_BOLD : iText.FontFactory.HELVETICA, Tamano, Base(color))
                        : fila.Total ? negrita : normal;
                    iTextPdf.PdfPCell celda = new iTextPdf.PdfPCell(new iText.Phrase(texto, letra))
                    {
                        Border = iText.Rectangle.BOTTOM_BORDER,
                        BorderColorBottom = GrisLinea,
                        BorderWidthBottom = 0.6f,
                        Padding = Relleno,
                        PaddingTop = 3f,
                        HorizontalAlignment = datos.Columnas[i].Derecha ? iText.Element.ALIGN_RIGHT : iText.Element.ALIGN_LEFT
                    };
                    if (fila.Total)
                    {
                        celda.BackgroundColor = FondoTotal;
                    }

                    tabla.AddCell(celda);
                }
            }

            // Con el titulo, el encabezado y al menos tres filas en la misma hoja.
            AsegurarEspacio(documento, escritor, 26f + 18f * Math.Min(3, datos.Filas.Count) + 16f);
            documento.Add(tabla);
        }

        private static void AgregarNotas(iText.Document documento, SeccionGraficas seccion)
        {
            foreach (string nota in seccion.Notas)
            {
                documento.Add(new iText.Paragraph(nota, Letra(iText.FontFactory.HELVETICA_OBLIQUE, 7.5f, Gris)) { SpacingBefore = 4f });
            }
        }

        // =====================================================================
        // Como se calcula y firmas
        // =====================================================================

        private static void AgregarDefiniciones(iText.Document documento, iTextPdf.PdfWriter escritor, ResultadoEstadisticas r, List<string> notasFinales)
        {
            AsegurarEspacio(documento, escritor, 160f);
            documento.Add(new iText.Paragraph("Cómo se calcula cada número", Letra(iText.FontFactory.HELVETICA_BOLD, 13f, Verde)) { SpacingBefore = 16f, SpacingAfter = 4f });
            iText.Font termino = Letra(iText.FontFactory.HELVETICA_BOLD, 8f, Tinta);
            iText.Font texto = Letra(iText.FontFactory.HELVETICA, 8f, Tinta);
            foreach (Definicion definicion in ContenidoGraficas.Definiciones(r))
            {
                iText.Paragraph parrafo = new iText.Paragraph { SpacingAfter = 3.5f, Leading = 10.5f };
                parrafo.Add(new iText.Chunk(definicion.Termino + ". ", termino));
                parrafo.Add(new iText.Chunk(definicion.ComoSeCuenta, texto));
                documento.Add(parrafo);
            }

            if (notasFinales == null)
            {
                return;
            }

            iText.Font nota = Letra(iText.FontFactory.HELVETICA_BOLD, 8f, Tinta);
            foreach (string texto2 in notasFinales.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                documento.Add(new iText.Paragraph(texto2, nota) { SpacingBefore = 3f, Leading = 10.5f });
            }
        }

        /// <summary>Elaboro y Reviso, con nombre y cargo en blanco (propuesta: el usuario decide si se quedan).</summary>
        private static void AgregarFirmas(iText.Document documento, iTextPdf.PdfWriter escritor)
        {
            AsegurarEspacio(documento, escritor, 130f);
            iTextPdf.PdfPTable firmas = new iTextPdf.PdfPTable(2) { WidthPercentage = 100f, SpacingBefore = 28f, KeepTogether = true };
            iText.Font rotulo = Letra(iText.FontFactory.HELVETICA_BOLD, 9f, Tinta);
            iText.Font linea = Letra(iText.FontFactory.HELVETICA, 8.5f, Tinta);
            foreach (string quien in new[] { "Elaboró", "Revisó" })
            {
                iTextPdf.PdfPTable una = new iTextPdf.PdfPTable(1) { WidthPercentage = 100f };
                una.AddCell(new iTextPdf.PdfPCell(new iText.Phrase(" ")) { FixedHeight = 40f, Border = iText.Rectangle.NO_BORDER });
                una.AddCell(new iTextPdf.PdfPCell(new iText.Phrase(quien, rotulo))
                {
                    Border = iText.Rectangle.TOP_BORDER,
                    BorderColorTop = Tinta,
                    BorderWidthTop = 0.6f,
                    HorizontalAlignment = iText.Element.ALIGN_CENTER,
                    PaddingTop = 4f
                });
                una.AddCell(new iTextPdf.PdfPCell(new iText.Phrase("Nombre: ________________________________", linea)) { Border = iText.Rectangle.NO_BORDER, PaddingTop = 10f });
                una.AddCell(new iTextPdf.PdfPCell(new iText.Phrase("Cargo: _________________________________", linea)) { Border = iText.Rectangle.NO_BORDER, PaddingTop = 8f });
                firmas.AddCell(new iTextPdf.PdfPCell(una) { Border = iText.Rectangle.NO_BORDER, PaddingLeft = 18f, PaddingRight = 18f });
            }

            documento.Add(firmas);
        }

        // =====================================================================
        // Ayudantes
        // =====================================================================

        /// <summary>Si en la hoja no quedan esos puntos, a la siguiente.</summary>
        private static void AsegurarEspacio(iText.Document documento, iTextPdf.PdfWriter escritor, float alto)
        {
            if (escritor.GetVerticalPosition(false) - documento.BottomMargin < alto)
            {
                documento.NewPage();
            }
        }

        private static iText.Font Letra(string nombre, float tamano, iText.BaseColor color)
        {
            return iText.FontFactory.GetFont(nombre, tamano, color);
        }

        private static iText.BaseColor Base(Color color)
        {
            return new iText.BaseColor(color.R, color.G, color.B);
        }

        /// <summary>El pie de cada hoja: de que es el reporte y "Pagina X de Y" (Y se escribe al cerrar).</summary>
        private sealed class PieDePagina : iTextPdf.PdfPageEventHelper
        {
            private const float Tamano = 7.5f;
            private readonly string texto;
            private iTextPdf.PdfTemplate total;
            private iTextPdf.BaseFont letra;
            // Las hojas terminadas: al cerrar, todas (Close termina la ultima antes de OnCloseDocument).
            private int hojas;

            public PieDePagina(string texto)
            {
                this.texto = texto;
            }

            public override void OnOpenDocument(iTextPdf.PdfWriter escritor, iText.Document documento)
            {
                total = escritor.DirectContent.CreateTemplate(30f, 12f);
                letra = iTextPdf.BaseFont.CreateFont(iTextPdf.BaseFont.HELVETICA, iTextPdf.BaseFont.CP1252, iTextPdf.BaseFont.NOT_EMBEDDED);
            }

            public override void OnEndPage(iTextPdf.PdfWriter escritor, iText.Document documento)
            {
                hojas++;
                iTextPdf.PdfContentByte lienzo = escritor.DirectContent;
                float izquierda = documento.LeftMargin;
                float derecha = documento.PageSize.Width - documento.RightMargin;
                float y = documento.BottomMargin - 24f;
                lienzo.SaveState();
                lienzo.SetColorStroke(GrisLinea);
                lienzo.SetLineWidth(0.6f);
                lienzo.MoveTo(izquierda, y + 11f);
                lienzo.LineTo(derecha, y + 11f);
                lienzo.Stroke();
                lienzo.RestoreState();

                string pagina = "Página " + hojas.ToString(CultureInfo.InvariantCulture) + " de ";
                float anchoTotal = letra.GetWidthPoint("000", Tamano);
                lienzo.BeginText();
                lienzo.SetFontAndSize(letra, Tamano);
                lienzo.SetColorFill(Gris);
                lienzo.ShowTextAligned(iText.Element.ALIGN_LEFT, texto, izquierda, y, 0f);
                lienzo.ShowTextAligned(iText.Element.ALIGN_RIGHT, pagina, derecha - anchoTotal, y, 0f);
                lienzo.EndText();
                lienzo.AddTemplate(total, derecha - anchoTotal, y);
            }

            public override void OnCloseDocument(iTextPdf.PdfWriter escritor, iText.Document documento)
            {
                total.BeginText();
                total.SetFontAndSize(letra, Tamano);
                total.SetColorFill(Gris);
                total.SetTextMatrix(0f, 0f);
                total.ShowText(hojas.ToString(CultureInfo.InvariantCulture));
                total.EndText();
            }
        }
    }
}
