using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;

namespace DigBit
{
    /// <summary>
    /// La imagen de una clase: abrirla (de un archivo o del portapapeles),
    /// enderezarla, recortar el cuadrado que se ve en el circulo y codificarla
    /// en pequeno para la base (Lado x Lado, como mucho
    /// PersonalizacionClasesDatos.BytesImagen).
    ///
    /// DigBit.exe corre en 32 bits: antes de decodificar se miran los bytes del
    /// archivo y las medidas de la cabecera (Image.FromStream sin validar los
    /// datos solo lee la cabecera); se decodifica una vez, se reduce enseguida a
    /// una copia de trabajo de LadoTrabajo como mucho y el original se libera.
    /// Nada de esto se usa desde otro hilo: lo llama el dialogo.
    /// </summary>
    internal static class ImagenDeClase
    {
        public const int Lado = 128;
        /// <summary>Si a Lado no cabe en los bytes de la base, se prueba a este.</summary>
        public const int LadoReducido = 96;
        /// <summary>La copia de trabajo, para encuadrar y acercar sin guardar el original.</summary>
        public const int LadoTrabajo = 1024;
        public const long BytesArchivoMaximo = 20L * 1024 * 1024;
        public const int MegapixelesMaximos = 30;
        /// <summary>Un PNG de hasta esto se usa tal cual (salvo una foto, ver Recortar); mas pesado y sin transparencia, JPEG.</summary>
        public const int BytesPngPreferido = 48000;
        private const long CalidadJpeg = 85;
        private const int EtiquetaOrientacion = 0x0112;

        public const string NoSeAbre = "No se pudo abrir esa imagen. Usa un archivo PNG, JPG, BMP o GIF.";
        public const string NoCabe = "No se pudo reducir la imagen lo suficiente. Prueba con otra.";
        public const string SinImagenEnPortapapeles = "En el portapapeles no hay una imagen. Cópiala (por ejemplo, con clic derecho > Copiar imagen en el navegador) y vuelve a pulsar Pegar imagen.";
        public const string DemasiadoGrandeParaAbrir = "La imagen es demasiado grande para abrirla en este equipo. Prueba con una más pequeña.";

        /// <summary>
        /// La imagen orientada (EXIF 0x0112) y reducida a LadoTrabajo px como
        /// mucho, en 32 bits con transparencia; null y el motivo (para la linea de
        /// error) si no se pudo. Un GIF animado da su primer cuadro.
        /// </summary>
        public static Bitmap Abrir(string archivo, out string motivo)
        {
            motivo = null;
            // Solo para el log, que va en ASCII ("Programación.png" -> "Programacion.png").
            // Dentro del try: una ruta con caracteres no validos ("<") lanza aqui.
            string nombre = "?";
            try
            {
                nombre = PersonalizacionClasesDatos.Ascii(Path.GetFileName(archivo ?? ""));
                FileInfo info = new FileInfo(archivo);
                if (!info.Exists)
                {
                    motivo = "No se pudo leer el archivo: ya no está en esa carpeta.";
                    return null;
                }

                if (info.Length > BytesArchivoMaximo)
                {
                    motivo = "La imagen pesa más de 20 MB. Usa una copia más chica.";
                    Log.Aviso("Imagen de clase: " + nombre + " pesa " + info.Length + " bytes; no se abre.");
                    return null;
                }

                using (FileStream flujo = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return DesdeFlujo(flujo, nombre, out motivo);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                // La causa y que hacer; el texto de Windows, al registro.
                Log.Aviso("Imagen de clase: no se pudo leer " + nombre + ": " + ex.Message);
                motivo = ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                    ? "No se pudo leer el archivo: Windows no da permiso para abrirlo. Cópialo a Documentos o al Escritorio y elígelo desde ahí."
                    : ex is IOException
                    ? "No se pudo leer el archivo: está abierto en otro programa o ya no se puede leer. Ciérralo e inténtalo de nuevo, o elige otra imagen."
                    : "No se pudo leer el archivo: su nombre o su carpeta no son válidos. Cópialo a Documentos y elígelo desde ahí.";
                return null;
            }
        }

        /// <summary>
        /// Del portapapeles: el formato "PNG" si viene (conserva la
        /// transparencia; lo ponen los navegadores); si no, GetImage(). Necesita
        /// el hilo de la pantalla (STA). Null y el motivo si no hay imagen.
        /// </summary>
        public static Bitmap DelPortapapeles(out string motivo)
        {
            motivo = null;
            try
            {
                IDataObject datos = Clipboard.GetDataObject();
                if (datos != null && datos.GetDataPresent("PNG"))
                {
                    using (Stream png = datos.GetData("PNG") as Stream)
                    {
                        if (png != null)
                        {
                            if (png.CanSeek && png.Length > BytesArchivoMaximo)
                            {
                                motivo = "La imagen pesa más de 20 MB. Usa una copia más chica.";
                                return null;
                            }

                            Bitmap desdePng = DesdeFlujo(png, "portapapeles (PNG)", out motivo);
                            if (desdePng != null || !Clipboard.ContainsImage())
                            {
                                return desdePng;
                            }
                        }
                    }
                }

                if (!Clipboard.ContainsImage())
                {
                    motivo = SinImagenEnPortapapeles;
                    return null;
                }

                using (Image pegada = Clipboard.GetImage())
                {
                    if (pegada == null)
                    {
                        motivo = SinImagenEnPortapapeles;
                        return null;
                    }

                    motivo = null;
                    return Reducir(pegada, 1);
                }
            }
            catch (OutOfMemoryException)
            {
                motivo = DemasiadoGrandeParaAbrir;
                return null;
            }
            catch (Exception ex) when (ex is ExternalException || ex is System.Threading.ThreadStateException || ex is ArgumentException)
            {
                // La causa y que hacer; el texto de Windows, al registro. Ocupado (ExternalException)
                // o lo copiado no se deja leer como imagen (lo demas).
                Log.Aviso("Imagen de clase: no se pudo leer el portapapeles: " + ex.Message);
                motivo = ex is ExternalException
                    ? "No se pudo leer el portapapeles: otro programa lo estaba usando. Espera un momento y vuelve a pulsar Pegar imagen."
                    : "No se pudo leer la imagen que copiaste. Cópiala otra vez (o copia otra) y vuelve a pulsar Pegar imagen.";
                return null;
            }
        }

        /// <summary>
        /// La guardada en la base (para volver a encuadrarla o pintarla), como
        /// Bitmap propio de 32 bits; null si no es un PNG o JPEG de hasta 256 px
        /// por lado que se pueda leer. Nunca lanza.
        /// </summary>
        public static Bitmap DesdeBytes(byte[] datos)
        {
            if (!PersonalizacionClasesDatos.ImagenAceptable(datos))
            {
                return null;
            }

            try
            {
                using (MemoryStream flujo = new MemoryStream(datos, false))
                using (Image imagen = Image.FromStream(flujo, false, false))
                {
                    if (imagen.Width <= 0 || imagen.Height <= 0 || imagen.Width > 256 || imagen.Height > 256)
                    {
                        return null;
                    }

                    return Copiar(imagen, imagen.Width, imagen.Height);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OutOfMemoryException || ex is ExternalException)
            {
                return null;
            }
        }

        /// <summary>
        /// El cuadrado mas grande, centrado: lo que se ve al abrir una imagen
        /// (acercamiento de 100 %, "cubrir" el circulo sin huecos).
        /// </summary>
        public static RectangleF CuadradoInicial(Size tamano)
        {
            float lado = Math.Min(tamano.Width, tamano.Height);
            return new RectangleF((tamano.Width - lado) / 2f, (tamano.Height - lado) / 2f, lado, lado);
        }

        /// <summary>
        /// El cuadrado que se ve en el circulo, a Lado x Lado y codificado: PNG si
        /// pesa BytesPngPreferido o menos; si no y no tiene transparencia, JPEG de
        /// calidad 85 (tambien si el JPEG pesa la mitad o menos que el PNG: es una
        /// foto); si aun asi pasa de BytesImagen, lo mismo a LadoReducido. Con
        /// transparencia se queda en PNG (el color de la clase se ve detras).
        /// Null y el motivo si no cabe.
        /// </summary>
        public static byte[] Recortar(Bitmap fuente, RectangleF cuadrado, out string motivo)
        {
            motivo = null;
            if (fuente == null)
            {
                motivo = NoSeAbre;
                return null;
            }

            try
            {
                foreach (int lado in new[] { Lado, LadoReducido })
                {
                    using (Bitmap recorte = Recorte(fuente, cuadrado, lado))
                    {
                        bool opaca = !TieneTransparencia(recorte);
                        byte[] png = Codificar(recorte, ImageFormat.Png);
                        byte[] elegida = png;
                        if (opaca)
                        {
                            // Una foto a 128 px pesa unos 40 KB en PNG y unos 7 en JPEG: si el
                            // JPEG pesa la mitad o menos, es una foto y va en JPEG. Un logo o un
                            // dibujo plano se queda en PNG (nitido y casi igual de ligero).
                            byte[] jpeg = Codificar(recorte, ImageFormat.Jpeg);
                            if ((png.Length > BytesPngPreferido && jpeg.Length < png.Length) || jpeg.Length * 2 <= png.Length)
                            {
                                elegida = jpeg;
                            }
                        }

                        if (elegida.Length <= PersonalizacionClasesDatos.BytesImagen)
                        {
                            return elegida;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ExternalException || ex is OutOfMemoryException || ex is ArgumentException)
            {
                Log.Aviso("Imagen de clase: no se pudo codificar: " + ex.Message);
            }

            motivo = NoCabe;
            return null;
        }

        /// <summary>True si algun pixel no es del todo opaco.</summary>
        public static bool TieneTransparencia(Bitmap imagen)
        {
            if (imagen == null || !Image.IsAlphaPixelFormat(imagen.PixelFormat))
            {
                return false;
            }

            Rectangle todo = new Rectangle(0, 0, imagen.Width, imagen.Height);
            BitmapData datos = imagen.LockBits(todo, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int ancho = imagen.Width * 4;
                byte[] fila = new byte[ancho];
                for (int y = 0; y < imagen.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(datos.Scan0, y * datos.Stride), fila, 0, ancho);
                    for (int x = 3; x < ancho; x += 4)
                    {
                        if (fila[x] != 255)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            finally
            {
                imagen.UnlockBits(datos);
            }
        }

        /// <summary>
        /// Comprueba la cabecera (formato y medidas) antes de decodificar,
        /// decodifica una vez, reduce y endereza. El flujo puede cerrarse al volver.
        /// </summary>
        private static Bitmap DesdeFlujo(Stream flujo, string nombre, out string motivo)
        {
            motivo = null;
            Image cabecera;
            try
            {
                // Sin validar los datos: solo lee la cabecera.
                cabecera = Image.FromStream(flujo, false, false);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OutOfMemoryException || ex is ExternalException)
            {
                Log.Aviso("Imagen de clase: " + nombre + " no es una imagen que se pueda abrir (" + ex.GetType().Name + ").");
                motivo = NoSeAbre;
                return null;
            }

            using (cabecera)
            {
                Guid formato = cabecera.RawFormat.Guid;
                bool admitido = formato == ImageFormat.Png.Guid || formato == ImageFormat.Jpeg.Guid || formato == ImageFormat.Bmp.Guid
                    || formato == ImageFormat.Gif.Guid || formato == ImageFormat.MemoryBmp.Guid;
                if (!admitido || cabecera.Width <= 0 || cabecera.Height <= 0)
                {
                    Log.Aviso("Imagen de clase: " + nombre + " tiene un formato que no se admite.");
                    motivo = NoSeAbre;
                    return null;
                }

                long pixeles = (long)cabecera.Width * cabecera.Height;
                if (pixeles > MegapixelesMaximos * 1000000L)
                {
                    Log.Aviso("Imagen de clase: " + nombre + " mide " + cabecera.Width + " x " + cabecera.Height + "; no se abre.");
                    motivo = "La imagen es demasiado grande (" + cabecera.Width + " x " + cabecera.Height
                        + " píxeles). Usa una de menos de " + MegapixelesMaximos + " megapíxeles.";
                    return null;
                }

                int orientacion = Orientacion(cabecera);
                try
                {
                    Bitmap reducida = Reducir(cabecera, orientacion);
                    return reducida;
                }
                catch (OutOfMemoryException)
                {
                    // GDI+ tambien la lanza con datos danados: con pocas medidas, es eso.
                    Log.Aviso("Imagen de clase: sin memoria al abrir " + nombre + " (" + cabecera.Width + " x " + cabecera.Height + ").");
                    motivo = pixeles > 4000000L ? DemasiadoGrandeParaAbrir : NoSeAbre;
                    return null;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is ExternalException)
                {
                    Log.Aviso("Imagen de clase: " + nombre + " esta danada (" + ex.GetType().Name + ").");
                    motivo = NoSeAbre;
                    return null;
                }
            }
        }

        /// <summary>La etiqueta EXIF de orientacion (1 a 8), o 1 si no la tiene.</summary>
        private static int Orientacion(Image imagen)
        {
            try
            {
                if (!imagen.PropertyIdList.Contains(EtiquetaOrientacion))
                {
                    return 1;
                }

                PropertyItem etiqueta = imagen.GetPropertyItem(EtiquetaOrientacion);
                int valor = etiqueta.Value != null && etiqueta.Value.Length > 0 ? etiqueta.Value[0] : 1;
                return valor >= 1 && valor <= 8 ? valor : 1;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is ExternalException)
            {
                return 1;
            }
        }

        /// <summary>Una copia de LadoTrabajo como mucho, en 32 bits, ya derecha.</summary>
        private static Bitmap Reducir(Image original, int orientacion)
        {
            double escala = Math.Min(1.0, (double)LadoTrabajo / Math.Max(original.Width, original.Height));
            int ancho = Math.Max(1, (int)Math.Round(original.Width * escala));
            int alto = Math.Max(1, (int)Math.Round(original.Height * escala));
            Bitmap copia = Copiar(original, ancho, alto);
            RotateFlipType giro = Giro(orientacion);
            if (giro != RotateFlipType.RotateNoneFlipNone)
            {
                copia.RotateFlip(giro);
            }

            return copia;
        }

        private static RotateFlipType Giro(int orientacion)
        {
            switch (orientacion)
            {
                case 2: return RotateFlipType.RotateNoneFlipX;
                case 3: return RotateFlipType.Rotate180FlipNone;
                case 4: return RotateFlipType.RotateNoneFlipY;
                case 5: return RotateFlipType.Rotate90FlipX;
                case 6: return RotateFlipType.Rotate90FlipNone;
                case 7: return RotateFlipType.Rotate270FlipX;
                case 8: return RotateFlipType.Rotate270FlipNone;
                default: return RotateFlipType.RotateNoneFlipNone;
            }
        }

        /// <summary>Un Bitmap propio (no depende del flujo) de ese tamano, con transparencia.</summary>
        private static Bitmap Copiar(Image original, int ancho, int alto)
        {
            Bitmap copia = new Bitmap(ancho, alto, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(copia))
                using (ImageAttributes bordes = new ImageAttributes())
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = ancho == original.Width && alto == original.Height
                        ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    // Sin esto el borde sale con una linea semitransparente.
                    bordes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(original, new Rectangle(0, 0, ancho, alto), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, bordes);
                }

                return copia;
            }
            catch
            {
                copia.Dispose();
                throw;
            }
        }

        /// <summary>El cuadrado de la fuente a lado x lado (lo que no cae dentro de la imagen queda transparente).</summary>
        private static Bitmap Recorte(Bitmap fuente, RectangleF cuadrado, int lado)
        {
            Bitmap recorte = new Bitmap(lado, lado, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(recorte))
                using (ImageAttributes bordes = new ImageAttributes())
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    bordes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(fuente, new Rectangle(0, 0, lado, lado), cuadrado.X, cuadrado.Y, cuadrado.Width, cuadrado.Height, GraphicsUnit.Pixel, bordes);
                }

                return recorte;
            }
            catch
            {
                recorte.Dispose();
                throw;
            }
        }

        private static byte[] Codificar(Bitmap imagen, ImageFormat formato)
        {
            using (MemoryStream salida = new MemoryStream())
            {
                if (formato.Guid == ImageFormat.Jpeg.Guid)
                {
                    ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    if (codec != null)
                    {
                        using (EncoderParameters parametros = new EncoderParameters(1))
                        {
                            parametros.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, CalidadJpeg);
                            // El JPEG no guarda transparencia: se aplana sobre blanco (solo llega aqui opaca).
                            using (Bitmap opaca = new Bitmap(imagen.Width, imagen.Height, PixelFormat.Format24bppRgb))
                            {
                                using (Graphics g = Graphics.FromImage(opaca))
                                {
                                    g.Clear(Color.White);
                                    g.DrawImageUnscaled(imagen, 0, 0);
                                }

                                opaca.Save(salida, codec, parametros);
                            }
                        }

                        return salida.ToArray();
                    }
                }

                imagen.Save(salida, formato);
                return salida.ToArray();
            }
        }
    }
}
