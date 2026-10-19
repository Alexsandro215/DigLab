using System.Data;
using System.IO;
using System.Linq;
using DigBit.conexion;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.tool.xml;

namespace DigBit
{
    /// <summary>
    /// El PDF de la bitacora de una sesion: el encabezado de la plantilla
    /// (Resources\plantilla.html) con los logos y la tabla de alumnos. Lo usan
    /// Bitacoras (una sesion o un lote) y la bitacora de una clase
    /// (BitacoraSesion), que antes tenian cada una su copia.
    /// </summary>
    internal static class PdfBitacora
    {
        /// <summary>El PDF en destino (pdfDoc.Close lo cierra; ToArray de un MemoryStream sirve igual).</summary>
        public static void Escribir(EncabezadoPdfSesion datos, DataTable alumnos, Stream destino)
        {
            // Lo que falta (el profesor y el grupo del uso libre), con un guion. La
            // plantilla se lee como XHTML: un '&' o un '<' en un nombre la romperia.
            string html = Properties.Resources.plantilla
                .Replace("@HORARIODEENTRADA", Xhtml(datos.HoraEntrada))
                .Replace("@HORARIODESALIDA", Xhtml(datos.HoraSalida))
                .Replace("@FECHA", Xhtml(datos.Fecha))
                .Replace("@PROFESOR", Xhtml(datos.Profesor))
                .Replace("@GRUPO", Xhtml(datos.Grupo))
                .Replace("@MATERIA", Xhtml(datos.Materia))
                .Replace("@LABORATORIO", Xhtml(datos.Laboratorio));

            Document pdfDoc = new Document(PageSize.A4, 25, 25, 25, 25);
            PdfWriter writer = PdfWriter.GetInstance(pdfDoc, destino);
            pdfDoc.Open();
            pdfDoc.AddTitle("Bitácora DigBit");
            pdfDoc.AddAuthor("DigBit");

            // Los logos van en posicion fija (y = 750), a los lados del titulo; el
            // titulo de la plantilla deja arriba el hueco para que no se encimen.
            AgregarLogo(pdfDoc, Properties.Resources.logoteschi, 470);
            AgregarLogo(pdfDoc, Properties.Resources.estadoMexico, 40);

            using (StringReader sr = new StringReader(html))
            {
                XMLWorkerHelper.GetInstance().ParseXHtml(writer, pdfDoc, sr);
            }

            TablaAlumnosPdf.Agregar(pdfDoc, alumnos);
            pdfDoc.Close();
        }

        /// <summary>
        /// "codigo_2026-09-29_0700_laboratorio_profesor.pdf": las partes vacias no se
        /// ponen y lo que Windows no admite en un nombre de archivo pasa a "_". La
        /// fecha va como aaaa-mm-dd para que los archivos se ordenen solos.
        /// </summary>
        public static string NombreArchivo(params string[] partes)
        {
            string nombre = string.Join("_", partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
            if (nombre.Length == 0)
            {
                nombre = "Bitacora";
            }

            foreach (char invalido in Path.GetInvalidFileNameChars())
            {
                nombre = nombre.Replace(invalido, '_');
            }

            return nombre + ".pdf";
        }

        /// <summary>El logo a la izquierda (x = 40) o a la derecha (x = 470), de 100 x 60 como mucho.</summary>
        private static void AgregarLogo(Document pdfDoc, System.Drawing.Bitmap imagen, float x)
        {
            // GetInstance copia la imagen: el Bitmap del recurso (uno nuevo en cada lectura) se libera ya.
            using (imagen)
            {
                Image logo = Image.GetInstance(imagen, System.Drawing.Imaging.ImageFormat.Png);
                logo.ScaleToFit(100, 60);
                logo.Alignment = Image.UNDERLYING;
                logo.SetAbsolutePosition(x, 750);
                pdfDoc.Add(logo);
            }
        }

        /// <summary>El texto para la plantilla: un guion si esta vacio, y con los caracteres especiales de XML escapados.</summary>
        private static string Xhtml(string texto)
        {
            return string.IsNullOrWhiteSpace(texto)
                ? "-"
                : texto.Trim().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    /// <summary>
    /// La tabla de alumnos de una sesion en los PDF de bitacoras: la del
    /// administrador, la de la sesion del profesor y la de su historial (antes
    /// habia tres copias iguales). Lleva la hora a la que registro cada alumno,
    /// la de su primer registro: con dos clases seguidas del mismo profesor es lo
    /// que dice si llego al final de una o al principio de la otra.
    /// </summary>
    internal static class TablaAlumnosPdf
    {
        // La fila que reporta algo va en naranja claro y la celda con la falla en
        // naranja intenso, como en las tablas de la aplicacion.
        private static readonly BaseColor Suave = new BaseColor(255, 240, 214);
        private static readonly BaseColor Fuerte = new BaseColor(255, 176, 79);

        public static void Agregar(Document pdfDoc, DataTable detalles)
        {
            // Un poco separada del encabezado: si no, la raya del laboratorio se junta con su borde.
            PdfPTable tabla = new PdfPTable(5) { WidthPercentage = 100, SpacingBefore = 8f };
            tabla.SetWidths(new float[] { 6f, 27f, 9f, 16f, 42f });
            foreach (string encabezado in new[] { "No.", "Nombre completo", "Hora", "Computadora", "Comentarios" })
            {
                tabla.AddCell(new PdfPCell(new Phrase(encabezado)) { BackgroundColor = BaseColor.LIGHT_GRAY, Padding = 4f });
            }

            // Siempre con fuente: una Phrase con fuente null hace fallar a iTextSharp
            // al anadir la tabla (asi fallaban antes dos de las tres copias).
            Font normal = FontFactory.GetFont(FontFactory.HELVETICA, 10f);
            Font negrita = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10f);
            bool hayFallas = false;
            int numeroFila = 1;
            foreach (DataRow fila in detalles.Rows)
            {
                bool conFalla = Consultas.ConFalla(fila);
                hayFallas |= conFalla;

                string[] celdas =
                {
                    numeroFila.ToString(),
                    fila["nombre_completo"].ToString(),
                    fila["hora_registro"].ToString(),
                    fila["numero_computadora"].ToString(),
                    Consultas.DescribirFallas(fila)
                };
                for (int i = 0; i < celdas.Length; i++)
                {
                    bool esLaFalla = conFalla && i == celdas.Length - 1;
                    PdfPCell celda = new PdfPCell(new Phrase(celdas[i], esLaFalla ? negrita : normal)) { Padding = 4f };
                    if (conFalla)
                    {
                        celda.BackgroundColor = esLaFalla ? Fuerte : Suave;
                    }

                    tabla.AddCell(celda);
                }

                numeroFila++;
            }

            if (detalles.Rows.Count == 0)
            {
                // Bitacoras deja descargar una sesion sin nadie: que no parezca que falta la tabla.
                tabla.AddCell(new PdfPCell(new Phrase("Nadie registró su bitácora en esta sesión.", normal))
                {
                    Colspan = 5,
                    Padding = 6f,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
            }

            pdfDoc.Add(tabla);

            if (hayFallas)
            {
                pdfDoc.Add(new Paragraph("Celda naranja intensa: la falla o el comentario que se reportó en esa bitácora.",
                    FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8f)) { SpacingBefore = 6f });
            }

            // Lo que avisaron de los equipos de alrededor (db/10), contado por equipo.
            if (detalles.Columns.Contains("avisa_sin_internet"))
            {
                string avisos = Consultas.DescribirSinInternet(Consultas.ContarSinInternet(
                    detalles.Rows.Cast<DataRow>().Select(f => f["avisa_sin_internet"].ToString())), false);
                if (avisos.Length > 0)
                {
                    pdfDoc.Add(new Paragraph(avisos + ".", negrita) { SpacingBefore = 6f });
                }
            }
        }
    }
}
