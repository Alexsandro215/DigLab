using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace DigLab.Infraestructura
{
    /// <summary>Como se ve una casilla de un libro de Excel (LibroExcel). Las que dicen lo mismo comparten estilo.</summary>
    internal sealed class EstiloExcel
    {
        public bool Negrita;
        public float Tamano = 11F;
        public Color Letra = Color.Black;
        /// <summary>Empty: sin relleno.</summary>
        public Color Fondo = Color.Empty;
        /// <summary>null (como Excel decida), "left", "center" o "right".</summary>
        public string Horizontal;
        /// <summary>El texto se parte en renglones dentro de la casilla.</summary>
        public bool Ajustar;
        /// <summary>0, o 90: de abajo arriba (las fechas de la lista).</summary>
        public int Rotacion;
        /// <summary>Las cuatro orillas en gris fino, como la cuadricula de la lista.</summary>
        public bool Cuadricula;
        /// <summary>A la derecha, la linea gruesa que separa los bloques de la lista.</summary>
        public bool DivisoriaDerecha;
        /// <summary>Arriba, la linea gruesa de la fila de totales.</summary>
        public bool LineaArriba;
        /// <summary>null: General; "0%": porcentaje sin decimales.</summary>
        public string Formato;

        public EstiloExcel Copiar()
        {
            return (EstiloExcel)MemberwiseClone();
        }
    }

    /// <summary>Una hoja de un libro de Excel: casillas, anchos, combinadas, lo congelado y como se imprime.</summary>
    internal sealed class HojaExcel
    {
        internal sealed class Casilla
        {
            public string Texto;
            public double? Numero;
            public EstiloExcel Estilo;
        }

        public readonly string Nombre;
        internal readonly SortedDictionary<int, SortedDictionary<int, Casilla>> Filas = new SortedDictionary<int, SortedDictionary<int, Casilla>>();
        internal readonly SortedDictionary<int, double> Anchos = new SortedDictionary<int, double>();
        internal readonly Dictionary<int, double> Altos = new Dictionary<int, double>();
        internal readonly List<string> Combinadas = new List<string>();
        internal int FilasCongeladas;
        internal int ColumnasCongeladas;
        /// <summary>La fila de encabezados que se repite arriba de cada pagina al imprimir (0: ninguna).</summary>
        internal int RepetirAlImprimir;
        /// <summary>Al imprimir: horizontal y todo el ancho en una pagina.</summary>
        public bool Horizontal = true;
        /// <summary>Lo que va abajo a la izquierda de cada pagina impresa (a la derecha, "Pagina N de M").</summary>
        public string Pie = "";

        public HojaExcel(string nombre)
        {
            Nombre = NombreValido(nombre);
        }

        public void Texto(int fila, int columna, string texto, EstiloExcel estilo)
        {
            Poner(fila, columna, new Casilla { Texto = texto ?? "", Estilo = estilo });
        }

        public void Numero(int fila, int columna, double valor, EstiloExcel estilo)
        {
            Poner(fila, columna, new Casilla { Numero = valor, Estilo = estilo });
        }

        /// <summary>Sin nada escrito, pero con su estilo (el borde de una combinada, una muestra de color).</summary>
        public void Vacia(int fila, int columna, EstiloExcel estilo)
        {
            Poner(fila, columna, new Casilla { Estilo = estilo });
        }

        /// <summary>Junta de la columna desde a la hasta en una fila; las de en medio llevan el mismo estilo (asi se ven sus orillas).</summary>
        public void Combinar(int fila, int desde, int hasta)
        {
            if (hasta <= desde)
            {
                return;
            }

            Casilla primera = Buscar(fila, desde);
            EstiloExcel estilo = primera == null ? null : primera.Estilo;
            for (int c = desde + 1; c <= hasta; c++)
            {
                if (Buscar(fila, c) == null && estilo != null)
                {
                    Vacia(fila, c, estilo);
                }
            }

            Combinadas.Add(LibroExcel.Celda(fila, desde) + ":" + LibroExcel.Celda(fila, hasta));
        }

        public void Ancho(int columna, double ancho)
        {
            Anchos[columna] = ancho;
        }

        public void Alto(int fila, double alto)
        {
            Altos[fila] = alto;
        }

        public void Congelar(int filas, int columnas)
        {
            FilasCongeladas = filas;
            ColumnasCongeladas = columnas;
        }

        internal Casilla Buscar(int fila, int columna)
        {
            SortedDictionary<int, Casilla> renglon;
            Casilla casilla;
            return Filas.TryGetValue(fila, out renglon) && renglon.TryGetValue(columna, out casilla) ? casilla : null;
        }

        private void Poner(int fila, int columna, Casilla casilla)
        {
            if (fila < 1 || columna < 1)
            {
                throw new ArgumentOutOfRangeException(fila < 1 ? "fila" : "columna");
            }

            SortedDictionary<int, Casilla> renglon;
            if (!Filas.TryGetValue(fila, out renglon))
            {
                renglon = new SortedDictionary<int, Casilla>();
                Filas[fila] = renglon;
            }

            renglon[columna] = casilla;
        }

        /// <summary>Excel no admite []:*?/\ en el nombre de una hoja, ni mas de 31 caracteres.</summary>
        private static string NombreValido(string nombre)
        {
            string limpio = new string((nombre ?? "").Where(c => "[]:*?/\\".IndexOf(c) < 0 && !char.IsControl(c)).ToArray()).Trim().Trim('\'');
            if (limpio.Length > 31)
            {
                limpio = limpio.Substring(0, 31).Trim();
            }

            return limpio.Length == 0 ? "Hoja" : limpio;
        }
    }

    /// <summary>
    /// Un libro de Excel (.xlsx) de verdad, con colores, anchos y lo que hace falta para
    /// imprimirlo, sin bibliotecas de mas: un .xlsx es un ZIP con unos XML (Office Open
    /// XML, SpreadsheetML), y aqui se escriben los minimos que Excel abre sin pedir
    /// reparacion. Pedido del 2026-11-08: la lista que descargan los docentes "que se vea
    /// mejor" (antes era un CSV, sin formato). Los textos van como texto (inlineStr):
    /// nunca como formula, aunque empiecen con "=", y una matricula con ceros a la
    /// izquierda los conserva.
    /// </summary>
    internal static class LibroExcel
    {
        private const string Principal = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string Relaciones = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly Color Gris = Color.FromArgb(212, 212, 212);
        private static readonly Color GrisDivisoria = Color.FromArgb(128, 128, 128);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>El libro entero, listo para guardarlo con File.WriteAllBytes.</summary>
        public static byte[] Crear(IList<HojaExcel> hojas, string titulo)
        {
            if (hojas == null || hojas.Count == 0)
            {
                throw new ArgumentException("Un libro necesita al menos una hoja.", "hojas");
            }

            Estilos estilos = new Estilos();
            List<string> xmlHojas = hojas.Select((h, i) => Hoja(h, estilos, i == 0)).ToList();
            using (MemoryStream memoria = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
                {
                    Escribir(zip, "[Content_Types].xml", TiposDeContenido(hojas.Count));
                    Escribir(zip, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                        + "<Relationship Id=\"rId1\" Type=\"" + Relaciones + "/officeDocument\" Target=\"xl/workbook.xml\"/>"
                        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>"
                        + "</Relationships>");
                    Escribir(zip, "docProps/core.xml", Propiedades(titulo));
                    Escribir(zip, "xl/workbook.xml", Libro(hojas));
                    Escribir(zip, "xl/_rels/workbook.xml.rels", RelacionesDelLibro(hojas.Count));
                    for (int i = 0; i < xmlHojas.Count; i++)
                    {
                        Escribir(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", xmlHojas[i]);
                    }

                    // Al final: las hojas ya registraron todos sus estilos.
                    Escribir(zip, "xl/styles.xml", estilos.Xml());
                }

                return memoria.ToArray();
            }
        }

        /// <summary>"A1", "AB12": la columna en letras y la fila.</summary>
        public static string Celda(int fila, int columna)
        {
            return Letras(columna) + fila.ToString(Inv);
        }

        public static string Letras(int columna)
        {
            string letras = "";
            for (int n = columna; n > 0; n = (n - 1) / 26)
            {
                letras = (char)('A' + (n - 1) % 26) + letras;
            }

            return letras;
        }

        private static void Escribir(ZipArchive zip, string nombre, string contenido)
        {
            ZipArchiveEntry entrada = zip.CreateEntry(nombre, CompressionLevel.Optimal);
            using (Stream flujo = entrada.Open())
            using (StreamWriter escritor = new StreamWriter(flujo, new UTF8Encoding(false)))
            {
                escritor.Write(contenido);
            }
        }

        private static string TiposDeContenido(int hojas)
        {
            StringBuilder xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            for (int i = 1; i <= hojas; i++)
            {
                xml.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i)
                    .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }

            return xml.Append("</Types>").ToString();
        }

        private static string Propiedades(string titulo)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" "
                + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" "
                + "xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + "<dc:title>" + Xml(titulo) + "</dc:title><dc:creator>DigLab</dc:creator>"
                + "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv) + "</dcterms:created>"
                + "</cp:coreProperties>";
        }

        private static string Libro(IList<HojaExcel> hojas)
        {
            StringBuilder xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<workbook xmlns=\"" + Principal + "\" xmlns:r=\"" + Relaciones + "\">"
                + "<bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>");
            for (int i = 0; i < hojas.Count; i++)
            {
                xml.Append("<sheet name=\"").Append(Xml(hojas[i].Nombre)).Append("\" sheetId=\"").Append(i + 1)
                    .Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
            }

            xml.Append("</sheets>");
            List<string> titulos = new List<string>();
            for (int i = 0; i < hojas.Count; i++)
            {
                if (hojas[i].RepetirAlImprimir > 0)
                {
                    string fila = hojas[i].RepetirAlImprimir.ToString(Inv);
                    titulos.Add("<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"" + i + "\">"
                        + Xml("'" + hojas[i].Nombre.Replace("'", "''") + "'!$" + fila + ":$" + fila) + "</definedName>");
                }
            }

            if (titulos.Count > 0)
            {
                xml.Append("<definedNames>").Append(string.Concat(titulos)).Append("</definedNames>");
            }

            return xml.Append("</workbook>").ToString();
        }

        private static string RelacionesDelLibro(int hojas)
        {
            StringBuilder xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= hojas; i++)
            {
                xml.Append("<Relationship Id=\"rId").Append(i).Append("\" Type=\"").Append(Relaciones)
                    .Append("/worksheet\" Target=\"worksheets/sheet").Append(i).Append(".xml\"/>");
            }

            xml.Append("<Relationship Id=\"rId").Append(hojas + 1).Append("\" Type=\"").Append(Relaciones)
                .Append("/styles\" Target=\"styles.xml\"/>");
            return xml.Append("</Relationships>").ToString();
        }

        private static string Hoja(HojaExcel hoja, Estilos estilos, bool primera)
        {
            int ultimaFila = hoja.Filas.Count == 0 ? 1 : hoja.Filas.Keys.Max();
            int ultimaColumna = Math.Max(1, Math.Max(hoja.Filas.Values.Where(r => r.Count > 0).Select(r => r.Keys.Max()).DefaultIfEmpty(1).Max(),
                hoja.Anchos.Count == 0 ? 1 : hoja.Anchos.Keys.Max()));

            StringBuilder xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<worksheet xmlns=\"").Append(Principal).Append("\" xmlns:r=\"").Append(Relaciones).Append("\">");
            if (hoja.Horizontal)
            {
                xml.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            }

            xml.Append("<dimension ref=\"A1:").Append(Celda(ultimaFila, ultimaColumna)).Append("\"/>");
            // Sin la cuadricula de Excel: la tabla lleva sus propias orillas, y fuera de ella no estorba.
            xml.Append("<sheetViews><sheetView showGridLines=\"0\"").Append(primera ? " tabSelected=\"1\"" : "").Append(" workbookViewId=\"0\">");
            Congelar(xml, hoja.FilasCongeladas, hoja.ColumnasCongeladas);
            xml.Append("</sheetView></sheetViews>");
            xml.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

            if (hoja.Anchos.Count > 0)
            {
                xml.Append("<cols>");
                foreach (KeyValuePair<int, double> ancho in hoja.Anchos)
                {
                    xml.Append("<col min=\"").Append(ancho.Key).Append("\" max=\"").Append(ancho.Key)
                        .Append("\" width=\"").Append(ancho.Value.ToString("0.##", Inv)).Append("\" customWidth=\"1\"/>");
                }

                xml.Append("</cols>");
            }

            xml.Append("<sheetData>");
            foreach (KeyValuePair<int, SortedDictionary<int, HojaExcel.Casilla>> renglon in hoja.Filas)
            {
                double alto;
                xml.Append("<row r=\"").Append(renglon.Key).Append('"');
                if (hoja.Altos.TryGetValue(renglon.Key, out alto))
                {
                    xml.Append(" ht=\"").Append(alto.ToString("0.##", Inv)).Append("\" customHeight=\"1\"");
                }

                xml.Append('>');
                foreach (KeyValuePair<int, HojaExcel.Casilla> casilla in renglon.Value)
                {
                    string referencia = Celda(renglon.Key, casilla.Key);
                    int estilo = estilos.Indice(casilla.Value.Estilo);
                    xml.Append("<c r=\"").Append(referencia).Append('"');
                    if (estilo > 0)
                    {
                        xml.Append(" s=\"").Append(estilo).Append('"');
                    }

                    if (casilla.Value.Numero.HasValue)
                    {
                        xml.Append("><v>").Append(casilla.Value.Numero.Value.ToString("R", Inv)).Append("</v></c>");
                    }
                    else if (!string.IsNullOrEmpty(casilla.Value.Texto))
                    {
                        xml.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(Xml(casilla.Value.Texto)).Append("</t></is></c>");
                    }
                    else
                    {
                        xml.Append("/>");
                    }
                }

                xml.Append("</row>");
            }

            xml.Append("</sheetData>");
            if (hoja.Combinadas.Count > 0)
            {
                xml.Append("<mergeCells count=\"").Append(hoja.Combinadas.Count).Append("\">");
                foreach (string rango in hoja.Combinadas)
                {
                    xml.Append("<mergeCell ref=\"").Append(rango).Append("\"/>");
                }

                xml.Append("</mergeCells>");
            }

            xml.Append("<pageMargins left=\"0.4\" right=\"0.4\" top=\"0.5\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
            // Carta (paperSize 1), horizontal y todo el ancho en una pagina; de alto, las que hagan falta.
            xml.Append("<pageSetup paperSize=\"1\" orientation=\"").Append(hoja.Horizontal ? "landscape" : "portrait").Append('"')
                .Append(hoja.Horizontal ? " fitToWidth=\"1\" fitToHeight=\"0\"" : "").Append("/>");
            // En el pie, un "&" del texto se escribe "&&" (si no, Excel lo toma por un codigo).
            xml.Append("<headerFooter><oddFooter>")
                .Append(Xml("&L&8" + (hoja.Pie ?? "").Replace("&", "&&") + "&R&8Página &P de &N"))
                .Append("</oddFooter></headerFooter>");
            return xml.Append("</worksheet>").ToString();
        }

        private static void Congelar(StringBuilder xml, int filas, int columnas)
        {
            if (filas <= 0 && columnas <= 0)
            {
                return;
            }

            string esquina = Celda(filas + 1, columnas + 1);
            if (filas > 0 && columnas > 0)
            {
                xml.Append("<pane xSplit=\"").Append(columnas).Append("\" ySplit=\"").Append(filas).Append("\" topLeftCell=\"").Append(esquina)
                    .Append("\" activePane=\"bottomRight\" state=\"frozen\"/>")
                    .Append("<selection pane=\"topRight\"/><selection pane=\"bottomLeft\"/>")
                    .Append("<selection pane=\"bottomRight\" activeCell=\"").Append(esquina).Append("\" sqref=\"").Append(esquina).Append("\"/>");
            }
            else if (filas > 0)
            {
                xml.Append("<pane ySplit=\"").Append(filas).Append("\" topLeftCell=\"").Append(esquina)
                    .Append("\" activePane=\"bottomLeft\" state=\"frozen\"/>")
                    .Append("<selection pane=\"bottomLeft\" activeCell=\"").Append(esquina).Append("\" sqref=\"").Append(esquina).Append("\"/>");
            }
            else
            {
                xml.Append("<pane xSplit=\"").Append(columnas).Append("\" topLeftCell=\"").Append(esquina)
                    .Append("\" activePane=\"topRight\" state=\"frozen\"/>")
                    .Append("<selection pane=\"topRight\" activeCell=\"").Append(esquina).Append("\" sqref=\"").Append(esquina).Append("\"/>");
            }
        }

        /// <summary>
        /// Texto para un XML: sin los caracteres de control que el XML no admite (los
        /// nombres los escribe quien crea su cuenta) y con &amp;, &lt;, &gt; y comillas escapados.
        /// </summary>
        internal static string Xml(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return "";
            }

            StringBuilder limpio = new StringBuilder(texto.Length);
            for (int i = 0; i < texto.Length; i++)
            {
                char c = texto[i];
                if (char.IsHighSurrogate(c) && i + 1 < texto.Length && char.IsLowSurrogate(texto[i + 1]))
                {
                    limpio.Append(c).Append(texto[++i]);
                }
                else if ((c >= 0x20 && c < 0xD800) || (c > 0xDFFF && c < 0xFFFE) || c == '\t' || c == '\n' || c == '\r')
                {
                    limpio.Append(c);
                }
            }

            return SecurityElement.Escape(limpio.ToString());
        }

        private static string Argb(Color color)
        {
            return "FF" + color.R.ToString("X2", Inv) + color.G.ToString("X2", Inv) + color.B.ToString("X2", Inv);
        }

        /// <summary>
        /// styles.xml: letras, rellenos, orillas y formatos, cada combinacion una sola vez.
        /// Los primeros de cada lista son los que Excel espera (el relleno 1 es el gris
        /// "gray125", obligatorio aunque nadie lo use).
        /// </summary>
        private sealed class Estilos
        {
            private readonly List<string> letras = new List<string> { "<font><sz val=\"11\"/><color rgb=\"FF000000\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" };
            private readonly List<string> rellenos = new List<string> { "<fill><patternFill patternType=\"none\"/></fill>", "<fill><patternFill patternType=\"gray125\"/></fill>" };
            private readonly List<string> orillas = new List<string> { "<border><left/><right/><top/><bottom/><diagonal/></border>" };
            private readonly List<string> formatos = new List<string>();
            private readonly List<string> casillas = new List<string> { "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" };
            private readonly Dictionary<string, int> indices = new Dictionary<string, int>();

            public int Indice(EstiloExcel estilo)
            {
                if (estilo == null)
                {
                    return 0;
                }

                int letra = Agregar(letras, "<font>" + (estilo.Negrita ? "<b/>" : "") + "<sz val=\"" + estilo.Tamano.ToString("0.##", Inv) + "\"/>"
                    + "<color rgb=\"" + Argb(estilo.Letra) + "\"/><name val=\"Calibri\"/><family val=\"2\"/></font>");
                int relleno = estilo.Fondo.IsEmpty ? 0 : Agregar(rellenos, "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + Argb(estilo.Fondo)
                    + "\"/><bgColor indexed=\"64\"/></patternFill></fill>");
                int orilla = estilo.Cuadricula || estilo.DivisoriaDerecha || estilo.LineaArriba ? Agregar(orillas, Orilla(estilo)) : 0;
                int formato = NumeroDeFormato(estilo.Formato);
                string alineacion = "";
                if (estilo.Horizontal != null || estilo.Ajustar || estilo.Rotacion != 0)
                {
                    alineacion = "<alignment" + (estilo.Horizontal != null ? " horizontal=\"" + estilo.Horizontal + "\"" : "") + " vertical=\"center\""
                        + (estilo.Ajustar ? " wrapText=\"1\"" : "") + (estilo.Rotacion != 0 ? " textRotation=\"" + estilo.Rotacion + "\"" : "") + "/>";
                }
                else
                {
                    alineacion = "<alignment vertical=\"center\"/>";
                }

                string xf = "<xf numFmtId=\"" + formato + "\" fontId=\"" + letra + "\" fillId=\"" + relleno + "\" borderId=\"" + orilla + "\" xfId=\"0\""
                    + (formato != 0 ? " applyNumberFormat=\"1\"" : "") + " applyFont=\"1\"" + (relleno != 0 ? " applyFill=\"1\"" : "")
                    + (orilla != 0 ? " applyBorder=\"1\"" : "") + " applyAlignment=\"1\">" + alineacion + "</xf>";
                int indice;
                if (!indices.TryGetValue(xf, out indice))
                {
                    indice = casillas.Count;
                    casillas.Add(xf);
                    indices[xf] = indice;
                }

                return indice;
            }

            private static string Orilla(EstiloExcel estilo)
            {
                string fina = estilo.Cuadricula ? "style=\"thin\"><color rgb=\"" + Argb(Gris) + "\"/>" : null;
                string gruesa = "style=\"medium\"><color rgb=\"" + Argb(GrisDivisoria) + "\"/>";
                Func<string, string, string> lado = (nombre, tipo) => tipo == null ? "<" + nombre + "/>" : "<" + nombre + " " + tipo + "</" + nombre + ">";
                return "<border>" + lado("left", fina) + lado("right", estilo.DivisoriaDerecha ? gruesa : fina)
                    + lado("top", estilo.LineaArriba ? gruesa : fina) + lado("bottom", fina) + "<diagonal/></border>";
            }

            private int NumeroDeFormato(string formato)
            {
                if (string.IsNullOrEmpty(formato))
                {
                    return 0;
                }

                // Los de Excel: no hace falta declararlos.
                if (formato == "0%")
                {
                    return 9;
                }

                if (formato == "0")
                {
                    return 1;
                }

                int i = formatos.IndexOf(formato);
                if (i < 0)
                {
                    formatos.Add(formato);
                    i = formatos.Count - 1;
                }

                return 164 + i;
            }

            private static int Agregar(List<string> lista, string xml)
            {
                int i = lista.IndexOf(xml);
                if (i >= 0)
                {
                    return i;
                }

                lista.Add(xml);
                return lista.Count - 1;
            }

            public string Xml()
            {
                StringBuilder xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                xml.Append("<styleSheet xmlns=\"").Append(Principal).Append("\">");
                if (formatos.Count > 0)
                {
                    xml.Append("<numFmts count=\"").Append(formatos.Count).Append("\">");
                    for (int i = 0; i < formatos.Count; i++)
                    {
                        xml.Append("<numFmt numFmtId=\"").Append(164 + i).Append("\" formatCode=\"").Append(LibroExcel.Xml(formatos[i])).Append("\"/>");
                    }

                    xml.Append("</numFmts>");
                }

                xml.Append("<fonts count=\"").Append(letras.Count).Append("\">").Append(string.Concat(letras)).Append("</fonts>");
                xml.Append("<fills count=\"").Append(rellenos.Count).Append("\">").Append(string.Concat(rellenos)).Append("</fills>");
                xml.Append("<borders count=\"").Append(orillas.Count).Append("\">").Append(string.Concat(orillas)).Append("</borders>");
                xml.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
                xml.Append("<cellXfs count=\"").Append(casillas.Count).Append("\">").Append(string.Concat(casillas)).Append("</cellXfs>");
                xml.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
                return xml.Append("</styleSheet>").ToString();
            }
        }
    }
}
