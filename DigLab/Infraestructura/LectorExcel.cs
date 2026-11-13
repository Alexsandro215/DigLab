using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace DigLab.Infraestructura
{
    /// <summary>Una hoja leida de un .xlsx (LectorExcel): lo que dice cada casilla y las combinadas.</summary>
    internal sealed class HojaLeida
    {
        /// <summary>Un rango de casillas combinadas (filas y columnas desde 1).</summary>
        internal sealed class Rango
        {
            public int Fila1;
            public int Columna1;
            public int Fila2;
            public int Columna2;

            public bool Contiene(int fila, int columna)
            {
                return fila >= Fila1 && fila <= Fila2 && columna >= Columna1 && columna <= Columna2;
            }
        }

        private readonly Dictionary<long, string> textos = new Dictionary<long, string>();
        private readonly Dictionary<long, double> numeros = new Dictionary<long, double>();

        public string Nombre = "";
        /// <summary>Oculta en Excel (state="hidden" o "veryHidden").</summary>
        public bool Oculta;
        public int UltimaFila;
        public int UltimaColumna;
        public readonly List<Rango> Combinadas = new List<Rango>();

        private static long Clave(int fila, int columna)
        {
            return (long)fila * 20000 + columna;
        }

        internal void Poner(int fila, int columna, string texto, double? numero)
        {
            if (fila < 1 || columna < 1)
            {
                return;
            }

            if (!string.IsNullOrEmpty(texto))
            {
                textos[Clave(fila, columna)] = texto;
            }

            if (numero.HasValue)
            {
                numeros[Clave(fila, columna)] = numero.Value;
            }

            if (!string.IsNullOrEmpty(texto) || numero.HasValue)
            {
                UltimaFila = Math.Max(UltimaFila, fila);
                UltimaColumna = Math.Max(UltimaColumna, columna);
            }
        }

        /// <summary>Lo que dice la casilla tal cual ("" si esta vacia); un numero, como Excel lo escribe sin formato.</summary>
        public string Texto(int fila, int columna)
        {
            string texto;
            return textos.TryGetValue(Clave(fila, columna), out texto) ? texto : "";
        }

        /// <summary>El numero de la casilla si es un numero (una hora de Excel es la fraccion del dia).</summary>
        public double? Numero(int fila, int columna)
        {
            double numero;
            return numeros.TryGetValue(Clave(fila, columna), out numero) ? numero : (double?)null;
        }

        /// <summary>La combinada en la que esta esa casilla, o null.</summary>
        public Rango CombinadaDe(int fila, int columna)
        {
            return Combinadas.FirstOrDefault(r => r.Contiene(fila, columna));
        }

        /// <summary>Lo que se ve en la casilla: si esta dentro de una combinada, lo de la primera de ella.</summary>
        public string TextoVisible(int fila, int columna)
        {
            Rango rango = CombinadaDe(fila, columna);
            return rango == null ? Texto(fila, columna) : Texto(rango.Fila1, rango.Columna1);
        }
    }

    /// <summary>
    /// Lee un libro de Excel (.xlsx) sin bibliotecas de mas: es un ZIP con XML (Office
    /// Open XML). Solo lo que hace falta para leer un horario en cuadricula (pedido del
    /// 2026-11-12): el texto de cada casilla (compartido, en linea o el resultado guardado
    /// de una formula), los numeros y las combinadas. Ni formatos ni formulas: lo que
    /// Excel guardo como resultado. El .xls viejo (de antes de 2007) no se lee.
    /// </summary>
    internal static class LectorExcel
    {
        // Un horario son unas decenas de KB; con esto ningun archivo raro se come la memoria.
        private const long MaximoPorParte = 40L * 1024 * 1024;
        private const int MaximoFilas = 5000;
        private const int MaximoColumnas = 200;

        /// <summary>El archivo no es un .xlsx que se pueda leer (el mensaje dice por que y que hacer).</summary>
        internal sealed class ArchivoNoValido : Exception
        {
            public ArchivoNoValido(string mensaje) : base(mensaje)
            {
            }
        }

        /// <summary>Lee el archivo aunque este abierto en Excel (se abre compartido, solo para leer).</summary>
        /// <exception cref="ArchivoNoValido">No es un libro de Excel que se pueda leer.</exception>
        /// <exception cref="IOException">No se pudo abrir el archivo.</exception>
        public static List<HojaLeida> Leer(string ruta)
        {
            byte[] datos;
            using (FileStream flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (flujo.Length > MaximoPorParte)
                {
                    throw new ArchivoNoValido("El archivo es demasiado grande para ser un horario (" + (flujo.Length / (1024 * 1024)) + " MB).");
                }

                datos = new byte[flujo.Length];
                int leidos = 0;
                while (leidos < datos.Length)
                {
                    int n = flujo.Read(datos, leidos, datos.Length - leidos);
                    if (n <= 0)
                    {
                        break;
                    }

                    leidos += n;
                }
            }

            return Leer(datos);
        }

        /// <exception cref="ArchivoNoValido">No es un libro de Excel que se pueda leer.</exception>
        public static List<HojaLeida> Leer(byte[] datos)
        {
            if (datos == null || datos.Length < 4)
            {
                throw new ArchivoNoValido("El archivo está vacío.");
            }

            // Un .xls (de Excel 97-2003) empieza con la firma de los documentos compuestos de OLE.
            if (datos[0] == 0xD0 && datos[1] == 0xCF && datos[2] == 0x11 && datos[3] == 0xE0)
            {
                throw new ArchivoNoValido("Es un Excel de formato antiguo (.xls). Ábrelo en Excel y guárdalo como «Libro de Excel (.xlsx)».");
            }

            if (datos[0] != (byte)'P' || datos[1] != (byte)'K')
            {
                throw new ArchivoNoValido("No es un libro de Excel (.xlsx). Si es un .csv o de otro programa, ábrelo en Excel y guárdalo como «Libro de Excel (.xlsx)».");
            }

            try
            {
                using (MemoryStream memoria = new MemoryStream(datos, false))
                using (ZipArchive zip = new ZipArchive(memoria, ZipArchiveMode.Read))
                {
                    return LeerZip(zip);
                }
            }
            catch (ArchivoNoValido)
            {
                throw;
            }
            catch (InvalidDataException ex)
            {
                throw new ArchivoNoValido("El archivo está dañado o no es un libro de Excel (" + ex.Message + ").");
            }
            catch (XmlException ex)
            {
                throw new ArchivoNoValido("El libro de Excel tiene una parte que no se entiende (" + ex.Message + "). Ábrelo en Excel y vuelve a guardarlo.");
            }
        }

        private static List<HojaLeida> LeerZip(ZipArchive zip)
        {
            XmlDocument libro = Xml(zip, "xl/workbook.xml");
            if (libro == null)
            {
                throw new ArchivoNoValido("No es un libro de Excel (.xlsx): le falta la lista de sus hojas. Si es de otro programa, ábrelo en Excel y guárdalo como «Libro de Excel (.xlsx)».");
            }

            Dictionary<string, string> destinos = Relaciones(Xml(zip, "xl/_rels/workbook.xml.rels"));
            List<string> compartidos = Compartidos(Xml(zip, "xl/sharedStrings.xml"));
            List<HojaLeida> hojas = new List<HojaLeida>();
            foreach (XmlElement hoja in Elementos(libro.DocumentElement, "sheet"))
            {
                string id = Atributo(hoja, "id");
                string destino;
                if (id == null || !destinos.TryGetValue(id, out destino))
                {
                    continue;
                }

                string estado = Atributo(hoja, "state") ?? "";
                HojaLeida leida = new HojaLeida
                {
                    Nombre = Atributo(hoja, "name") ?? "",
                    Oculta = estado == "hidden" || estado == "veryHidden"
                };
                XmlDocument xml = Xml(zip, Ruta(destino));
                if (xml == null)
                {
                    // Una hoja de graficas (chartsheet) u otra cosa: sin casillas.
                    continue;
                }

                LlenarHoja(leida, xml, compartidos);
                hojas.Add(leida);
            }

            return hojas;
        }

        /// <summary>"worksheets/sheet1.xml" o "/xl/worksheets/sheet1.xml": la ruta dentro del ZIP.</summary>
        private static string Ruta(string destino)
        {
            destino = destino.Replace('\\', '/');
            return destino.StartsWith("/", StringComparison.Ordinal) ? destino.Substring(1) : "xl/" + destino;
        }

        private static void LlenarHoja(HojaLeida hoja, XmlDocument xml, List<string> compartidos)
        {
            int filaSiguiente = 1;
            foreach (XmlElement renglon in Elementos(xml.DocumentElement, "row"))
            {
                int fila;
                string r = Atributo(renglon, "r");
                if (r == null || !int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out fila))
                {
                    fila = filaSiguiente;
                }

                filaSiguiente = fila + 1;
                if (fila > MaximoFilas)
                {
                    break;
                }

                int columnaSiguiente = 1;
                foreach (XmlNode nodo in renglon.ChildNodes)
                {
                    XmlElement casilla = nodo as XmlElement;
                    if (casilla == null || casilla.LocalName != "c")
                    {
                        continue;
                    }

                    int f = fila;
                    int columna = columnaSiguiente;
                    string referencia = Atributo(casilla, "r");
                    if (referencia != null)
                    {
                        int ff;
                        int cc;
                        if (Referencia(referencia, out ff, out cc))
                        {
                            f = ff;
                            columna = cc;
                        }
                    }

                    columnaSiguiente = columna + 1;
                    if (columna > MaximoColumnas)
                    {
                        continue;
                    }

                    string tipo = Atributo(casilla, "t") ?? "n";
                    string valor = TextoHijo(casilla, "v");
                    string texto = null;
                    double? numero = null;
                    switch (tipo)
                    {
                        case "s":
                            int indice;
                            if (valor != null && int.TryParse(valor.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out indice)
                                && indice >= 0 && indice < compartidos.Count)
                            {
                                texto = compartidos[indice];
                            }

                            break;
                        case "inlineStr":
                            XmlElement en = Hijo(casilla, "is");
                            texto = en == null ? null : TextoDeCadena(en);
                            break;
                        case "str":
                        case "e":
                            texto = valor;
                            break;
                        case "b":
                            texto = valor == "1" ? "VERDADERO" : valor == "0" ? "FALSO" : valor;
                            break;
                        default:
                            double d;
                            if (valor != null && double.TryParse(valor.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                            {
                                numero = d;
                                texto = d.ToString("R", CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                texto = valor;
                            }

                            break;
                    }

                    hoja.Poner(f, columna, texto, numero);
                }
            }

            foreach (XmlElement combinada in Elementos(xml.DocumentElement, "mergeCell"))
            {
                string referencia = Atributo(combinada, "ref") ?? "";
                string[] partes = referencia.Split(':');
                int f1;
                int c1;
                int f2;
                int c2;
                if (partes.Length == 2 && Referencia(partes[0], out f1, out c1) && Referencia(partes[1], out f2, out c2))
                {
                    hoja.Combinadas.Add(new HojaLeida.Rango
                    {
                        Fila1 = Math.Min(f1, f2),
                        Columna1 = Math.Min(c1, c2),
                        Fila2 = Math.Max(f1, f2),
                        Columna2 = Math.Max(c1, c2)
                    });
                }
            }
        }

        /// <summary>"AB12" a fila 12, columna 28. Sin numero (o con algo mas), false.</summary>
        internal static bool Referencia(string texto, out int fila, out int columna)
        {
            fila = 0;
            columna = 0;
            texto = (texto ?? "").Replace("$", "").Trim();
            int i = 0;
            while (i < texto.Length && char.IsLetter(texto[i]))
            {
                char c = char.ToUpperInvariant(texto[i]);
                if (c < 'A' || c > 'Z' || columna > 100000)
                {
                    return false;
                }

                columna = columna * 26 + (c - 'A' + 1);
                i++;
            }

            if (i == 0 || i == texto.Length)
            {
                return false;
            }

            return int.TryParse(texto.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out fila) && fila > 0;
        }

        private static List<string> Compartidos(XmlDocument xml)
        {
            List<string> lista = new List<string>();
            if (xml == null)
            {
                return lista;
            }

            foreach (XmlElement si in Elementos(xml.DocumentElement, "si"))
            {
                lista.Add(TextoDeCadena(si));
            }

            return lista;
        }

        /// <summary>
        /// El texto de un &lt;si&gt; o &lt;is&gt;: su &lt;t&gt;, o los &lt;t&gt; de sus
        /// tramos con formato (&lt;r&gt;), sin la guia fonetica (&lt;rPh&gt;).
        /// </summary>
        private static string TextoDeCadena(XmlElement cadena)
        {
            StringBuilder texto = new StringBuilder();
            foreach (XmlNode nodo in cadena.ChildNodes)
            {
                XmlElement e = nodo as XmlElement;
                if (e == null)
                {
                    continue;
                }

                if (e.LocalName == "t")
                {
                    texto.Append(e.InnerText);
                }
                else if (e.LocalName == "r")
                {
                    XmlElement t = Hijo(e, "t");
                    if (t != null)
                    {
                        texto.Append(t.InnerText);
                    }
                }
            }

            // Excel escribe "_x000D_" por un retorno de carro (y _xHHHH_ por otros caracteres).
            return Escapes(texto.ToString());
        }

        private static string Escapes(string texto)
        {
            if (texto.IndexOf("_x", StringComparison.Ordinal) < 0)
            {
                return texto;
            }

            StringBuilder limpio = new StringBuilder(texto.Length);
            for (int i = 0; i < texto.Length; i++)
            {
                int codigo;
                if (texto[i] == '_' && i + 6 < texto.Length && texto[i + 1] == 'x' && texto[i + 6] == '_'
                    && int.TryParse(texto.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codigo))
                {
                    limpio.Append((char)codigo);
                    i += 6;
                }
                else
                {
                    limpio.Append(texto[i]);
                }
            }

            return limpio.ToString();
        }

        private static Dictionary<string, string> Relaciones(XmlDocument xml)
        {
            Dictionary<string, string> destinos = new Dictionary<string, string>(StringComparer.Ordinal);
            if (xml == null)
            {
                return destinos;
            }

            foreach (XmlElement relacion in Elementos(xml.DocumentElement, "Relationship"))
            {
                string id = Atributo(relacion, "Id");
                string destino = Atributo(relacion, "Target");
                if (id != null && destino != null && !(Atributo(relacion, "TargetMode") ?? "").Equals("External", StringComparison.OrdinalIgnoreCase))
                {
                    destinos[id] = destino;
                }
            }

            return destinos;
        }

        /// <summary>Una parte del ZIP como XML, o null si no esta. Sin DTD ni nada externo.</summary>
        private static XmlDocument Xml(ZipArchive zip, string nombre)
        {
            ZipArchiveEntry entrada = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), nombre, StringComparison.OrdinalIgnoreCase));
            if (entrada == null)
            {
                return null;
            }

            if (entrada.Length > MaximoPorParte)
            {
                throw new ArchivoNoValido("El libro de Excel es demasiado grande para ser un horario.");
            }

            XmlReaderSettings ajustes = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                MaxCharactersInDocument = MaximoPorParte
            };
            using (Stream flujo = entrada.Open())
            using (XmlReader lector = XmlReader.Create(flujo, ajustes))
            {
                XmlDocument documento = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
                documento.Load(lector);
                return documento;
            }
        }

        /// <summary>Los descendientes con ese nombre, sin mirar el espacio de nombres (el "Strict" de Excel usa otro).</summary>
        private static IEnumerable<XmlElement> Elementos(XmlElement raiz, string nombre)
        {
            if (raiz == null)
            {
                yield break;
            }

            foreach (XmlNode nodo in raiz.GetElementsByTagName("*"))
            {
                XmlElement e = nodo as XmlElement;
                if (e != null && e.LocalName == nombre)
                {
                    yield return e;
                }
            }
        }

        private static XmlElement Hijo(XmlElement padre, string nombre)
        {
            foreach (XmlNode nodo in padre.ChildNodes)
            {
                XmlElement e = nodo as XmlElement;
                if (e != null && e.LocalName == nombre)
                {
                    return e;
                }
            }

            return null;
        }

        private static string TextoHijo(XmlElement padre, string nombre)
        {
            XmlElement hijo = Hijo(padre, nombre);
            return hijo == null ? null : hijo.InnerText;
        }

        /// <summary>Un atributo por su nombre local (r:id es "id"), o null.</summary>
        private static string Atributo(XmlElement e, string nombre)
        {
            foreach (XmlAttribute a in e.Attributes)
            {
                if (a.LocalName == nombre)
                {
                    return a.Value;
                }
            }

            return null;
        }
    }
}
