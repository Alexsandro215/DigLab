using System.Data;
using DigBit.conexion;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace DigBit
{
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
            PdfPTable tabla = new PdfPTable(5) { WidthPercentage = 100 };
            tabla.SetWidths(new float[] { 6f, 27f, 9f, 16f, 42f });
            foreach (string encabezado in new[] { "No.", "Nombre Completo", "Hora", "Computadora", "Comentarios" })
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

            pdfDoc.Add(tabla);

            if (hayFallas)
            {
                pdfDoc.Add(new Paragraph("Celda naranja intensa: la falla o el comentario que reporto el alumno.",
                    FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8f)) { SpacingBefore = 6f });
            }
        }
    }
}
