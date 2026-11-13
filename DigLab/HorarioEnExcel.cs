using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DigLab.conexion;
using DigLab.Infraestructura;

namespace DigLab
{
    /// <summary>Un laboratorio que viene en el Excel: lo que trae y lo que el horario tiene ahora en el.</summary>
    internal sealed class LaboratorioDelExcel
    {
        public int Id;
        public string Nombre = "";
        public string Hoja = "";
        public bool Activo = true;
        public int Clases;
        public int Minutos;
        public int ClasesAntes;
        public int MinutosAntes;
    }

    /// <summary>Lo que se entendio de un Excel con horarios (HorarioEnExcel.Leer).</summary>
    internal sealed class LecturaDeHorario
    {
        /// <summary>Lo que impide cargarlo, con donde esta: "Laboratorio CISCO, lunes 07:00: ...".</summary>
        public List<string> Problemas = new List<string>();
        /// <summary>Lo que no impide cargarlo (choques, laboratorios deshabilitados, hojas que no se usaron).</summary>
        public List<string> Avisos = new List<string>();
        public List<LaboratorioDelExcel> Laboratorios = new List<LaboratorioDelExcel>();
        public List<ClaseDelExcel> Clases = new List<ClaseDelExcel>();
        /// <summary>Las clases del horario que solo tienen horas en esos laboratorios y no vienen en el Excel (se quitan).</summary>
        public List<string> SeQuitan = new List<string>();

        public bool SePuedeCargar
        {
            get { return Problemas.Count == 0 && Laboratorios.Count > 0; }
        }
    }

    /// <summary>
    /// Un horario en Excel, en cuadricula (pedido del 2026-11-12): una hoja por laboratorio,
    /// los dias en columnas, las horas en renglones y en cada casilla la materia, el grupo y
    /// la persona docente. Crear arma la plantilla para llenarla (con lo que el horario en
    /// preparacion ya tiene); Leer la entiende contra lo registrado en DigLab: con algo que
    /// no esta registrado dice que y donde, y no se carga nada.
    /// </summary>
    internal static class HorarioEnExcel
    {
        public const string HojaComo = "Cómo llenarlo";
        public const string HojaRegistrados = "Registrados";
        public const string SinDocente = "Sin docente";
        /// <summary>El nombre de la lista desplegable de las casillas (hoja «Registrados»).</summary>
        public const string NombreLista = "ClasesRegistradas";
        /// <summary>Entre docente, grupo y materia en la lista desplegable: una casilla tiene un solo renglon.</summary>
        public const string Separador = " · ";

        private static readonly string[] Dias = { "", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado", "domingo" };
        private static readonly string[] DiasCortos = { "", "lun", "mar", "mie", "jue", "vie", "sab", "dom" };
        private static readonly HashSet<string> Libres = new HashSet<string> { "", "libre", "disponible", "vacio", "vacia", "sin clase", "x" };
        private static readonly HashSet<string> SinDocenteTextos = new HashSet<string>
        {
            "sin docente", "sin profesor", "sin profesora", "sin maestro", "sin maestra", "por asignar", "por definir", "docente por asignar"
        };

        private static readonly HashSet<string> Titulos = new HashSet<string>
        {
            "ing", "ingeniero", "ingeniera", "lic", "licenciado", "licenciada", "mtro", "mtra", "maestro", "maestra", "dr", "dra",
            "doctor", "doctora", "prof", "profa", "profesor", "profesora", "mc", "msc", "arq", "cp", "ma", "mti", "mdi"
        };

        private static readonly Regex Horas = new Regex(@"(\d{1,2})\s*[:.]\s*(\d{2})\s*(a\.?\s*m\.?|p\.?\s*m\.?)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HorasSueltas = new Regex(@"^\s*(\d{1,2})\s*(?:(?:-|–|—|a)\s*(\d{1,2}))?\s*(?:h|hrs?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Color Azul = Color.FromArgb(40, 70, 110);
        private static readonly Color AzulClaro = Color.FromArgb(226, 234, 243);
        private static readonly Color GrisHora = Color.FromArgb(242, 242, 242);
        private static readonly Color SinDocenteFondo = Color.FromArgb(228, 228, 228);

        // =====================================================================
        // La plantilla
        // =====================================================================

        /// <summary>
        /// El libro para llenar: "Como llenarlo", una hoja por laboratorio (los que estan en
        /// uso y los que ya tienen horas en el horario) con lo que ya tiene, y "Registrados".
        /// </summary>
        /// <param name="leido">El horario en preparacion (null: en blanco).</param>
        public static byte[] Crear(CicloLeido leido, CatalogosDeHorario catalogos, string nombreHorario)
        {
            List<CargaCiclo> cargas = leido == null ? new List<CargaCiclo>() : leido.Cargas.Where(c => !c.Quitada).ToList();
            List<FranjaCiclo> todas = cargas.SelectMany(c => c.Franjas).ToList();
            HashSet<int> conHoras = new HashSet<int>(todas.Select(f => f.LaboratorioId));
            List<CatalogosDeHorario.Laboratorio> laboratorios = catalogos.Laboratorios.Where(l => l.Activo || conHoras.Contains(l.Id)).ToList();
            if (laboratorios.Count == 0)
            {
                throw new ErrorDeDatos("No hay ningún laboratorio en uso: habilita uno en «Laboratorios».");
            }

            int paso = Paso(todas);
            TimeSpan desde = TimeSpan.FromHours(EstiloHorarios.HoraApertura);
            TimeSpan hasta = TimeSpan.FromHours(EstiloHorarios.HoraCierre);
            foreach (FranjaCiclo f in todas)
            {
                desde = f.Inicio < desde ? f.Inicio : desde;
                hasta = f.Fin > hasta ? f.Fin : hasta;
            }

            desde = TimeSpan.FromMinutes(Math.Floor(desde.TotalMinutes / paso) * paso);
            hasta = TimeSpan.FromMinutes(Math.Min(24 * 60, Math.Ceiling(hasta.TotalMinutes / paso) * paso));

            List<string> opciones = OpcionesDeClase(cargas, catalogos);
            List<Tuple<string, Color>> colores = ColoresPorDocente(catalogos);
            List<HojaExcel> hojas = new List<HojaExcel> { HojaDeComo(nombreHorario) };
            HashSet<string> nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HojaComo, HojaRegistrados };
            foreach (CatalogosDeHorario.Laboratorio laboratorio in laboratorios)
            {
                string nombre = laboratorio.Nombre;
                HojaExcel hoja = new HojaExcel(nombre);
                for (int n = 2; nombres.Contains(hoja.Nombre); n++)
                {
                    string sufijo = " (" + n + ")";
                    hoja = new HojaExcel((nombre.Length + sufijo.Length > 31 ? nombre.Substring(0, 31 - sufijo.Length) : nombre) + sufijo);
                }

                nombres.Add(hoja.Nombre);
                LlenarLaboratorio(hoja, laboratorio, cargas, catalogos, nombreHorario, paso, desde, hasta, opciones.Count > 0, colores);
                hojas.Add(hoja);
            }

            hojas.Add(HojaDeRegistrados(catalogos, opciones));
            return LibroExcel.Crear(hojas, "Horario «" + nombreHorario + "»");
        }

        /// <summary>
        /// Lo que se elige en la flecha de cada casilla (pedido del 2026-11-12: "para que no tenga
        /// que escribir"): las clases del horario y las de Horarios que siguen vigentes, como
        /// "Marco Llinas · 6ISC11 · Redes de computadoras". El docente y el grupo van primero
        /// porque Excel corta la lista al ancho de la columna: si algo no cabe, es el final de la
        /// materia. El docente va corto si con eso no se confunde con otro. Sin docentes de baja.
        /// </summary>
        internal static List<string> OpcionesDeClase(IEnumerable<CargaCiclo> cargas, CatalogosDeHorario catalogos)
        {
            Catalogo catalogo = new Catalogo(catalogos, null);
            IEnumerable<Tuple<int?, int, int>> todas = (cargas ?? new List<CargaCiclo>()).Select(c => Tuple.Create(c.ProfesorId, c.GrupoId, c.MateriaId))
                .Concat(catalogos.Clases.Select(c => Tuple.Create((int?)c.ProfesorId, c.GrupoId, c.MateriaId)));
            HashSet<string> vistas = new HashSet<string>();
            List<KeyValuePair<bool, string>> opciones = new List<KeyValuePair<bool, string>>();
            foreach (Tuple<int?, int, int> t in todas)
            {
                CatalogosDeHorario.Docente d = t.Item1.HasValue ? catalogos.Docentes.FirstOrDefault(x => x.Id == t.Item1.Value) : null;
                CatalogosDeHorario.Grupo g = catalogos.Grupos.FirstOrDefault(x => x.Id == t.Item2);
                CatalogosDeHorario.Materia m = catalogos.Materias.FirstOrDefault(x => x.Id == t.Item3);
                if ((t.Item1.HasValue && (d == null || !d.Activo)) || g == null || m == null)
                {
                    continue;
                }

                string texto = (d == null ? SinDocente : catalogo.NombreParaLista(d)) + Separador + g.Nombre.Trim() + Separador + m.Nombre.Trim();
                if (vistas.Add(texto))
                {
                    opciones.Add(new KeyValuePair<bool, string>(d == null, texto));
                }
            }

            return opciones.OrderBy(o => o.Key).ThenBy(o => o.Value, StringComparer.CurrentCultureIgnoreCase).Select(o => o.Value).ToList();
        }

        /// <summary>
        /// El color de cada docente en la cuadricula, con formato condicional (pedido del
        /// 2026-11-12: "cuando se agregue uno de los seleccionados también debe iluminarse del
        /// color del maestro"): la formula (para la primera casilla, "B5") y su color. Se pinta
        /// la casilla que empieza como la opcion de la lista ("Marco Llinas · ...") o que
        /// termina con su nombre completo (la de tres renglones). Asi, al borrarla, el color se va.
        /// "{0}" es la casilla.
        /// </summary>
        internal static List<Tuple<string, Color>> ColoresPorDocente(CatalogosDeHorario catalogos)
        {
            Catalogo catalogo = new Catalogo(catalogos, null);
            List<Tuple<string, Color>> reglas = new List<Tuple<string, Color>>();
            // Tambien los de baja: una clase suya que ya este en el horario se sigue viendo de su color.
            foreach (CatalogosDeHorario.Docente d in catalogos.Docentes)
            {
                reglas.Add(Tuple.Create(Empieza(catalogo.NombreParaLista(d) + Separador, d.Completo), ColorDe(d.Id)));
            }

            reglas.Add(Tuple.Create(Empieza(SinDocente + Separador, SinDocente), SinDocenteFondo));
            return reglas;
        }

        /// <summary>
        /// OR(LEFT({0},LEN("inicio"))="inicio",RIGHT({0},LEN("fin")+1)=CHAR(10)&amp;"fin"): empieza como la
        /// opcion de la lista, o su ultimo renglon es el nombre completo (entero: "Juan Perez" no
        /// pinta a "Maria Juan Perez"). Excel compara sin mayusculas.
        /// </summary>
        private static string Empieza(string inicio, string fin)
        {
            Func<string, string> comillas = s => "\"" + s.Replace("\"", "\"\"") + "\"";
            return "OR(LEFT({0},LEN(" + comillas(inicio) + "))=" + comillas(inicio) + ",RIGHT({0},LEN(" + comillas(fin) + ")+1)=CHAR(10)&" + comillas(fin) + ")";
        }

        /// <summary>De cuantos minutos es cada renglon: 60, o menos si alguna clase empieza o termina a otra hora.</summary>
        private static int Paso(IEnumerable<FranjaCiclo> franjas)
        {
            int paso = 60;
            foreach (FranjaCiclo f in franjas)
            {
                paso = Mcd(paso, (int)Math.Round(f.Inicio.TotalMinutes) % 60);
                paso = Mcd(paso, (int)Math.Round(f.Fin.TotalMinutes) % 60);
            }

            return Math.Max(10, paso);
        }

        private static int Mcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }

            return Math.Abs(a);
        }

        private static HojaExcel HojaDeComo(string nombreHorario)
        {
            HojaExcel hoja = new HojaExcel(HojaComo) { Horizontal = false, Pie = "DigLab" };
            EstiloExcel titulo = new EstiloExcel { Negrita = true, Tamano = 16, Letra = Azul };
            EstiloExcel subtitulo = new EstiloExcel { Tamano = 10, Letra = Color.FromArgb(90, 90, 90) };
            EstiloExcel numero = new EstiloExcel { Negrita = true, Tamano = 11, Letra = Azul, Horizontal = "right" };
            EstiloExcel texto = new EstiloExcel { Tamano = 11, Ajustar = true };
            EstiloExcel nota = new EstiloExcel { Tamano = 10, Letra = Color.FromArgb(90, 90, 90), Ajustar = true };
            hoja.Ancho(1, 4);
            hoja.Ancho(2, 110);
            hoja.Texto(1, 1, "Horario de los laboratorios para DigLab", titulo);
            hoja.Alto(1, 24);
            hoja.Texto(2, 1, "«" + nombreHorario + "»: una semana tipo, sin fechas. Se carga en «Próximo ciclo», en el horario en preparación.", subtitulo);
            string[] pasos =
            {
                "Cada laboratorio va en su propia hoja, con su nombre como en DigLab. Una hoja que borres deja ese laboratorio como está.",
                "Haz clic en una casilla y elige la clase en su flecha: la lista tiene las clases registradas en DigLab (docente · grupo · materia), sin escribir nada. Para borrar una, selecciónala y pulsa Supr. Si la lista se ve chica, acerca la hoja (Ctrl + rueda del ratón): la lista crece con ella.",
                "Una clase de dos horas o más: elígela en cada hora (o combina sus casillas). Las horas libres, vacías.",
                "Si una clase no está en la lista, escríbela: la materia, el grupo y la persona docente, cada uno en su renglón (Alt+Enter pasa al siguiente). Solo valen los que ya están en DigLab (hoja «" + HojaRegistrados + "»). Si todavía no se sabe quién la da, «" + SinDocente + "».",
                "Puedes cambiar las horas de la primera columna (por ejemplo «07:00 - 07:50») y agregar o quitar renglones.",
                "En DigLab: «Próximo ciclo», «2. Preparación». Arrastra este archivo hasta el horario, o usa «Excel ▾», «Cargar un Excel...». Si algo no está registrado, DigLab dice qué y en qué casilla, y no carga nada.",
                "Lo que trae cada hoja reemplaza lo que el horario en preparación tiene en ese laboratorio. Nada cambia en los laboratorios hasta aplicarlo en «3. Aplicar con fechas»."
            };
            int fila = 4;
            for (int i = 0; i < pasos.Length; i++)
            {
                hoja.Texto(fila, 1, (i + 1).ToString(CultureInfo.InvariantCulture) + ".", numero);
                hoja.Texto(fila, 2, pasos[i], texto);
                hoja.Alto(fila, pasos[i].Length > 105 ? 32 : 17);
                fila++;
            }

            hoja.Texto(fila + 1, 2, "Una casilla elegida de la lista y una escrita a mano (las dos valen):", nota);
            EstiloExcel ejemplo = new EstiloExcel { Tamano = 9, Ajustar = true, Horizontal = "center", Cuadricula = true, Fondo = Color.FromArgb(200, 235, 140) };
            hoja.Ancho(3, 4);
            hoja.Texto(fila + 2, 2, "Nombre Apellido" + Separador + "5ISC11" + Separador + "Base de datos", ejemplo);
            hoja.Alto(fila + 2, 30);
            hoja.Texto(fila + 3, 2, "Base de datos\n5ISC11\nNombre Apellido Apellido", ejemplo);
            hoja.Alto(fila + 3, 44);
            return hoja;
        }

        private static void LlenarLaboratorio(HojaExcel hoja, CatalogosDeHorario.Laboratorio laboratorio, List<CargaCiclo> cargas,
            CatalogosDeHorario catalogos, string nombreHorario, int paso, TimeSpan desde, TimeSpan hasta, bool conLista,
            List<Tuple<string, Color>> colores)
        {
            List<KeyValuePair<CargaCiclo, FranjaCiclo>> franjas = cargas
                .SelectMany(c => c.Franjas.Where(f => f.LaboratorioId == laboratorio.Id).Select(f => new KeyValuePair<CargaCiclo, FranjaCiclo>(c, f)))
                .OrderBy(p => p.Value.DiaSemana).ThenBy(p => p.Value.Inicio)
                .ToList();
            int ultimoDia = franjas.Any(p => p.Value.DiaSemana == 7) ? 7 : 6;
            const int FilaDias = 4;

            EstiloExcel titulo = new EstiloExcel { Negrita = true, Tamano = 15, Letra = Azul };
            EstiloExcel subtitulo = new EstiloExcel { Tamano = 9, Letra = Color.FromArgb(100, 100, 100) };
            EstiloExcel encabezado = new EstiloExcel { Negrita = true, Tamano = 11, Letra = Color.White, Fondo = Azul, Horizontal = "center", Cuadricula = true };
            EstiloExcel hora = new EstiloExcel { Negrita = true, Tamano = 9, Fondo = GrisHora, Horizontal = "center", Cuadricula = true };
            EstiloExcel libre = new EstiloExcel { Tamano = 9, Ajustar = true, Horizontal = "center", Cuadricula = true };

            hoja.Texto(1, 1, EstiloHorarios.NombreLaboratorio(laboratorio.Nombre) + (laboratorio.Activo ? "" : " (deshabilitado)"), titulo);
            hoja.Alto(1, 22);
            hoja.Texto(2, 1, "«" + nombreHorario + "» · semana tipo · en cada casilla elige la clase en su flecha (docente · grupo · materia)", subtitulo);
            hoja.Texto(FilaDias, 1, "Hora", encabezado);
            hoja.Ancho(1, 15);
            for (int dia = 1; dia <= ultimoDia; dia++)
            {
                hoja.Texto(FilaDias, dia + 1, HorariosDatos.NombresDia[dia], encabezado);
                // La lista desplegable es del ancho de la columna y Excel no deja agrandarla (2026-11-12: "se ve
                // muy reducida"): con 40 se leen unos 49 caracteres (la mitad de las opciones enteras; al resto
                // le falta el final de la materia) y la semana sigue cabiendo en una pantalla de 1920 al 100 %.
                hoja.Ancho(dia + 1, 40);
            }

            hoja.Alto(FilaDias, 20);
            int renglones = (int)Math.Round((hasta - desde).TotalMinutes / paso);
            double alto = paso >= 60 ? 48 : Math.Max(16, 48.0 * paso / 60);
            for (int i = 0; i < renglones; i++)
            {
                TimeSpan inicio = desde + TimeSpan.FromMinutes(i * paso);
                TimeSpan fin = inicio + TimeSpan.FromMinutes(paso);
                int fila = FilaDias + 1 + i;
                hoja.Texto(fila, 1, HorariosDatos.Hora(inicio) + " - " + (fin.TotalHours >= 24 ? "24:00" : HorariosDatos.Hora(fin)), hora);
                hoja.Alto(fila, alto);
                for (int dia = 1; dia <= ultimoDia; dia++)
                {
                    hoja.Vacia(fila, dia + 1, libre);
                }
            }

            // Las clases, cada una en sus renglones combinados, con el color de su docente.
            HashSet<string> ocupadas = new HashSet<string>();
            foreach (KeyValuePair<CargaCiclo, FranjaCiclo> par in franjas)
            {
                FranjaCiclo f = par.Value;
                if (f.DiaSemana < 1 || f.DiaSemana > ultimoDia)
                {
                    continue;
                }

                int primera = FilaDias + 1 + (int)Math.Round((f.Inicio - desde).TotalMinutes / paso);
                int ultima = FilaDias + (int)Math.Round((f.Fin - desde).TotalMinutes / paso);
                int columna = f.DiaSemana + 1;
                List<string> suyas = Enumerable.Range(primera, Math.Max(0, ultima - primera + 1)).Select(r => r + ":" + columna).ToList();
                if (ultima < primera || suyas.Any(ocupadas.Contains))
                {
                    // Dos clases encimadas en un laboratorio no deberian estar; no se combina encima de otra.
                    Log.Aviso("HorarioEnExcel: en " + laboratorio.Nombre + " se encima " + par.Key.Nombre + " el dia " + f.DiaSemana + " " + HorariosDatos.Hora(f.Inicio) + "; no va en el Excel.");
                    continue;
                }

                foreach (string s in suyas)
                {
                    ocupadas.Add(s);
                }

                // Sin relleno fijo: el color lo pone el formato condicional de su docente (y se va si se borra).
                EstiloExcel clase = new EstiloExcel { Tamano = 9, Ajustar = true, Horizontal = "center", Cuadricula = true };
                hoja.Texto(primera, columna, TextoDeClase(par.Key, catalogos), clase);
                hoja.Combinar(primera, columna, ultima, columna);
            }

            if (renglones > 0)
            {
                string primeraCasilla = LibroExcel.Celda(FilaDias + 1, 2);
                foreach (Tuple<string, Color> regla in colores ?? new List<Tuple<string, Color>>())
                {
                    hoja.RellenoSi(FilaDias + 1, 2, FilaDias + renglones, ultimoDia + 1, regla.Item1.Replace("{0}", primeraCasilla), regla.Item2);
                }
            }

            if (conLista && renglones > 0)
            {
                hoja.ListaDesplegable(FilaDias + 1, 2, FilaDias + renglones, ultimoDia + 1, NombreLista, "No está en la lista",
                    "Esa clase no está en la lista de clases registradas. Puedes dejarla si la materia, el grupo y la persona docente están en DigLab (hoja «"
                    + HojaRegistrados + "»): se revisa al cargar el Excel.");
            }

            hoja.Congelar(FilaDias, 1);
            hoja.RepetirAlImprimir = FilaDias;
            hoja.Pie = "DigLab · " + EstiloHorarios.NombreLaboratorio(laboratorio.Nombre) + " · «" + nombreHorario + "»";
        }

        private static Color ColorDe(int profesor)
        {
            try
            {
                return ColoresProfesor.De(profesor);
            }
            catch (Exception ex)
            {
                Log.Error("HorarioEnExcel.ColorDe", ex);
                return AzulClaro;
            }
        }

        /// <summary>"Base de datos\n5ISC11\nLaura Cárdenas Ruiz": como se escribe en una casilla.</summary>
        internal static string TextoDeClase(CargaCiclo carga, CatalogosDeHorario catalogos)
        {
            string docente = SinDocente;
            if (carga.ProfesorId.HasValue)
            {
                CatalogosDeHorario.Docente d = catalogos.Docentes.FirstOrDefault(x => x.Id == carga.ProfesorId.Value);
                docente = d != null ? d.Completo : carga.Profesor;
            }

            string materia = carga.Materia;
            CatalogosDeHorario.Materia m = catalogos.Materias.FirstOrDefault(x => x.Id == carga.MateriaId);
            if (m != null)
            {
                materia = m.Nombre;
            }

            return materia.Trim() + "\n" + carga.Grupo.Trim() + "\n" + docente;
        }

        /// <summary>
        /// Lo registrado: primero las clases de la lista desplegable (con el nombre que usan las
        /// casillas), y luego docentes, grupos y materias, para escribir una que no este.
        /// </summary>
        private static HojaExcel HojaDeRegistrados(CatalogosDeHorario catalogos, List<string> opciones)
        {
            HojaExcel hoja = new HojaExcel(HojaRegistrados) { Horizontal = false, Pie = "DigLab · lo registrado" };
            EstiloExcel encabezado = new EstiloExcel { Negrita = true, Tamano = 10, Letra = Color.White, Fondo = Azul, Cuadricula = true };
            EstiloExcel normal = new EstiloExcel { Tamano = 10, Cuadricula = true };
            EstiloExcel gris = new EstiloExcel { Tamano = 9, Letra = Color.FromArgb(100, 100, 100), Cuadricula = true };
            string[] columnas = { "Clases (la lista de cada casilla)", "", "Personas docentes", "Número", "", "Grupos", "Carrera", "", "Materias" };
            double[] anchos = { 62, 3, 34, 14, 3, 14, 36, 3, 44 };
            for (int c = 0; c < columnas.Length; c++)
            {
                hoja.Ancho(c + 1, anchos[c]);
                if (columnas[c].Length > 0)
                {
                    hoja.Texto(1, c + 1, columnas[c], encabezado);
                }
            }

            int fila = 2;
            foreach (string opcion in opciones)
            {
                hoja.Texto(fila, 1, opcion, normal);
                fila++;
            }

            if (opciones.Count > 0)
            {
                hoja.Nombrar(NombreLista, 2, 1, opciones.Count + 1, 1);
            }

            fila = 2;
            foreach (CatalogosDeHorario.Docente d in catalogos.Docentes.Where(x => x.Activo))
            {
                hoja.Texto(fila, 3, d.Completo, normal);
                hoja.Texto(fila, 4, d.Numero, gris);
                fila++;
            }

            fila = 2;
            foreach (CatalogosDeHorario.Grupo g in catalogos.Grupos)
            {
                hoja.Texto(fila, 6, g.Nombre, normal);
                hoja.Texto(fila, 7, g.Carrera, gris);
                fila++;
            }

            fila = 2;
            foreach (string materia in catalogos.Materias.Select(m => m.Nombre.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase))
            {
                hoja.Texto(fila, 9, materia, normal);
                fila++;
            }

            hoja.Congelar(1, 0);
            hoja.RepetirAlImprimir = 1;
            return hoja;
        }

        // =====================================================================
        // Leer un Excel
        // =====================================================================

        /// <summary>Una casilla con clase, ya entendida.</summary>
        private sealed class Celda
        {
            public int? Profesor;
            public int Grupo;
            public int Materia;
            public string Clave;
        }

        private sealed class Renglon
        {
            public int Fila;
            public TimeSpan Inicio;
            public TimeSpan? Fin;
        }

        /// <summary>
        /// Entiende las hojas de un Excel contra lo registrado. Con cualquier problema,
        /// Problemas dice que y donde (y no se debe cargar nada).
        /// </summary>
        /// <param name="leido">El horario en preparacion (null: uno en blanco).</param>
        public static LecturaDeHorario Leer(List<HojaLeida> hojas, CatalogosDeHorario catalogos, CicloLeido leido)
        {
            LecturaDeHorario lectura = new LecturaDeHorario();
            Catalogo catalogo = new Catalogo(catalogos, leido);
            Problemas problemas = new Problemas();
            Dictionary<string, ClaseDelExcel> clases = new Dictionary<string, ClaseDelExcel>();
            Dictionary<int, string> hojaDe = new Dictionary<int, string>();

            foreach (HojaLeida hoja in hojas ?? new List<HojaLeida>())
            {
                string nombreHoja = (hoja.Nombre ?? "").Trim();
                if (Normal(nombreHoja) == Normal(HojaComo) || Normal(nombreHoja) == Normal(HojaRegistrados))
                {
                    continue;
                }

                if (hoja.Oculta)
                {
                    lectura.Avisos.Add("La hoja «" + nombreHoja + "» está oculta: no se usó.");
                    continue;
                }

                int filaDias;
                Dictionary<int, int> columnasDia;
                string errorDias;
                bool hayDias = BuscarDias(hoja, out filaDias, out columnasDia, out errorDias);
                CatalogosDeHorario.Laboratorio laboratorio = catalogo.Laboratorio(hoja, hayDias ? filaDias : Math.Min(hoja.UltimaFila, 6));
                if (!hayDias)
                {
                    if (laboratorio != null || errorDias != null)
                    {
                        problemas.Agregar("Hoja «" + nombreHoja + "»", errorDias ?? "no tiene la fila de los días (Lunes, Martes, Miércoles...).");
                    }
                    else if (hoja.UltimaFila > 0)
                    {
                        lectura.Avisos.Add("La hoja «" + nombreHoja + "» no tiene la fila de los días (Lunes, Martes...): no se usó.");
                    }

                    continue;
                }

                if (laboratorio == null)
                {
                    string parecido = catalogo.LaboratorioParecido(nombreHoja);
                    problemas.Agregar("Hoja «" + nombreHoja + "»", "no se sabe de qué laboratorio es: ponle a la hoja el nombre de uno de DigLab"
                        + (parecido != null ? " (¿es «" + parecido + "»?)." : " (" + string.Join(", ", catalogos.Laboratorios.Select(l => l.Nombre)) + ")."));
                    continue;
                }

                string otra;
                if (hojaDe.TryGetValue(laboratorio.Id, out otra))
                {
                    problemas.Agregar("Hoja «" + nombreHoja + "»", "es del mismo laboratorio que la hoja «" + otra + "» (" + laboratorio.Nombre + "): deja solo una.");
                    continue;
                }

                hojaDe[laboratorio.Id] = nombreHoja;
                string lugar = EstiloHorarios.NombreLaboratorio(laboratorio.Nombre);
                LaboratorioDelExcel delExcel = new LaboratorioDelExcel { Id = laboratorio.Id, Nombre = laboratorio.Nombre, Hoja = nombreHoja, Activo = laboratorio.Activo };
                lectura.Laboratorios.Add(delExcel);
                if (!laboratorio.Activo)
                {
                    lectura.Avisos.Add(lugar + " está deshabilitado: sus clases se guardan en el horario, pero no se dan mientras siga así.");
                }

                List<Renglon> renglones = LeerHoras(hoja, filaDias, columnasDia, lugar, problemas);
                if (renglones == null)
                {
                    continue;
                }

                LeerClases(hoja, laboratorio, lugar, renglones, columnasDia, catalogo, problemas, clases);
            }

            if (lectura.Laboratorios.Count == 0 && problemas.Cuantos == 0)
            {
                problemas.Agregar("El archivo", "no tiene ninguna hoja con un horario: hace falta la fila de los días (Lunes, Martes...) y la columna de las horas.");
            }

            lectura.Clases = clases.Values.ToList();
            if (problemas.Cuantos == 0)
            {
                Revisar(lectura, catalogo, leido, problemas);
            }

            lectura.Problemas = problemas.Lista();
            Resumir(lectura, leido);
            return lectura;
        }

        /// <summary>La fila con los nombres de los dias (dos o mas), y en que columna va cada uno.</summary>
        private static bool BuscarDias(HojaLeida hoja, out int filaDias, out Dictionary<int, int> columnas, out string error)
        {
            filaDias = 0;
            columnas = null;
            error = null;
            for (int fila = 1; fila <= Math.Min(hoja.UltimaFila, 40); fila++)
            {
                Dictionary<int, int> encontrados = new Dictionary<int, int>();
                Dictionary<int, int> repetidos = new Dictionary<int, int>();
                for (int columna = 1; columna <= hoja.UltimaColumna; columna++)
                {
                    int dia = Dia(hoja.Texto(fila, columna));
                    if (dia == 0)
                    {
                        continue;
                    }

                    if (encontrados.ContainsValue(dia))
                    {
                        repetidos[dia] = columna;
                    }

                    encontrados[columna] = dia;
                }

                if (encontrados.Count >= 2)
                {
                    if (repetidos.Count > 0)
                    {
                        int dia = repetidos.Keys.First();
                        error = "tiene dos columnas «" + HorariosDatos.NombresDia[dia] + "» (fila " + fila + "): cada día va en una sola.";
                        return false;
                    }

                    filaDias = fila;
                    columnas = encontrados;
                    return true;
                }
            }

            return false;
        }

        private static int Dia(string texto)
        {
            string n = Normal(texto);
            for (int d = 1; d <= 7; d++)
            {
                if (n == Dias[d] || n == DiasCortos[d])
                {
                    return d;
                }
            }

            return 0;
        }

        /// <summary>Los renglones con hora debajo de la fila de los dias; null (y el problema) si no se entienden.</summary>
        private static List<Renglon> LeerHoras(HojaLeida hoja, int filaDias, Dictionary<int, int> columnasDia, string lugar, Problemas problemas)
        {
            int primeraDia = columnasDia.Keys.Min();
            int columnaHora = 0;
            for (int c = 1; c <= hoja.UltimaColumna; c++)
            {
                if (!columnasDia.ContainsKey(c) && Normal(hoja.Texto(filaDias, c)).StartsWith("hora", StringComparison.Ordinal))
                {
                    columnaHora = c;
                    break;
                }
            }

            if (columnaHora == 0 && primeraDia > 1)
            {
                columnaHora = primeraDia - 1;
            }

            if (columnaHora == 0)
            {
                problemas.Agregar(lugar, "falta la columna de las horas, a la izquierda de los días.");
                return null;
            }

            List<Renglon> renglones = new List<Renglon>();
            for (int fila = filaDias + 1; fila <= hoja.UltimaFila; fila++)
            {
                TimeSpan inicio;
                TimeSpan? fin;
                string texto = hoja.Texto(fila, columnaHora).Trim();
                bool conHora = LeerHora(texto, hoja.Numero(fila, columnaHora), out inicio, out fin);
                if (!conHora)
                {
                    // Sin hora no puede tener clases propias (las de una combinada de arriba si);
                    // sin clases se salta (un "RECESO", una nota o una firma abajo).
                    bool conClase = columnasDia.Keys.Any(c => hoja.CombinadaDe(fila, c) == null && !EsLibre(hoja.Texto(fila, c)));
                    if (conClase)
                    {
                        problemas.Agregar(lugar + ", fila " + fila, texto.Length > 0
                            ? "no se entiende la hora «" + texto + "» (escríbela como «07:00 - 08:00»)."
                            : "tiene clases pero no su hora en la columna de las horas.");
                    }

                    continue;
                }

                renglones.Add(new Renglon { Fila = fila, Inicio = inicio, Fin = fin });
            }

            if (renglones.Count == 0)
            {
                problemas.Agregar(lugar, "no tiene ninguna hora en la columna de las horas (como «07:00 - 08:00»).");
                return null;
            }

            // Sin fin: hasta el siguiente renglon; el ultimo, lo que duro el de antes (o una hora).
            for (int i = 0; i < renglones.Count; i++)
            {
                if (renglones[i].Fin.HasValue)
                {
                    continue;
                }

                if (i + 1 < renglones.Count && renglones[i + 1].Inicio > renglones[i].Inicio)
                {
                    renglones[i].Fin = renglones[i + 1].Inicio;
                }
                else
                {
                    TimeSpan dura = i > 0 && renglones[i - 1].Fin.HasValue ? renglones[i - 1].Fin.Value - renglones[i - 1].Inicio : TimeSpan.FromHours(1);
                    renglones[i].Fin = renglones[i].Inicio + (dura > TimeSpan.Zero ? dura : TimeSpan.FromHours(1));
                }
            }

            for (int i = 0; i < renglones.Count; i++)
            {
                Renglon r = renglones[i];
                if (r.Fin.Value > TimeSpan.FromHours(24))
                {
                    r.Fin = TimeSpan.FromHours(24);
                }

                if (r.Fin.Value <= r.Inicio)
                {
                    problemas.Agregar(lugar + ", fila " + r.Fila, "la hora de fin (" + Hora(r.Fin.Value) + ") no es después de la de inicio (" + Hora(r.Inicio) + ").");
                    return null;
                }

                if (i > 0 && r.Inicio < renglones[i - 1].Fin.Value)
                {
                    problemas.Agregar(lugar + ", fila " + r.Fila, "las horas no van en orden: empieza a las " + Hora(r.Inicio) + " y el renglón de arriba termina a las "
                        + Hora(renglones[i - 1].Fin.Value) + ".");
                    return null;
                }
            }

            return renglones;
        }

        private static string Hora(TimeSpan hora)
        {
            return hora.TotalHours >= 24 ? "24:00" : HorariosDatos.Hora(hora);
        }

        /// <summary>"07:00 - 08:00", "7:00-8:00", "07:00", "7 - 8", "1:00 pm" o una hora de Excel (fraccion del dia).</summary>
        internal static bool LeerHora(string texto, double? numero, out TimeSpan inicio, out TimeSpan? fin)
        {
            inicio = TimeSpan.Zero;
            fin = null;
            if (numero.HasValue)
            {
                double v = numero.Value;
                if (v >= 0 && v < 1)
                {
                    inicio = TimeSpan.FromMinutes(Math.Round(v * 24 * 60));
                    return inicio < TimeSpan.FromHours(24);
                }

                if (v >= 1 && v <= 23 && Math.Abs(v - Math.Round(v)) < 1e-9)
                {
                    inicio = TimeSpan.FromHours(Math.Round(v));
                    return true;
                }

                double fraccion = v - Math.Floor(v);
                if (v > 1000 && fraccion > 0)
                {
                    inicio = TimeSpan.FromMinutes(Math.Round(fraccion * 24 * 60));
                    return true;
                }

                return false;
            }

            if (string.IsNullOrWhiteSpace(texto))
            {
                return false;
            }

            List<TimeSpan> horas = new List<TimeSpan>();
            foreach (Match m in Horas.Matches(texto))
            {
                int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                string ampm = m.Groups[3].Success ? m.Groups[3].Value.ToLowerInvariant() : "";
                if (ampm.StartsWith("p", StringComparison.Ordinal) && h < 12)
                {
                    h += 12;
                }
                else if (ampm.StartsWith("a", StringComparison.Ordinal) && h == 12)
                {
                    h = 0;
                }

                if (h > 24 || min > 59 || (h == 24 && min > 0))
                {
                    return false;
                }

                horas.Add(new TimeSpan(h, min, 0));
            }

            if (horas.Count == 0)
            {
                Match m = HorasSueltas.Match(texto);
                if (!m.Success)
                {
                    return false;
                }

                int h1 = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (h1 > 23)
                {
                    return false;
                }

                horas.Add(TimeSpan.FromHours(h1));
                if (m.Groups[2].Success)
                {
                    int h2 = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (h2 > 24)
                    {
                        return false;
                    }

                    horas.Add(TimeSpan.FromHours(h2));
                }
            }

            inicio = horas[0];
            if (inicio >= TimeSpan.FromHours(24))
            {
                return false;
            }

            if (horas.Count > 1)
            {
                fin = horas[1];
            }

            return true;
        }

        private static bool EsLibre(string texto)
        {
            string t = (texto ?? "").Trim();
            return t.Length == 0 || t.All(c => c == '-' || c == '–' || c == '—' || c == '.' || char.IsWhiteSpace(c)) || Libres.Contains(Normal(t));
        }

        /// <summary>Las clases de una hoja: cada casilla entendida, y las seguidas de la misma clase en una sola franja.</summary>
        private static void LeerClases(HojaLeida hoja, CatalogosDeHorario.Laboratorio laboratorio, string lugar, List<Renglon> renglones,
            Dictionary<int, int> columnasDia, Catalogo catalogo, Problemas problemas, Dictionary<string, ClaseDelExcel> clases)
        {
            foreach (KeyValuePair<int, int> columna in columnasDia.OrderBy(c => c.Value))
            {
                int dia = columna.Value;
                Celda anterior = null;
                FranjaCiclo abierta = null;
                foreach (Renglon r in renglones)
                {
                    string texto = hoja.TextoVisible(r.Fila, columna.Key);
                    Celda celda = null;
                    if (!EsLibre(texto))
                    {
                        string error;
                        celda = catalogo.Entender(texto, out error);
                        if (celda == null)
                        {
                            problemas.Agregar(lugar + ", " + HorariosDatos.NombresDia[dia].ToLowerInvariant() + " " + Hora(r.Inicio),
                                "en la casilla «" + EnUnaLinea(texto) + "», " + error, texto);
                        }
                    }

                    if (celda != null && anterior != null && abierta != null && celda.Clave == anterior.Clave && abierta.Fin == r.Inicio)
                    {
                        abierta.Fin = r.Fin.Value;
                        continue;
                    }

                    anterior = celda;
                    abierta = null;
                    if (celda == null)
                    {
                        continue;
                    }

                    ClaseDelExcel clase;
                    if (!clases.TryGetValue(celda.Clave, out clase))
                    {
                        clase = new ClaseDelExcel
                        {
                            ProfesorId = celda.Profesor,
                            GrupoId = celda.Grupo,
                            MateriaId = celda.Materia,
                            Nombre = catalogo.NombreDeClase(celda)
                        };
                        clases[celda.Clave] = clase;
                    }

                    abierta = new FranjaCiclo
                    {
                        LaboratorioId = laboratorio.Id,
                        Laboratorio = laboratorio.Nombre,
                        LaboratorioActivo = laboratorio.Activo,
                        DiaSemana = dia,
                        Inicio = r.Inicio,
                        Fin = r.Fin.Value
                    };
                    clase.Franjas.Add(abierta);
                }
            }

            // Hasta las 24:00 se guarda hasta las 23:59, como al poner una clase en la cuadricula.
            foreach (FranjaCiclo f in clases.Values.SelectMany(c => c.Franjas).Where(f => f.LaboratorioId == laboratorio.Id && f.Fin >= TimeSpan.FromHours(24)))
            {
                f.Fin = new TimeSpan(23, 59, 0);
            }
        }

        /// <summary>Lo que dice una casilla en un renglon ("Base de datos / 5ISC11 / ..."), corto.</summary>
        private static string EnUnaLinea(string texto)
        {
            string linea = string.Join(" / ", (texto ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()));
            return linea.Length > 90 ? linea.Substring(0, 87) + "..." : linea;
        }

        /// <summary>
        /// Lo que se ve con todo el Excel junto: la misma clase en dos laboratorios a la vez
        /// (o a la vez con sus horas de otro laboratorio del horario) no se puede; los choques
        /// de docente o grupo si, pero impiden aplicar (se avisa).
        /// </summary>
        private static void Revisar(LecturaDeHorario lectura, Catalogo catalogo, CicloLeido leido, Problemas problemas)
        {
            HashSet<int> labs = new HashSet<int>(lectura.Laboratorios.Select(l => l.Id));
            // Las horas que se quedan: las de los laboratorios que no vienen en el Excel.
            List<KeyValuePair<CargaCiclo, FranjaCiclo>> quedan = leido == null ? new List<KeyValuePair<CargaCiclo, FranjaCiclo>>()
                : leido.Cargas.Where(c => !c.Quitada)
                    .SelectMany(c => c.Franjas.Where(f => !labs.Contains(f.LaboratorioId)).Select(f => new KeyValuePair<CargaCiclo, FranjaCiclo>(c, f)))
                    .ToList();

            foreach (ClaseDelExcel clase in lectura.Clases)
            {
                for (int i = 0; i < clase.Franjas.Count; i++)
                {
                    for (int j = i + 1; j < clase.Franjas.Count; j++)
                    {
                        FranjaCiclo a = clase.Franjas[i];
                        FranjaCiclo b = clase.Franjas[j];
                        if (SeCruzan(a, b))
                        {
                            problemas.Agregar(Cuando(a), clase.Nombre + " está a la vez en " + EstiloHorarios.NombreLaboratorio(a.Laboratorio)
                                + " y en " + EstiloHorarios.NombreLaboratorio(b.Laboratorio) + ": una clase no puede estar en dos sitios.");
                        }
                    }
                }

                foreach (KeyValuePair<CargaCiclo, FranjaCiclo> otra in quedan.Where(q => Misma(q.Key, clase)))
                {
                    foreach (FranjaCiclo f in clase.Franjas.Where(f => SeCruzan(f, otra.Value)))
                    {
                        problemas.Agregar(Cuando(f), clase.Nombre + " ya tiene esa hora en " + EstiloHorarios.NombreLaboratorio(otra.Value.Laboratorio)
                            + " (que no viene en el Excel): quítasela ahí o aquí.");
                    }
                }
            }

            // Choques: la misma persona docente o el mismo grupo en dos clases a la vez.
            List<Tuple<int?, int, string, FranjaCiclo>> conQuien = new List<Tuple<int?, int, string, FranjaCiclo>>();
            foreach (ClaseDelExcel clase in lectura.Clases)
            {
                foreach (FranjaCiclo f in clase.Franjas)
                {
                    conQuien.Add(Tuple.Create(clase.ProfesorId, clase.GrupoId, clase.Nombre, f));
                }
            }

            int delExcel = conQuien.Count;
            foreach (KeyValuePair<CargaCiclo, FranjaCiclo> q in quedan)
            {
                conQuien.Add(Tuple.Create(q.Key.ProfesorId, q.Key.GrupoId, catalogo.NombreDeCarga(q.Key), q.Value));
            }

            HashSet<string> dichos = new HashSet<string>();
            for (int i = 0; i < delExcel; i++)
            {
                for (int j = i + 1; j < conQuien.Count; j++)
                {
                    Tuple<int?, int, string, FranjaCiclo> a = conQuien[i];
                    Tuple<int?, int, string, FranjaCiclo> b = conQuien[j];
                    if (a.Item3 == b.Item3 || !SeCruzan(a.Item4, b.Item4) || a.Item4.LaboratorioId == b.Item4.LaboratorioId && j < delExcel)
                    {
                        continue;
                    }

                    List<string> motivos = new List<string>();
                    if (a.Item1.HasValue && a.Item1 == b.Item1)
                    {
                        motivos.Add("la misma persona docente");
                    }

                    if (a.Item2 == b.Item2)
                    {
                        motivos.Add("el mismo grupo");
                    }

                    if (motivos.Count == 0)
                    {
                        continue;
                    }

                    string texto = Cuando(a.Item4) + ": " + a.Item3 + " (" + EstiloHorarios.NombreLaboratorio(a.Item4.Laboratorio) + ") y "
                        + b.Item3 + " (" + EstiloHorarios.NombreLaboratorio(b.Item4.Laboratorio) + ") tienen " + string.Join(" y ", motivos) + ".";
                    if (dichos.Add(texto))
                    {
                        lectura.Avisos.Add("Choque: " + texto);
                    }
                }
            }
        }

        private static bool Misma(CargaCiclo carga, ClaseDelExcel clase)
        {
            return carga.ProfesorId == clase.ProfesorId && carga.GrupoId == clase.GrupoId && carga.MateriaId == clase.MateriaId;
        }

        private static bool SeCruzan(FranjaCiclo a, FranjaCiclo b)
        {
            return a.DiaSemana == b.DiaSemana && a.Inicio < b.Fin && a.Fin > b.Inicio;
        }

        private static string Cuando(FranjaCiclo f)
        {
            return HorariosDatos.NombresDia[f.DiaSemana] + " " + Hora(f.Inicio) + "–" + Hora(f.Fin);
        }

        /// <summary>Lo que trae cada laboratorio, lo que tiene ahora y que clases se quitan.</summary>
        private static void Resumir(LecturaDeHorario lectura, CicloLeido leido)
        {
            HashSet<int> labs = new HashSet<int>(lectura.Laboratorios.Select(l => l.Id));
            List<CargaCiclo> cargas = leido == null ? new List<CargaCiclo>() : leido.Cargas.Where(c => !c.Quitada).ToList();
            foreach (LaboratorioDelExcel lab in lectura.Laboratorios)
            {
                lab.Clases = lectura.Clases.Count(c => c.Franjas.Any(f => f.LaboratorioId == lab.Id));
                lab.Minutos = lectura.Clases.SelectMany(c => c.Franjas).Where(f => f.LaboratorioId == lab.Id).Sum(f => HorariosDatos.MinutosEntre(f.Inicio, f.Fin));
                lab.ClasesAntes = cargas.Count(c => c.Franjas.Any(f => f.LaboratorioId == lab.Id));
                lab.MinutosAntes = cargas.SelectMany(c => c.Franjas).Where(f => f.LaboratorioId == lab.Id).Sum(f => HorariosDatos.MinutosEntre(f.Inicio, f.Fin));
            }

            lectura.SeQuitan = cargas
                .Where(c => c.Franjas.Count > 0 && c.Franjas.All(f => labs.Contains(f.LaboratorioId)) && !lectura.Clases.Any(x => Misma(c, x)))
                .Select(c => c.Materia + " con " + c.Grupo + " (" + (c.ProfesorId.HasValue ? c.ProfesorCorto : SinDocente.ToLowerInvariant()) + ")")
                .ToList();
        }

        // =====================================================================
        // Lo registrado, para entender cada casilla
        // =====================================================================

        /// <summary>
        /// "Base de Datos " y "base de datos" son lo mismo: sin acentos, en minusculas, sin
        /// signos y con un solo espacio entre palabras.
        /// </summary>
        internal static string Normal(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return "";
            }

            string separado = texto.Normalize(NormalizationForm.FormD);
            StringBuilder limpio = new StringBuilder(separado.Length);
            bool espacio = false;
            foreach (char c in separado)
            {
                UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    if (espacio && limpio.Length > 0)
                    {
                        limpio.Append(' ');
                    }

                    limpio.Append(char.ToLowerInvariant(c));
                    espacio = false;
                }
                else
                {
                    espacio = true;
                }
            }

            return limpio.ToString();
        }

        /// <summary>Sin espacios: "5ISC-11" y "5isc 11" son el mismo grupo.</summary>
        internal static string Compacto(string texto)
        {
            return Normal(texto).Replace(" ", "");
        }

        /// <summary>Cuantos cambios de letra hay de uno a otro (para "¿es ...?").</summary>
        private static int Distancia(string a, string b)
        {
            int[] previa = new int[b.Length + 1];
            int[] actual = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
            {
                previa[j] = j;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                actual[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, previa[j] + 1), previa[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                }

                int[] t = previa;
                previa = actual;
                actual = t;
            }

            return previa[b.Length];
        }

        /// <summary>Los problemas, sin repetir el mismo en cada casilla de una clase: "(y 3 casillas más)".</summary>
        private sealed class Problemas
        {
            private readonly List<string> orden = new List<string>();
            private readonly Dictionary<string, string> primero = new Dictionary<string, string>();
            private readonly Dictionary<string, int> veces = new Dictionary<string, int>();

            public int Cuantos
            {
                get { return orden.Count; }
            }

            /// <param name="agrupar">Lo que hace que dos sean el mismo (el texto de la casilla); null: el mensaje.</param>
            public void Agregar(string donde, string que, string agrupar = null)
            {
                string clave = (donde.Split(',')[0]) + "\u0001" + que + "\u0001" + (agrupar ?? "");
                int n;
                if (veces.TryGetValue(clave, out n))
                {
                    veces[clave] = n + 1;
                    return;
                }

                orden.Add(clave);
                primero[clave] = donde;
                veces[clave] = 1;
            }

            public List<string> Lista()
            {
                return orden.Select(clave =>
                {
                    string que = clave.Split('\u0001')[1];
                    int n = veces[clave];
                    return primero[clave] + (n > 1 ? " (y " + (n - 1) + (n == 2 ? " casilla más" : " casillas más") + ")" : "") + ": " + que;
                }).ToList();
            }
        }

        private sealed class Catalogo
        {
            private readonly CatalogosDeHorario catalogos;
            private readonly CicloLeido leido;
            private readonly Dictionary<string, List<CatalogosDeHorario.Grupo>> grupos = new Dictionary<string, List<CatalogosDeHorario.Grupo>>();
            private readonly Dictionary<string, List<CatalogosDeHorario.Materia>> materias = new Dictionary<string, List<CatalogosDeHorario.Materia>>();
            private readonly Dictionary<int, List<string>> palabras = new Dictionary<int, List<string>>();
            private readonly Dictionary<int, CatalogosDeHorario.Docente> docentes = new Dictionary<int, CatalogosDeHorario.Docente>();

            /// <summary>El papel de un pedazo de una casilla.</summary>
            private enum Papel
            {
                Materia,
                Grupo,
                Docente
            }

            private sealed class Pedazo
            {
                public string Texto;
                public List<CatalogosDeHorario.Materia> Materias = new List<CatalogosDeHorario.Materia>();
                public List<CatalogosDeHorario.Grupo> Grupos = new List<CatalogosDeHorario.Grupo>();
                public List<CatalogosDeHorario.Docente> Docentes = new List<CatalogosDeHorario.Docente>();
                public bool SinDocente;

                public List<Papel> Papeles
                {
                    get
                    {
                        List<Papel> papeles = new List<Papel>();
                        if (Materias.Count > 0)
                        {
                            papeles.Add(Papel.Materia);
                        }

                        if (Grupos.Count > 0)
                        {
                            papeles.Add(Papel.Grupo);
                        }

                        if (Docentes.Count > 0 || SinDocente)
                        {
                            papeles.Add(Papel.Docente);
                        }

                        return papeles;
                    }
                }
            }

            public Catalogo(CatalogosDeHorario catalogos, CicloLeido leido)
            {
                this.catalogos = catalogos;
                this.leido = leido;
                foreach (CatalogosDeHorario.Grupo g in catalogos.Grupos)
                {
                    Agregar(grupos, Compacto(g.Nombre), g);
                }

                foreach (CatalogosDeHorario.Materia m in catalogos.Materias)
                {
                    Agregar(materias, Normal(m.Nombre), m);
                }

                foreach (CatalogosDeHorario.Docente d in catalogos.Docentes)
                {
                    docentes[d.Id] = d;
                    palabras[d.Id] = Normal(d.Completo).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }

            private static void Agregar<T>(Dictionary<string, List<T>> indice, string clave, T valor)
            {
                if (clave.Length == 0)
                {
                    return;
                }

                List<T> lista;
                if (!indice.TryGetValue(clave, out lista))
                {
                    lista = new List<T>();
                    indice[clave] = lista;
                }

                lista.Add(valor);
            }

            /// <summary>El laboratorio de una hoja: por su nombre o por un titulo arriba de los dias ("Laboratorio CISCO").</summary>
            public CatalogosDeHorario.Laboratorio Laboratorio(HojaLeida hoja, int hastaFila)
            {
                CatalogosDeHorario.Laboratorio porNombre = LaboratorioDe(hoja.Nombre);
                if (porNombre != null)
                {
                    return porNombre;
                }

                for (int fila = 1; fila < hastaFila; fila++)
                {
                    for (int columna = 1; columna <= Math.Min(hoja.UltimaColumna, 12); columna++)
                    {
                        CatalogosDeHorario.Laboratorio l = LaboratorioDe(hoja.Texto(fila, columna));
                        if (l != null)
                        {
                            return l;
                        }
                    }
                }

                return null;
            }

            private static string SinPalabraLaboratorio(string texto)
            {
                string n = Normal(texto);
                foreach (string prefijo in new[] { "laboratorio de ", "laboratorio ", "lab de ", "lab " })
                {
                    if (n.StartsWith(prefijo, StringComparison.Ordinal))
                    {
                        n = n.Substring(prefijo.Length);
                        break;
                    }
                }

                // "(deshabilitado)" que pone la plantilla.
                if (n.EndsWith(" deshabilitado", StringComparison.Ordinal))
                {
                    n = n.Substring(0, n.Length - " deshabilitado".Length);
                }

                return n.Replace(" ", "");
            }

            private CatalogosDeHorario.Laboratorio LaboratorioDe(string texto)
            {
                string buscado = SinPalabraLaboratorio(texto);
                if (buscado.Length == 0)
                {
                    return null;
                }

                List<CatalogosDeHorario.Laboratorio> iguales = catalogos.Laboratorios.Where(l => SinPalabraLaboratorio(l.Nombre) == buscado).ToList();
                if (iguales.Count == 1)
                {
                    return iguales[0];
                }

                // El nombre de una hoja se corta en 31 letras.
                if (buscado.Length >= 20)
                {
                    List<CatalogosDeHorario.Laboratorio> empiezan = catalogos.Laboratorios.Where(l => SinPalabraLaboratorio(l.Nombre).StartsWith(buscado, StringComparison.Ordinal)).ToList();
                    if (empiezan.Count == 1)
                    {
                        return empiezan[0];
                    }
                }

                return null;
            }

            public string LaboratorioParecido(string texto)
            {
                string buscado = SinPalabraLaboratorio(texto);
                CatalogosDeHorario.Laboratorio mejor = catalogos.Laboratorios
                    .OrderBy(l => Distancia(SinPalabraLaboratorio(l.Nombre), buscado))
                    .FirstOrDefault();
                return mejor != null && buscado.Length > 0 && Distancia(SinPalabraLaboratorio(mejor.Nombre), buscado) <= Math.Max(2, buscado.Length / 3) ? mejor.Nombre : null;
            }

            public string NombreDeClase(Celda celda)
            {
                CatalogosDeHorario.Materia m = catalogos.Materias.First(x => x.Id == celda.Materia);
                CatalogosDeHorario.Grupo g = catalogos.Grupos.First(x => x.Id == celda.Grupo);
                return m.Nombre.Trim() + " con " + g.Nombre.Trim() + " (" + (celda.Profesor.HasValue ? Corto(docentes[celda.Profesor.Value]) : SinDocente.ToLowerInvariant()) + ")";
            }

            public string NombreDeCarga(CargaCiclo carga)
            {
                return carga.Materia + " con " + carga.Grupo + " (" + (carga.ProfesorId.HasValue ? carga.ProfesorCorto : SinDocente.ToLowerInvariant()) + ")";
            }

            /// <summary>"Marco Llinas" si con eso se reconoce solo a esa persona (tambien entre las de baja); si no, el nombre completo.</summary>
            public string NombreParaLista(CatalogosDeHorario.Docente d)
            {
                string corto = Corto(d);
                List<CatalogosDeHorario.Docente> casan = Reconocer(corto).Docentes;
                return casan.Count == 1 && casan[0].Id == d.Id ? corto : d.Completo;
            }

            private static string Corto(CatalogosDeHorario.Docente d)
            {
                string nombre = (d.Nombre ?? "").Trim().Split(' ').FirstOrDefault() ?? "";
                return (nombre + " " + (d.ApellidoPaterno ?? "").Trim()).Trim();
            }

            /// <summary>Una casilla con clase: su materia, grupo y docente registrados, o null y por que.</summary>
            public Celda Entender(string texto, out string error)
            {
                error = null;
                List<string> partes = Partes(texto);
                List<Pedazo> pedazos;
                if (partes.Count == 1)
                {
                    pedazos = EnUnRenglon(partes[0]);
                    if (pedazos == null)
                    {
                        error = SinEntender(partes[0]);
                        return null;
                    }
                }
                else
                {
                    pedazos = partes.Select(Reconocer).ToList();
                }

                List<Pedazo> sinPapel = pedazos.Where(p => p.Papeles.Count == 0).ToList();
                if (sinPapel.Count > 0)
                {
                    error = string.Join("; ", sinPapel.Select(p => Desconocido(p.Texto)));
                    error += error.EndsWith("?", StringComparison.Ordinal) ? "" : ".";
                    return null;
                }

                // A cada pedazo un papel: una materia, un grupo y una persona docente.
                Papel[] elegidos = Repartir(pedazos);
                if (elegidos == null)
                {
                    error = PorQueNoSeReparte(pedazos);
                    return null;
                }

                Pedazo pm = pedazos[Array.IndexOf(elegidos, Papel.Materia)];
                Pedazo pg = pedazos[Array.IndexOf(elegidos, Papel.Grupo)];
                int iDocente = Array.IndexOf(elegidos, Papel.Docente);
                if (iDocente < 0)
                {
                    error = "falta la persona docente (si todavía no se sabe, escribe «" + SinDocente + "»).";
                    return null;
                }

                Pedazo pd = pedazos[iDocente];
                CatalogosDeHorario.Docente docente = null;
                if (!pd.SinDocente)
                {
                    List<CatalogosDeHorario.Docente> exactos = pd.Docentes.Where(d => palabras[d.Id].Count == Palabras(pd.Texto).Count).ToList();
                    List<CatalogosDeHorario.Docente> posibles = exactos.Count == 1 ? exactos : pd.Docentes;
                    if (posibles.Count > 1)
                    {
                        error = "«" + pd.Texto.Trim() + "» puede ser " + string.Join(" o ", posibles.Take(4).Select(d => d.Completo))
                            + ": escribe su nombre completo o su número.";
                        return null;
                    }

                    docente = posibles[0];
                    if (!docente.Activo)
                    {
                        error = docente.Completo + " está dado de baja en DigLab.";
                        return null;
                    }
                }

                // Un grupo y una materia pueden repetirse en otra carrera: el de la misma carrera que el otro.
                List<CatalogosDeHorario.Grupo> gs = pg.Grupos;
                List<CatalogosDeHorario.Materia> ms = pm.Materias;
                if (gs.Count > 1)
                {
                    List<CatalogosDeHorario.Grupo> misma = gs.Where(g => ms.Any(m => m.CarreraId.HasValue && m.CarreraId == g.CarreraId)).ToList();
                    if (misma.Count == 1)
                    {
                        gs = misma;
                    }
                }

                if (gs.Count > 1)
                {
                    error = "hay " + gs.Count + " grupos «" + pg.Texto.Trim() + "» en DigLab (" + string.Join(", ", gs.Select(g => string.IsNullOrEmpty(g.Carrera) ? "sin carrera" : g.Carrera))
                        + "): cámbiale el nombre a uno en DigLab.";
                    return null;
                }

                CatalogosDeHorario.Grupo grupo = gs[0];
                CatalogosDeHorario.Materia materia = ElegirMateria(ms, grupo, docente);
                Celda celda = new Celda
                {
                    Profesor = docente == null ? (int?)null : docente.Id,
                    Grupo = grupo.Id,
                    Materia = materia.Id
                };
                celda.Clave = (celda.Profesor.HasValue ? celda.Profesor.Value.ToString(CultureInfo.InvariantCulture) : "-") + "|" + celda.Grupo + "|" + celda.Materia;
                return celda;
            }

            /// <summary>Con varias materias del mismo nombre: la que ya tiene el horario con ese grupo, la de su carrera o la primera.</summary>
            private CatalogosDeHorario.Materia ElegirMateria(List<CatalogosDeHorario.Materia> ms, CatalogosDeHorario.Grupo grupo, CatalogosDeHorario.Docente docente)
            {
                if (ms.Count == 1)
                {
                    return ms[0];
                }

                if (leido != null)
                {
                    CargaCiclo ya = leido.Cargas.FirstOrDefault(c => !c.Quitada && c.GrupoId == grupo.Id && ms.Any(m => m.Id == c.MateriaId)
                        && (docente == null ? !c.ProfesorId.HasValue : c.ProfesorId == docente.Id))
                        ?? leido.Cargas.FirstOrDefault(c => !c.Quitada && c.GrupoId == grupo.Id && ms.Any(m => m.Id == c.MateriaId));
                    if (ya != null)
                    {
                        return ms.First(m => m.Id == ya.MateriaId);
                    }
                }

                return ms.FirstOrDefault(m => grupo.CarreraId.HasValue && m.CarreraId == grupo.CarreraId) ?? ms.OrderBy(m => m.Id).First();
            }

            /// <summary>Los renglones de la casilla; en uno solo, separados por "/", "|", ";" o " - ".</summary>
            private static List<string> Partes(string texto)
            {
                List<string> renglones = (texto ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                    .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                if (renglones.Count != 1)
                {
                    return renglones;
                }

                // "·" primero: es el de la lista desplegable (una materia podria llevar "/").
                foreach (string separador in new[] { "·", "/", "|", ";", " - ", " – ", " — " })
                {
                    List<string> partes = renglones[0].Split(new[] { separador }, StringSplitOptions.None).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                    if (partes.Count >= 2)
                    {
                        return partes;
                    }
                }

                return renglones;
            }

            private static List<string> Palabras(string texto)
            {
                List<string> palabras = Normal(texto).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                // Sin el titulo de adelante: "Ing.", "Mtra.", "M. en C."...
                while (palabras.Count > 2)
                {
                    if (Titulos.Contains(palabras[0]))
                    {
                        palabras.RemoveAt(0);
                    }
                    else if (palabras.Count > 4 && palabras[0] == "m" && palabras[1] == "en" && palabras[2].Length <= 2)
                    {
                        palabras.RemoveRange(0, 3);
                    }
                    else
                    {
                        break;
                    }
                }

                return palabras;
            }

            /// <summary>Lo que puede ser un pedazo: materias, grupos o docentes registrados con ese nombre.</summary>
            private Pedazo Reconocer(string texto)
            {
                Pedazo p = new Pedazo { Texto = texto };
                List<CatalogosDeHorario.Materia> ms;
                if (materias.TryGetValue(Normal(texto), out ms))
                {
                    p.Materias = ms;
                }

                List<CatalogosDeHorario.Grupo> gs;
                if (grupos.TryGetValue(Compacto(texto), out gs))
                {
                    p.Grupos = gs;
                }

                string n = Normal(texto);
                if (SinDocenteTextos.Contains(n))
                {
                    p.SinDocente = true;
                    return p;
                }

                string compacto = Compacto(texto);
                CatalogosDeHorario.Docente porNumero = compacto.Length >= 3 ? catalogos.Docentes.FirstOrDefault(d => Compacto(d.Numero) == compacto) : null;
                if (porNumero != null)
                {
                    p.Docentes.Add(porNumero);
                    return p;
                }

                List<string> buscadas = Palabras(texto);
                if (buscadas.Count >= 2)
                {
                    foreach (CatalogosDeHorario.Docente d in catalogos.Docentes)
                    {
                        List<string> suyas = new List<string>(palabras[d.Id]);
                        if (buscadas.All(suyas.Remove))
                        {
                            p.Docentes.Add(d);
                        }
                    }
                }

                return p;
            }

            /// <summary>
            /// "Base de datos 5ISC11 Laura Cárdenas", sin separar: el grupo parte el renglon;
            /// de un lado la materia y del otro la persona docente. Null si no se entiende.
            /// </summary>
            private List<Pedazo> EnUnRenglon(string texto)
            {
                List<string> palabras = Normal(texto).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                for (int i = 0; i < palabras.Count; i++)
                {
                    for (int n = 1; n <= 3 && i + n <= palabras.Count; n++)
                    {
                        string junto = string.Concat(palabras.Skip(i).Take(n));
                        if (!grupos.ContainsKey(junto))
                        {
                            continue;
                        }

                        string antes = string.Join(" ", palabras.Take(i));
                        string despues = string.Join(" ", palabras.Skip(i + n));
                        Pedazo grupo = Reconocer(junto);
                        foreach (string[] orden in new[] { new[] { antes, despues }, new[] { despues, antes } })
                        {
                            Pedazo materia = Reconocer(orden[0]);
                            Pedazo docente = Reconocer(orden[1]);
                            if (materia.Materias.Count > 0 && (docente.Docentes.Count > 0 || docente.SinDocente))
                            {
                                materia.Grupos.Clear();
                                materia.Docentes.Clear();
                                docente.Materias.Clear();
                                docente.Grupos.Clear();
                                grupo.Materias.Clear();
                                grupo.Docentes.Clear();
                                return new List<Pedazo> { materia, grupo, docente };
                            }
                        }
                    }
                }

                return null;
            }

            private string SinEntender(string texto)
            {
                Pedazo p = Reconocer(texto);
                if (p.Papeles.Count > 0)
                {
                    return "solo dice «" + texto.Trim() + "»: escribe la materia, el grupo y la persona docente, cada uno en su renglón (Alt+Enter).";
                }

                return "no se entiende «" + texto.Trim() + "»: escribe la materia, el grupo y la persona docente registrados, cada uno en su renglón (Alt+Enter).";
            }

            /// <summary>Un papel para cada pedazo; con varias formas, la del orden materia, grupo, docente. Null si no hay ninguna.</summary>
            private static Papel[] Repartir(List<Pedazo> pedazos)
            {
                if (pedazos.Count > 3)
                {
                    return null;
                }

                List<Papel[]> validos = new List<Papel[]>();
                Probar(pedazos, 0, new Papel[pedazos.Count], validos);
                if (validos.Count == 0)
                {
                    return null;
                }

                Papel[] natural = { Papel.Materia, Papel.Grupo, Papel.Docente };
                return validos.OrderByDescending(v => v.Where((p, i) => i < natural.Length && p == natural[i]).Count()).First();
            }

            private static void Probar(List<Pedazo> pedazos, int i, Papel[] actual, List<Papel[]> validos)
            {
                if (i == pedazos.Count)
                {
                    if (actual.Count(p => p == Papel.Materia) == 1 && actual.Count(p => p == Papel.Grupo) == 1 && actual.Count(p => p == Papel.Docente) <= 1)
                    {
                        validos.Add((Papel[])actual.Clone());
                    }

                    return;
                }

                foreach (Papel papel in pedazos[i].Papeles)
                {
                    actual[i] = papel;
                    Probar(pedazos, i + 1, actual, validos);
                }
            }

            private string PorQueNoSeReparte(List<Pedazo> pedazos)
            {
                if (pedazos.Count > 3)
                {
                    return "tiene " + pedazos.Count + " renglones: una casilla lleva solo la materia, el grupo y la persona docente (una clase).";
                }

                int grupos = pedazos.Count(p => p.Papeles.Contains(Papel.Grupo));
                int materias = pedazos.Count(p => p.Papeles.Contains(Papel.Materia));
                if (grupos == 0)
                {
                    return "falta el grupo (o no está registrado en DigLab).";
                }

                if (materias == 0)
                {
                    return "falta la materia (o no está registrada en DigLab).";
                }

                if (grupos > 1 && pedazos.Count(p => p.Papeles.Count == 1 && p.Papeles[0] == Papel.Grupo) > 1)
                {
                    return "tiene dos grupos: una casilla es una sola clase.";
                }

                return "no se entiende: escribe la materia, el grupo y la persona docente, cada uno en su renglón (Alt+Enter).";
            }

            /// <summary>"«Base de dato»: no está registrada ninguna materia, grupo ni persona docente con ese nombre (¿es «Base de datos»?)".</summary>
            private string Desconocido(string texto)
            {
                string t = texto.Trim();
                string n = Normal(t);
                string sugerencia = null;
                int mejor = int.MaxValue;
                Action<string> probar = candidato =>
                {
                    int d = Distancia(Normal(candidato), n);
                    if (d < mejor)
                    {
                        mejor = d;
                        sugerencia = candidato;
                    }
                };
                foreach (CatalogosDeHorario.Materia m in catalogos.Materias)
                {
                    probar(m.Nombre.Trim());
                }

                foreach (CatalogosDeHorario.Grupo g in catalogos.Grupos)
                {
                    probar(g.Nombre.Trim());
                }

                foreach (CatalogosDeHorario.Docente d in catalogos.Docentes.Where(x => x.Activo))
                {
                    probar(d.Completo);
                }

                bool parecido = sugerencia != null && n.Length > 0 && mejor <= Math.Max(2, n.Length / 4);
                return "«" + t + "» no está registrado en DigLab (ni materia, ni grupo, ni persona docente)" + (parecido ? " — ¿es «" + sugerencia + "»?" : "");
            }
        }
    }
}
