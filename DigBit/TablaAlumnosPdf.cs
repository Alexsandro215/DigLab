using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using DigBit.conexion;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.tool.xml;
using MySql.Data.MySqlClient;

namespace DigBit
{
    /// <summary>
    /// El PDF de la bitacora de una sesion: el encabezado de la plantilla
    /// (Resources\plantilla.html) con los logos y la tabla de alumnos. Lo usan
    /// Bitacoras (una sesion o un lote) y la bitacora de una clase
    /// (BitacoraSesion), que antes tenian cada una su copia. Debajo, si las hay,
    /// las asistencias que dio el profesor a quien no pudo registrar (db/22):
    /// en su propia tabla, porque no son bitacoras.
    /// </summary>
    internal static class PdfBitacora
    {
        /// <summary>
        /// El PDF en destino (pdfDoc.Close lo cierra; ToArray de un MemoryStream sirve
        /// igual). dadas: las que cuentan de esa sesion (LectorDeDadas.DeLaSesion, con
        /// el nombre como en la tabla de bitacoras); vacia o null, el PDF de siempre.
        /// </summary>
        public static void Escribir(EncabezadoPdfSesion datos, DataTable alumnos, IList<AsistenciaDada> dadas, Stream destino)
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
            TablaAlumnosPdf.AgregarDadas(pdfDoc, dadas, alumnos);
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

        /// <summary>
        /// Las asistencias que dio el profesor a quien no pudo registrar su bitacora
        /// (db/22), debajo de la de bitacoras y en su propia tabla: cuentan como
        /// asistencia, pero no son bitacoras. Sin ninguna (o sin la 22) no se anade
        /// nada y el PDF queda como siempre. alumnos (la tabla de bitacoras de la
        /// misma sesion) dice si despues llego tambien su bitacora.
        /// </summary>
        public static void AgregarDadas(Document pdfDoc, IList<AsistenciaDada> dadas, DataTable alumnos)
        {
            if (dadas == null || dadas.Count == 0)
            {
                return;
            }

            Font normal = FontFactory.GetFont(FontFactory.HELVETICA, 10f);
            pdfDoc.Add(new Paragraph("Asistencias que dio su docente sin bitácora (no son bitácoras)",
                FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11f)) { SpacingBefore = 12f });

            PdfPTable tabla = new PdfPTable(6) { WidthPercentage = 100, SpacingBefore = 4f };
            tabla.SetWidths(new float[] { 6f, 14f, 27f, 8f, 33f, 12f });
            foreach (string encabezado in new[] { "No.", "Matrícula", "Nombre completo", "Hora", "Motivo", "Equipo" })
            {
                tabla.AddCell(new PdfPCell(new Phrase(encabezado)) { BackgroundColor = BaseColor.LIGHT_GRAY, Padding = 4f });
            }

            HashSet<string> conBitacora = new HashSet<string>();
            if (alumnos != null && alumnos.Columns.Contains("fk_usuario"))
            {
                foreach (DataRow fila in alumnos.Rows)
                {
                    conBitacora.Add(fila["fk_usuario"].ToString());
                }
            }

            int numeroFila = 1;
            foreach (AsistenciaDada dada in dadas)
            {
                // Si su bitacora llego despues (la cola sin conexion), cuenta una sola vez;
                // se dice para que no parezca un error verlo en las dos tablas.
                string motivo = MotivoDeLaDada(dada)
                    + (conBitacora.Contains(dada.AlumnoId.ToString(CultureInfo.InvariantCulture)) ? " (después llegó su bitácora)" : "");
                string[] celdas =
                {
                    numeroFila.ToString(CultureInfo.InvariantCulture),
                    dada.Matricula,
                    dada.Nombre,
                    dada.DadaEn.ToString("HH:mm", CultureInfo.InvariantCulture),
                    motivo,
                    string.IsNullOrWhiteSpace(dada.Equipo) ? "—" : dada.Equipo
                };
                foreach (string texto in celdas)
                {
                    tabla.AddCell(new PdfPCell(new Phrase(texto ?? "", normal)) { Padding = 4f });
                }

                numeroFila++;
            }

            pdfDoc.Add(tabla);
            pdfDoc.Add(new Paragraph("Su docente las dio durante la clase a quien estaba en el salón y no pudo registrar su bitácora. "
                + "Cada una cuenta como asistencia, pero no es una bitácora: no registra el uso de un equipo ni sus fallas.",
                FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8f)) { SpacingBefore = 6f });
        }

        /// <summary>
        /// Por que se dio, para una tabla: "Su equipo no tenia internet" (con su
        /// acento) y, si el profesor escribio algo, ": lo que escribio"; con "otro",
        /// solo lo que escribio.
        /// </summary>
        internal static string MotivoDeLaDada(AsistenciaDada dada)
        {
            if (dada.Motivo == AsistenciasDadas.Otro)
            {
                string frase = AsistenciasDadas.EnFrase(dada);
                return frase.Length == 0 ? AsistenciasDadas.NombreDeMotivo(dada.Motivo) : char.ToUpper(frase[0]) + frase.Substring(1);
            }

            string detalle = (dada.Comentario ?? "").Trim();
            return AsistenciasDadas.NombreDeMotivo(dada.Motivo) + (detalle.Length > 0 ? ": " + detalle : "");
        }
    }

    /// <summary>
    /// Las asistencias dadas de una sesion para las vistas de bitacoras
    /// (BitacoraSesion cada 10 s, el detalle y los PDF de Bitacoras). Recuerda si
    /// la base tiene db/22: sin ella, despues de la primera vez ni siquiera abre
    /// una conexion, y todo queda como antes. Lo recordado vale para la cuenta con
    /// la que se vio (la del equipo o la de solo consulta, Conexion): si cambia,
    /// se vuelve a preguntar. Una ventana nueva tambien pregunta: asi se nota la
    /// migracion sin reiniciar.
    /// </summary>
    internal sealed class LectorDeDadas
    {
        // null: aun no se sabe (la primera lectura, o la anterior fallo antes de saberlo).
        private bool? conDadas;
        // Con que cuenta se supo (Conexion.ConCuentaDeSesion).
        private bool conCuentaDeSesion;

        /// <summary>
        /// Las que cuentan de esa sesion (AsistenciasDadas.DeLaSesion), de la primera a
        /// la ultima, con el nombre como en la tabla de bitacoras ("Nombre Apellidos"),
        /// o vacia sin db/22.
        /// </summary>
        public List<AsistenciaDada> DeLaSesion(int idSesion)
        {
            if (!PuedeHaber(idSesion))
            {
                return new List<AsistenciaDada>();
            }

            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                if (!ConDadas(conexion))
                {
                    return new List<AsistenciaDada>();
                }

                List<AsistenciaDada> dadas = AsistenciasDadas.DeLaSesion(conexion, idSesion);
                PonerNombres(conexion, dadas);
                return dadas;
            }
        }

        /// <summary>Cuantas cuentan en esa sesion (0 sin db/22), sin leerlas una por una.</summary>
        public int Contar(int idSesion)
        {
            if (!PuedeHaber(idSesion))
            {
                return 0;
            }

            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                int cuantas;
                return ConDadas(conexion) && AsistenciasDadas.ContarPorSesion(conexion, new[] { idSesion }).TryGetValue(idSesion, out cuantas)
                    ? cuantas : 0;
            }
        }

        /// <summary>Si vale la pena abrir una conexion: no, si ya se sabe que esta cuenta no ve la tabla.</summary>
        private bool PuedeHaber(int idSesion)
        {
            if (conDadas.HasValue && conCuentaDeSesion != Conexion.ConCuentaDeSesion)
            {
                conDadas = null;
            }

            return idSesion > 0 && conDadas != false;
        }

        private bool ConDadas(MySqlConnection conexion)
        {
            if (!conDadas.HasValue)
            {
                conCuentaDeSesion = Conexion.ConCuentaDeSesion;
                conDadas = Esquema.AsistenciaDada(conexion);
            }

            return conDadas.Value;
        }

        /// <summary>
        /// El nombre de cada alumno como lo pone la tabla de bitacoras de la misma
        /// sesion (consultaRegistroSesion: nombre, apellido paterno y materno), en
        /// una sola consulta: DeLaSesion lo trae como "Apellidos Nombre" y en el PDF
        /// y en la Bitacora de la clase las dos tablas deben decirlo igual. Si una
        /// cuenta no aparece, se queda el que traia.
        /// </summary>
        private static void PonerNombres(MySqlConnection conexion, List<AsistenciaDada> dadas)
        {
            List<int> ids = dadas.Select(d => d.AlumnoId).Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            Dictionary<int, string> nombres = new Dictionary<int, string>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idusuarios, nombre, apellido_paterno, apellido_materno FROM usuarios WHERE idusuarios IN ("
                + string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))) + ")", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    string nombre = (System.Convert.ToString(reader["nombre"], CultureInfo.InvariantCulture) + " "
                        + System.Convert.ToString(reader["apellido_paterno"], CultureInfo.InvariantCulture) + " "
                        + System.Convert.ToString(reader["apellido_materno"], CultureInfo.InvariantCulture)).Trim();
                    nombres[System.Convert.ToInt32(reader["idusuarios"], CultureInfo.InvariantCulture)] = nombre;
                }
            }

            foreach (AsistenciaDada dada in dadas)
            {
                string nombre;
                if (nombres.TryGetValue(dada.AlumnoId, out nombre) && nombre.Length > 0)
                {
                    dada.Nombre = nombre;
                }
            }
        }
    }
}
