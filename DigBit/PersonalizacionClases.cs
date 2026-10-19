using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;

namespace DigBit
{
    /// <summary>
    /// Como ve el profesor una de sus clases. No se modifica despues de
    /// publicarse: cuando algo cambia, PersonalizacionClases pone otro entero
    /// (por eso se puede comparar por referencia para saber si repintar).
    /// </summary>
    internal sealed class AspectoClase
    {
        public int ClaseId;
        public int MateriaId;
        public int GrupoId;
        /// <summary>Oficial.</summary>
        public string Materia = "";
        /// <summary>Oficial.</summary>
        public string Grupo = "";
        /// <summary>De este ciclo (activa y vigente hasta hace 30 dias, como Mis grupos).</summary>
        public bool Actual;
        /// <summary>El que se pinta: el elegido o el automatico, siempre ColoresProfesor.ParaTextoNegro.</summary>
        public Color Color;
        /// <summary>El que le toca a su materia sin elegir nada.</summary>
        public Color ColorAutomatico;
        /// <summary>'#RRGGBB' o null (automatico).</summary>
        public string ColorElegido;
        /// <summary>O null.</summary>
        public string NombreCorto;
        /// <summary>O null (las iniciales).</summary>
        public string ImagenSha1;
        /// <summary>Lo que lleva el circulo sin imagen: las iniciales, o el nombre corto si tiene 4 caracteres o menos.</summary>
        public string TextoCirculo = "";
        /// <summary>Su grupo tiene la suya (no sigue a la de la materia).</summary>
        public bool DeSuGrupo;
        /// <summary>Se ve distinto de lo de siempre: tiene color elegido, nombre corto o imagen.</summary>
        public bool Personalizada;

        /// <summary>El nombre corto si lo hay; si no, el oficial. Donde el oficial no cabe.</summary>
        public string Nombre
        {
            get { return NombreCorto ?? Materia; }
        }

        /// <summary>Para las ayudas, que siempre llevan el oficial: "POO (Programacion Orientada a Objetos)", o solo el oficial.</summary>
        public string NombreConOficial
        {
            get { return NombreCorto == null ? Materia : NombreCorto + " (" + Materia + ")"; }
        }
    }

    /// <summary>
    /// Lo que cada profesor eligio para sus clases (db/19), ya leido y en
    /// memoria, para que sus pantallas lo pinten sin consultar: el panel, Mis
    /// grupos y Mi horario. Es solo del profesor de la sesion; el administrador y
    /// el encargado nunca lo leen. Sin db/19 (o antes de la primera lectura)
    /// Disponible es falso y De da null: todo se pinta como siempre.
    ///
    /// Quien lee (en segundo plano) anota Version antes, trae la lectura
    /// (PersonalizacionClasesDatos.Leer) y las imagenes que falten
    /// (TraerImagenes), y en el hilo de la pantalla llama a Publicar. Solo si lo
    /// que se ve cambio se sustituye el mapa, sube Version, se vacia la cache de
    /// circulos y se lanza Cambio: la lectura de cada 30 s del panel no repinta
    /// nada si nada cambio.
    ///
    /// Colores automaticos, por materia y sin escribir nada: sus materias en
    /// orden (primero las de este ciclo, por id; luego las demas) se reparten la
    /// paleta de DigBit con ColoresProfesor.Calcular, el mismo reparto que entre
    /// profesores; los colores de las filas de materia son los "elegidos" (un
    /// intercambio: si eligen el de otra materia, esa toma el que quedo libre).
    /// Los de las filas de grupo son excepciones y no entran en el reparto.
    /// </summary>
    internal static class PersonalizacionClases
    {
        /// <summary>Lo leido de una vez, ya resuelto. Se sustituye entero y nunca se modifica despues.</summary>
        private sealed class Mapa
        {
            public static readonly Mapa Vacio = new Mapa();

            public int ProfesorId;
            public bool Disponible;
            public Dictionary<int, AspectoClase> Clases = new Dictionary<int, AspectoClase>();
            /// <summary>Las materias en el orden del reparto.</summary>
            public List<int> Orden = new List<int>();
            /// <summary>El color de la fila de cada materia que lo tiene.</summary>
            public Dictionary<int, string> Elegidos = new Dictionary<int, string>();
            /// <summary>Lo que da el reparto por materia (sin aclarar).</summary>
            public Dictionary<int, Color> Colores = new Dictionary<int, Color>();
            public string Firma = "";
        }

        // Lo leen a la vez la pantalla y los refrescos en segundo plano.
        private static volatile Mapa actual = Mapa.Vacio;
        private static int version;
        // Version al cerrar la ultima sesion: lo que se leyo antes se tira entero.
        private static int versionDelOlvido;
        // Las imagenes por SHA1 (inmutables): se llenan en segundo plano.
        private static readonly ConcurrentDictionary<string, byte[]> imagenes = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cambio lo que se ve (al guardar, o porque una lectura trajo algo
        /// distinto o la imagen que faltaba). Siempre en el hilo de la pantalla.
        /// Cada ventana se da de alta al crearse y de baja en OnFormClosed, como
        /// con ColoresProfesor.Cambio.
        /// </summary>
        public static event Action Cambio;

        /// <summary>
        /// Sube en cada cambio. Quien lee en segundo plano lo anota antes: si al
        /// llegar ya cambio (otra lectura o un guardado llego antes), no pisa lo nuevo.
        /// </summary>
        public static int Version
        {
            get { return Volatile.Read(ref version); }
        }

        /// <summary>False sin db/19 (o antes de la primera lectura): ni circulos ni colores de clase.</summary>
        public static bool Disponible
        {
            get { return actual.Disponible; }
        }

        /// <summary>El profesor de la ultima lectura publicada (0 si no hay).</summary>
        public static int ProfesorId
        {
            get { return actual.ProfesorId; }
        }

        /// <summary>Como ve el profesor esa clase; null si no se sabe (sin db/19, sin leer, o no es suya).</summary>
        public static AspectoClase De(int idClase)
        {
            Mapa mapa = actual;
            AspectoClase aspecto;
            return mapa.Disponible && mapa.Clases.TryGetValue(idClase, out aspecto) ? aspecto : null;
        }

        /// <summary>Sus clases de esa materia (de este ciclo y de antes), por grupo. Para el dialogo.</summary>
        public static List<AspectoClase> DeLaMateria(int idMateria)
        {
            Mapa mapa = actual;
            return mapa.Clases.Values.Where(a => a.MateriaId == idMateria)
                .OrderBy(a => a.Grupo, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ClaseId).ToList();
        }

        /// <summary>Sus clases de este ciclo, por materia y grupo (para saber que colores ya usa).</summary>
        public static List<AspectoClase> Actuales()
        {
            Mapa mapa = actual;
            return mapa.Clases.Values.Where(a => a.Actual)
                .OrderBy(a => a.Materia, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.Grupo, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ClaseId).ToList();
        }

        /// <summary>Los bytes de esa imagen si ya estan en memoria; si no, null.</summary>
        public static byte[] BytesDe(string sha1)
        {
            byte[] datos;
            return !string.IsNullOrEmpty(sha1) && imagenes.TryGetValue(sha1, out datos) ? datos : null;
        }

        /// <summary>
        /// Segundo plano: abre su conexion y trae las imagenes de la lectura que
        /// no estan en memoria. Nunca lanza: si falla, las que haya (y las demas
        /// se piden en la siguiente lectura; mientras, van las iniciales).
        /// </summary>
        public static Dictionary<string, byte[]> TraerImagenes(int idProfesor, LecturaPersonal leida)
        {
            if (leida == null)
            {
                return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            }

            return TraerFaltantes(idProfesor, leida.Clases.Select(c => c.ImagenSha1));
        }

        /// <summary>
        /// Hilo de la pantalla. leida == null: sin db/19 (todo como siempre);
        /// quien tuvo un fallo pasajero al leer no llama (asi no se borran los
        /// circulos). versionAlLeer: Version cuando se empezo a leer; si desde
        /// entonces cambio, las imagenes se aprovechan pero el mapa no se pisa.
        /// </summary>
        public static void Publicar(LecturaPersonal leida, IDictionary<string, byte[]> imagenesNuevas, int versionAlLeer)
        {
            if (versionAlLeer < Volatile.Read(ref versionDelOlvido))
            {
                // Se leyo antes de cerrar la sesion: era del profesor anterior.
                return;
            }

            List<string> llegaron = new List<string>();
            if (imagenesNuevas != null)
            {
                foreach (KeyValuePair<string, byte[]> imagen in imagenesNuevas)
                {
                    if (!string.IsNullOrEmpty(imagen.Key) && imagen.Value != null && imagen.Value.Length > 0
                        && imagenes.TryAdd(imagen.Key.ToLowerInvariant(), imagen.Value))
                    {
                        llegaron.Add(imagen.Key);
                    }
                }
            }

            bool cambio = false;
            if (versionAlLeer == Version)
            {
                Mapa nuevo = Construir(leida);
                if (nuevo.Firma != actual.Firma)
                {
                    actual = nuevo;
                    Interlocked.Increment(ref version);
                    cambio = true;
                }
            }

            // Llego la imagen de una clase que hasta ahora salia con sus iniciales.
            Mapa mapa = actual;
            if (!cambio && llegaron.Count > 0
                && mapa.Clases.Values.Any(a => a.ImagenSha1 != null && llegaron.Contains(a.ImagenSha1, StringComparer.OrdinalIgnoreCase)))
            {
                cambio = true;
            }

            if (cambio)
            {
                CirculoClase.VaciarCache();
                Action aviso = Cambio;
                if (aviso != null)
                {
                    aviso();
                }
            }
        }

        /// <summary>
        /// Al cerrar la sesion (AppContexto.CerrarSesion): en un equipo del
        /// laboratorio entra otro profesor y no debe heredar ni las imagenes ni
        /// los circulos. Libera los Bitmap. No lanza Cambio (las ventanas se cierran).
        /// </summary>
        public static void Olvidar()
        {
            actual = Mapa.Vacio;
            int nueva = Interlocked.Increment(ref version);
            Volatile.Write(ref versionDelOlvido, nueva);
            imagenes.Clear();
            CirculoClase.VaciarCache();
        }

        /// <summary>
        /// El dialogo "Personalizar tu clase" para esa clase, modal sobre dueno.
        /// True si se guardo algo (y ya se publico: las ventanas abiertas se
        /// enteran por Cambio). False si se cancelo, si no habia nada que guardar o
        /// si no se puede (encargado, sin db/19, clase desconocida).
        /// </summary>
        public static bool Abrir(IWin32Window dueno, int idClase)
        {
            if (Permisos.Bloquear(dueno, "Personalizar una clase"))
            {
                return false;
            }

            if (Datos_User.TipoUsuario != TiposUsuario.Profesor)
            {
                MessageBox.Show(dueno, "Solo quien imparte la clase puede personalizarla.", "Personalizar tu clase",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            AspectoClase aspecto = De(idClase);
            if (aspecto == null)
            {
                Log.Aviso("Personalizar la clase " + idClase + ": no esta en lo leido (sin db/19 o aun sin leer).");
                return false;
            }

            // Su imagen, si aun no llego (sin ella el dialogo ensenaria las iniciales).
            if (aspecto.ImagenSha1 != null && BytesDe(aspecto.ImagenSha1) == null)
            {
                Cursor antes = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    foreach (KeyValuePair<string, byte[]> imagen in TraerFaltantes(actual.ProfesorId, new[] { aspecto.ImagenSha1 }))
                    {
                        imagenes.TryAdd(imagen.Key.ToLowerInvariant(), imagen.Value);
                    }
                }
                finally
                {
                    Cursor.Current = antes;
                }
            }

            using (DialogoPersonalizarClase dialogo = new DialogoPersonalizarClase(aspecto))
            {
                dialogo.ShowDialog(dueno);
                return dialogo.Guardado;
            }
        }

        /// <summary>
        /// Que materias cambiarian de color si esa eligiera ese color (null:
        /// automatico) para todos sus grupos: (materia, color que tendria).
        /// Para avisar en el dialogo antes de guardar.
        /// </summary>
        internal static List<KeyValuePair<int, Color>> Simular(int idMateria, Color? color)
        {
            Mapa mapa = actual;
            Dictionary<int, string> elegidos = new Dictionary<int, string>(mapa.Elegidos);
            if (color.HasValue)
            {
                elegidos[idMateria] = ColoresProfesor.ATexto(color.Value);
            }
            else
            {
                elegidos.Remove(idMateria);
            }

            List<KeyValuePair<int, Color>> cambian = new List<KeyValuePair<int, Color>>();
            foreach (KeyValuePair<int, Color> despues in ColoresProfesor.Calcular(mapa.Orden, elegidos))
            {
                Color antes;
                if (despues.Key != idMateria && mapa.Colores.TryGetValue(despues.Key, out antes) && antes.ToArgb() != despues.Value.ToArgb())
                {
                    cambian.Add(despues);
                }
            }

            return cambian;
        }

        /// <summary>Pone en memoria una imagen recien guardada (cuando no se pudo volver a leer despues de guardar).</summary>
        internal static void Recordar(string sha1, byte[] datos)
        {
            if (!string.IsNullOrEmpty(sha1) && datos != null && datos.Length > 0)
            {
                imagenes.TryAdd(sha1.ToLowerInvariant(), datos);
            }
        }

        /// <summary>
        /// La pantalla del alumno (kiosco): el circulo de una clase de un profesor
        /// que no es el de la sesion, por su materia y su grupo, de lo que se leyo
        /// de ese profesor. No toca lo del profesor en sesion. El nombre corto es
        /// del profesor: el alumno ve las iniciales de la materia oficial. Las
        /// imagenes se ponen en memoria con Recordar y se olvidan al cerrar la
        /// sesion. Null sin db/19 o si esa clase no esta en lo leido.
        /// </summary>
        internal static AspectoClase DeOtroProfesor(LecturaPersonal leida, int idMateria, int idGrupo)
        {
            if (leida == null)
            {
                return null;
            }

            AspectoClase aspecto = Construir(leida).Clases.Values
                .Where(a => a.MateriaId == idMateria && a.GrupoId == idGrupo)
                .OrderByDescending(a => a.Actual).ThenByDescending(a => a.ClaseId)
                .FirstOrDefault();
            if (aspecto != null)
            {
                aspecto.NombreCorto = null;
                aspecto.TextoCirculo = CirculoClase.Iniciales(aspecto.Materia);
            }

            return aspecto;
        }

        private static Dictionary<string, byte[]> TraerFaltantes(int idProfesor, IEnumerable<string> sha1)
        {
            Dictionary<string, byte[]> traidas = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                List<string> faltan = sha1.Where(s => !string.IsNullOrEmpty(s) && !imagenes.ContainsKey(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (faltan.Count == 0 || idProfesor <= 0)
                {
                    return traidas;
                }

                using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
                {
                    return PersonalizacionClasesDatos.Imagenes(conexion, idProfesor, faltan);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Traer las imagenes de las clases del profesor " + idProfesor, ex);
                return traidas;
            }
        }

        /// <summary>
        /// Lo que se ve de cada clase, ya resuelto: el color (elegido o del
        /// reparto), el texto del circulo y la firma para saber si cambio algo.
        /// Funcion pura: no toca el estado.
        /// </summary>
        private static Mapa Construir(LecturaPersonal leida)
        {
            if (leida == null)
            {
                return Mapa.Vacio;
            }

            Mapa mapa = new Mapa { ProfesorId = leida.ProfesorId, Disponible = true };
            List<FilaPersonal> filas = leida.Clases ?? new List<FilaPersonal>();

            // 1) El orden: primero las materias de este ciclo, por id; luego las demas.
            List<int> deAhora = filas.Where(f => f.Actual).Select(f => f.MateriaId).Distinct().OrderBy(id => id).ToList();
            List<int> deAntes = filas.Select(f => f.MateriaId).Distinct().Where(id => !deAhora.Contains(id)).OrderBy(id => id).ToList();
            mapa.Orden = deAhora.Concat(deAntes).ToList();

            // 2) Los elegidos: los colores de las filas de materia.
            foreach (FilaPersonal fila in filas)
            {
                if (fila.ColorDeLaMateria != null && !mapa.Elegidos.ContainsKey(fila.MateriaId))
                {
                    mapa.Elegidos[fila.MateriaId] = fila.ColorDeLaMateria;
                }
            }

            // 3) El reparto, y lo que le tocaria a cada materia sin elegir.
            mapa.Colores = ColoresProfesor.Calcular(mapa.Orden, mapa.Elegidos);
            Dictionary<int, Color> automaticos = new Dictionary<int, Color>();
            foreach (int materia in mapa.Orden)
            {
                if (mapa.Elegidos.ContainsKey(materia))
                {
                    Dictionary<int, string> sinElla = mapa.Elegidos.Where(kv => kv.Key != materia).ToDictionary(kv => kv.Key, kv => kv.Value);
                    automaticos[materia] = ColoresProfesor.Calcular(mapa.Orden, sinElla)[materia];
                }
                else
                {
                    automaticos[materia] = mapa.Colores[materia];
                }
            }

            StringBuilder firma = new StringBuilder("p").Append(leida.ProfesorId.ToString(CultureInfo.InvariantCulture));
            foreach (FilaPersonal fila in filas.OrderBy(f => f.ClaseId))
            {
                if (mapa.Clases.ContainsKey(fila.ClaseId))
                {
                    continue;
                }

                // Un grupo con su fila sigue, sin color, al de su materia: el automatico de la
                // materia (el de antes de elegir) es justo el que paso a la materia desplazada.
                Color automatico = ColoresProfesor.ParaTextoNegro(fila.DeSuGrupo ? mapa.Colores[fila.MateriaId] : automaticos[fila.MateriaId]);
                Color elegido;
                Color pintado;
                if (ColoresProfesor.DesdeTexto(fila.Color, out elegido))
                {
                    pintado = ColoresProfesor.ParaTextoNegro(elegido);
                }
                else if (fila.DeSuGrupo)
                {
                    // La fila de su grupo manda entera; sin color, el de su materia (ver arriba).
                    pintado = automatico;
                }
                else
                {
                    pintado = ColoresProfesor.ParaTextoNegro(mapa.Colores[fila.MateriaId]);
                }

                string nombre = string.IsNullOrWhiteSpace(fila.NombreCorto) ? null : fila.NombreCorto.Trim();
                AspectoClase aspecto = new AspectoClase
                {
                    ClaseId = fila.ClaseId,
                    MateriaId = fila.MateriaId,
                    GrupoId = fila.GrupoId,
                    Materia = fila.Materia ?? "",
                    Grupo = fila.Grupo ?? "",
                    Actual = fila.Actual,
                    Color = pintado,
                    ColorAutomatico = automatico,
                    ColorElegido = ColoresProfesor.DesdeTexto(fila.Color, out elegido) ? ColoresProfesor.ATexto(elegido) : null,
                    NombreCorto = nombre,
                    ImagenSha1 = string.IsNullOrEmpty(fila.ImagenSha1) ? null : fila.ImagenSha1.ToLowerInvariant(),
                    TextoCirculo = CirculoClase.TextoDelCirculo(fila.Materia, nombre),
                    DeSuGrupo = fila.DeSuGrupo
                };
                aspecto.Personalizada = aspecto.ColorElegido != null || aspecto.NombreCorto != null || aspecto.ImagenSha1 != null;
                mapa.Clases[fila.ClaseId] = aspecto;

                firma.Append('|').Append(aspecto.ClaseId.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(aspecto.MateriaId.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(aspecto.GrupoId.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(aspecto.Color.ToArgb().ToString("X8", CultureInfo.InvariantCulture))
                    .Append(':').Append(aspecto.ColorAutomatico.ToArgb().ToString("X8", CultureInfo.InvariantCulture))
                    .Append(':').Append(aspecto.ColorElegido ?? "-")
                    .Append(':').Append(aspecto.NombreCorto ?? "\u0001")
                    .Append(':').Append(aspecto.ImagenSha1 ?? "-")
                    .Append(':').Append(aspecto.Actual ? '1' : '0')
                    .Append(aspecto.DeSuGrupo ? '1' : '0')
                    .Append(':').Append(aspecto.Materia)
                    .Append('\u0002').Append(aspecto.Grupo);
            }

            mapa.Firma = firma.ToString();
            return mapa;
        }
    }
}
