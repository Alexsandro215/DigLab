<#
.SYNOPSIS
    Prueba los periodos de Estadisticas (2026-10-31): "Elegir fechas" con cualquier dia y
    "Hasta hoy", los ciclos que ya terminaron en el combo Periodo (EstadisticasDatos.ArmarCiclos)
    y los textos del periodo (la linea de abajo, el reporte para auditoria y el CSV), contra una
    base LOCAL de pruebas (127.0.0.1, puerto 3317 o 3318). Nunca contra la de la escuela.

.DESCRIPTION
    Carga DigLab.exe y MySql.Data.dll por reflexion, sin ensenar ninguna ventana. Nunca escribe
    nombres ni matriculas en la salida: van como huella (8 letras de su SHA-256).

      -Modo Foto -Salida <archivo> [-Comparar <otro>]
            Solo lee. Vuelca lo que calcula Estadisticas con "Este ciclo", el mes, la semana,
            los 30 dias y cinco fechas (de la semana, de septiembre, de julio, de 400 dias y
            las de "Este ciclo"), leidos desde cero y desde la cache, con todos los filtros en
            "Todos", cada laboratorio, 3 docentes y 3 grupos (y un docente que no existe). Con
            -Comparar, dice en que difiere de otro volcado (p. ej. del DigLab de antes): solo
            lo que existe en los dos.
      -Modo Ciclos [-Tsv <carpeta>]
            Sin base: la regla de los ciclos con los casos de la especificacion (y los c_*.tsv
            de -Tsv) y las funciones de texto. Con -CadenaConexion, tambien lo que dice la base.
      -Modo Copia -Destino teschi_cp_x [-Dias 364] [-SoloCorrer] [-Borrar]
            ESCRIBE (solo con el visto bueno del usuario): copia la base de -CadenaConexion en
            teschi_cp_x, en el mismo servidor, y la corre -Dias dias atras (multiplo de 7, o 0: cada
            fecha, DATE, DATETIME o TIMESTAMP; de usuarios solo baja_en). -SoloCorrer corre
            otra vez una copia que ya existe; -Borrar la borra.
      -Modo Ciclo [-Salida <resumen>] [-Comparar <resumen>]
            Solo lee (en una copia corrida, sin ciclo en curso): el ultimo ciclo pasado
            calculado como ciclo, contra Mis grupos, Faltas y la base; la cache; la lectura
            acotada contra una mas larga.
      -Modo Pdf -Salida <carpeta>
            Solo lee; escribe en esa carpeta el reporte para auditoria de un ciclo pasado, de
            "Este ciclo" y de unas fechas hasta hoy, y busca sus textos.
      -Modo Fechas
            Solo lee: fechas de hace mas de 400 dias (3318: 2024) y un "Del ... a hoy" de mas
            de 400 dias.
      -Modo Consulta
            Solo lee con la cuenta de solo consulta (digbit_consulta, db/18).

.EXAMPLE
    .\probar_periodos.ps1 -Modo Foto -Exe ..\antes\DigLab.exe -Salida antes_otru.tsv -CadenaConexion 'Database=teschi_otru;Server=127.0.0.1;Port=3317;User Id=root;Password='
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Foto', 'Ciclos', 'Copia', 'Ciclo', 'Pdf', 'Fechas', 'Consulta')]
    [string]$Modo,

    [string]$CadenaConexion = $env:DIGLAB_CONNECTION_STRING,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Otro DigLab.exe; por defecto, el de DigLab\bin\<Configuration>.
    [string]$Exe,

    [string]$Salida,
    [string]$Comparar,

    # Copia: la base nueva (o la que se corre otra vez con -SoloCorrer).
    [string]$Destino,
    [int]$Dias = 364,
    [switch]$SoloCorrer,
    [switch]$Borrar,

    # Ciclos: la carpeta con c_3317_otru.tsv, c_3317_demo.tsv y c_3318_otru.tsv (opcional).
    [string]$Tsv
)

$ErrorActionPreference = 'Stop'

function Revisar-Cadena([string]$cadena) {
    if ([string]::IsNullOrWhiteSpace($cadena)) { throw 'Falta -CadenaConexion (o DIGLAB_CONNECTION_STRING).' }
    if ($cadena -match 'teschi\.edu\.mx') { throw 'Nunca contra la base de la escuela.' }
    if ($cadena -notmatch '(?i)(^|;)\s*(Server|Host|Data Source)\s*=\s*127\.0\.0\.1\s*(;|$)' -or $cadena -notmatch '(?i)(^|;)\s*Port\s*=\s*(3317|3318)\s*(;|$)') {
        throw 'Solo las bases locales de pruebas: Server=127.0.0.1 y Port=3317 o 3318.'
    }
}

function Base-De([string]$cadena) {
    if ($cadena -match '(?i)(^|;)\s*(Database|Initial Catalog)\s*=\s*([^;]+)') { return $Matches[3].Trim() }
    throw 'La cadena no dice la base (Database=...).'
}

if ($Modo -ne 'Ciclos' -or -not [string]::IsNullOrWhiteSpace($CadenaConexion)) { Revisar-Cadena $CadenaConexion }
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "..\DigLab\bin\$Configuration\DigLab.exe" }
$Exe = (Resolve-Path $Exe).Path
$script:carpeta = Split-Path $Exe

# ---------------------------------------------------------------------------
# Copia: SHOW CREATE TABLE + INSERT ... SELECT (con FOREIGN_KEY_CHECKS = 0) y los
# disparadores; despues, cada fecha -Dias dias atras. Solo bases teschi_cp_*.
# ---------------------------------------------------------------------------
if ($Modo -eq 'Copia') {
    if ($Destino -notmatch '^teschi_cp_[a-z0-9_]+$') { throw '-Destino tiene que ser teschi_cp_<algo> (minusculas, numeros y _).' }
    if ($Dias % 7 -ne 0 -or $Dias -lt 0) { throw '-Dias tiene que ser un multiplo de 7 (las sesiones caen el mismo dia de la semana que sus franjas); 0, sin correrla.' }
    $origen = Base-De $CadenaConexion
    if (-not $SoloCorrer -and -not $Borrar -and $origen -eq $Destino) { throw 'El origen y el destino son la misma base: para correrla otra vez, -SoloCorrer.' }
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $script:carpeta 'MySql.Data.dll'))
    $sinBase = ($CadenaConexion -replace '(?i)(^|;)\s*(Database|Initial Catalog)\s*=\s*[^;]+', '$1').Trim(';') + ';AllowUserVariables=true'
    $c = New-Object MySql.Data.MySqlClient.MySqlConnection($sinBase)
    $c.Open()
    function Ejecutar([string]$sql) { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 600; return $cmd.ExecuteNonQuery() }
    function Tabla([string]$sql) { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $t = New-Object System.Data.DataTable; $r = $cmd.ExecuteReader(); $t.Load($r); $r.Close(); return , $t }
    try {
        if ($Borrar) {
            $null = Ejecutar "DROP DATABASE IF EXISTS ``$Destino``"
            Write-Host "Borrada $Destino."
            return
        }

        if (-not $SoloCorrer) {
            $info = Tabla "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = '$origen'"
            if ($info.Rows.Count -ne 1) { throw "No existe la base $origen." }
            $null = Ejecutar "DROP DATABASE IF EXISTS ``$Destino``"
            $null = Ejecutar "CREATE DATABASE ``$Destino`` CHARACTER SET $($info.Rows[0][0]) COLLATE $($info.Rows[0][1])"
            $null = Ejecutar 'SET FOREIGN_KEY_CHECKS = 0'
            $tablas = @((Tabla "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = '$origen' AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME").Rows | ForEach-Object { $_[0] })
            foreach ($t in $tablas) {
                $crear = (Tabla "SHOW CREATE TABLE ``$origen``.``$t``").Rows[0][1]
                $null = Ejecutar "USE ``$Destino``"
                $null = Ejecutar $crear
                $null = Ejecutar "INSERT INTO ``$Destino``.``$t`` SELECT * FROM ``$origen``.``$t``"
            }

            foreach ($d in @((Tabla "SELECT TRIGGER_NAME FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = '$origen'").Rows | ForEach-Object { $_[0] })) {
                $crear = (Tabla "SHOW CREATE TRIGGER ``$origen``.``$d``").Rows[0]['SQL Original Statement']
                $null = Ejecutar "USE ``$Destino``"
                $null = Ejecutar $crear
            }

            $null = Ejecutar 'SET FOREIGN_KEY_CHECKS = 1'
            Write-Host "Copia $origen -> $Destino`: $($tablas.Count) tablas."
        }
        elseif ((Tabla "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = '$Destino'").Rows[0][0] -ne 1) {
            throw "No existe la copia $Destino."
        }

        if ($Dias -eq 0) { Write-Host "$Destino sin correr (-Dias 0)."; return }

        # Cada fecha -Dias dias atras: una sentencia por tabla (todas sus fechas a la vez) en el
        # orden de 'fecha' (o de la primera): asi las llaves unicas con fecha nunca chocan.
        $null = Ejecutar "USE ``$Destino``"
        $columnas = Tabla "SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = '$Destino' AND DATA_TYPE IN ('date', 'datetime', 'timestamp') ORDER BY TABLE_NAME, ORDINAL_POSITION"
        $porTabla = [ordered]@{}
        foreach ($fila in $columnas.Rows) {
            $t = [string]$fila[0]; $col = [string]$fila[1]
            # usuarios tiene un disparador de UPDATE (trg_usuarios_admin_protegido): solo baja_en.
            if ($t -eq 'usuarios' -and $col -ne 'baja_en') { continue }
            if (-not $porTabla.Contains($t)) { $porTabla[$t] = New-Object System.Collections.Generic.List[string] }
            $porTabla[$t].Add($col)
        }

        $null = Ejecutar 'SET FOREIGN_KEY_CHECKS = 0'
        foreach ($t in $porTabla.Keys) {
            $cols = $porTabla[$t]
            $orden = $(if ($cols.Contains('fecha')) { 'fecha' } else { $cols[0] })
            if ($t -eq 'usuarios') {
                $n = Ejecutar "UPDATE ``usuarios`` SET baja_en = baja_en - INTERVAL $Dias DAY WHERE baja_en > '1000-01-01' ORDER BY baja_en"
            }
            else {
                $sets = ($cols | ForEach-Object { "``$_`` = IF(``$_`` > '1000-01-01', ``$_`` - INTERVAL $Dias DAY, ``$_``)" }) -join ', '
                $n = Ejecutar "UPDATE ``$t`` SET $sets ORDER BY ``$orden``"
            }

            Write-Host ("  {0}: {1} ({2} filas)" -f $t, ($cols -join ', '), $n)
        }

        $null = Ejecutar 'SET FOREIGN_KEY_CHECKS = 1'
        Write-Host "$Destino corrida $Dias dias atras."
    }
    finally { $c.Close() }
    return
}

# ---------------------------------------------------------------------------
# Lo demas, por reflexion desde C# (C# 5: Add-Type de Windows PowerShell).
# ---------------------------------------------------------------------------
$env:DIGLAB_CONNECTION_STRING = $CadenaConexion
$env:DIGLAB_MODO_KIOSCO = 'false'
$datos = Join-Path $env:TEMP ('diglab_probar_periodos_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $datos | Out-Null
$env:DIGLAB_CARPETA_DATOS = $datos

$codigo = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static class ProbarPeriodos
{
    private const BindingFlags TI = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringBuilder log = new StringBuilder();
    private static Assembly asm;
    private static string carpeta;
    private static string carpetaDatos;
    private static int oks, fallos;

    // -----------------------------------------------------------------------
    // Reflexion
    // -----------------------------------------------------------------------

    public static void Cargar(string exe, string datos)
    {
        carpeta = Path.GetDirectoryName(exe);
        carpetaDatos = datos;
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string ruta = Path.Combine(carpeta, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(ruta) ? Assembly.LoadFrom(ruta) : null;
        };
        asm = Assembly.LoadFrom(exe);
        T("DigLab.Infraestructura.Log").GetMethod("Configurar", TI).Invoke(null, new object[] { datos, "periodos" });
        Type du = T("DigLab.conexion.Datos_User");
        du.GetMethod("SetUser", TI).Invoke(null, new object[] { "ADMPRUEBA" });
        du.GetProperty("TipoUsuario", TI).SetValue(null, 3, null);
    }

    private static Type T(string n) { return asm.GetType(n, true); }

    private static object Get(object o, string n)
    {
        Type t = o is Type ? (Type)o : o.GetType();
        object instancia = o is Type ? null : o;
        while (t != null)
        {
            FieldInfo f = t.GetField(n, TI | BindingFlags.DeclaredOnly);
            if (f != null) { return f.GetValue(instancia); }
            PropertyInfo p = t.GetProperty(n, TI | BindingFlags.DeclaredOnly);
            if (p != null) { return p.GetValue(instancia, null); }
            t = t.BaseType;
        }
        throw new Exception("No hay " + n + " en " + (o is Type ? ((Type)o).Name : o.GetType().Name));
    }

    private static bool Tiene(object o, string n)
    {
        Type t = o is Type ? (Type)o : o.GetType();
        return t.GetField(n, TI) != null || t.GetProperty(n, TI) != null || t.GetMethods(TI).Any(m => m.Name == n);
    }

    private static void Set(object o, string n, object v)
    {
        Type t = o is Type ? (Type)o : o.GetType();
        object instancia = o is Type ? null : o;
        FieldInfo f = t.GetField(n, TI);
        if (f != null) { f.SetValue(instancia, v); return; }
        t.GetProperty(n, TI).SetValue(instancia, v, null);
    }

    private static object Call(object o, string n, params object[] a)
    {
        Type t = o is Type ? (Type)o : o.GetType();
        object instancia = o is Type ? null : o;
        while (t != null)
        {
            MethodInfo m = t.GetMethods(TI | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == n && x.GetParameters().Length == a.Length
                && x.GetParameters().Select((p, i) => a[i] == null || p.ParameterType.IsInstanceOfType(a[i])).All(b => b));
            if (m != null)
            {
                try { return m.Invoke(instancia, a); }
                catch (TargetInvocationException e) { throw e.InnerException; }
            }
            t = t.BaseType;
        }
        throw new Exception("No hay metodo " + n + "(" + a.Length + ")");
    }

    private static Type Datos { get { return T("DigLab.conexion.EstadisticasDatos"); } }
    private static Type Periodo { get { return T("DigLab.conexion.PeriodoElegido"); } }

    private static object De(string tipo) { return Periodo.GetMethod("De", TI).Invoke(null, new object[] { Enum.Parse(T("DigLab.conexion.TipoPeriodo"), tipo) }); }
    private static object EntreFechas(DateTime a, DateTime b) { return Periodo.GetMethod("EntreFechas", TI).Invoke(null, new object[] { a, b }); }
    private static object DesdeHastaHoy(DateTime a) { return Periodo.GetMethod("DesdeHastaHoy", TI).Invoke(null, new object[] { a }); }
    private static object DelCiclo(object c) { return Periodo.GetMethod("DelCiclo", TI).Invoke(null, new object[] { c }); }
    private static object Leer(object p, bool forzar) { return Call(Datos, "Leer", p, forzar); }
    private static object Resolver(object p, object l, int lab, int doc, int grp) { return Call(Datos, "Resolver", p, l, lab, doc, grp); }
    private static object Calcular(object l, object f) { return Call(Datos, "Calcular", l, f); }
    private static object Calc(object p, object l) { return Calcular(l, Resolver(p, l, 0, 0, 0)); }
    private static void Olvidar() { Call(Datos, "Olvidar"); }
    private static string S(object o) { return o == null ? "null" : o is DateTime ? ((DateTime)o).ToString("yyyy-MM-dd", Inv) : Convert.ToString(o, Inv); }
    private static string Fecha(DateTime d) { return d.ToString("dd/MM/yyyy", Inv); }
    private static DateTime D(string s) { return DateTime.ParseExact(s, "yyyy-MM-dd", Inv); }
    private static IList Lista(object o, string n) { return (IList)Get(o, n); }

    // -----------------------------------------------------------------------
    // Salida
    // -----------------------------------------------------------------------

    private static void L(string s) { log.AppendLine(s); }

    private static void Ver(string que, bool bien) { Ver(que, bien, null); }

    private static void Ver(string que, bool bien, string detalle)
    {
        if (bien) { oks++; } else { fallos++; }
        L((bien ? "[OK   ] " : "[FALLO] ") + que + (!bien && !string.IsNullOrEmpty(detalle) ? "\r\n        " + detalle : ""));
    }

    public static string Resultado()
    {
        return log.ToString() + "\r\nRESULTADO: " + oks + " bien, " + fallos + " fallos";
    }

    public static int Fallos { get { return fallos; } }

    public static string Huella(string s)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
            return BitConverter.ToString(h, 0, 4).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>Las lineas del log de DigLab de esta prueba que dicen eso.</summary>
    private static List<string> LineasDelLog(string contiene)
    {
        List<string> lineas = new List<string>();
        foreach (string archivo in Directory.GetFiles(carpetaDatos, "periodos-*.log"))
        {
            using (FileStream fs = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader r = new StreamReader(fs))
            {
                string linea;
                while ((linea = r.ReadLine()) != null)
                {
                    if (linea.Contains(contiene)) { lineas.Add(linea); }
                }
            }
        }
        return lineas;
    }

    // -----------------------------------------------------------------------
    // Volcado plano (nombres y matriculas como huella)
    // -----------------------------------------------------------------------

    private static readonly string[] Tablas = { "Estudiantes", "Clases", "Grupos", "Docentes", "Laboratorios", "Equipos" };
    private static readonly HashSet<string> Personales = new HashSet<string> { "Nombre", "Matricula", "Docente", "LoUltimo", "QueReporto" };

    private static bool EsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(DateTime) || t == typeof(TimeSpan) || t == typeof(decimal);
    }

    private static string Valor(object o)
    {
        if (o is DateTime) { return ((DateTime)o).ToString("yyyy-MM-dd HH:mm:ss", Inv); }
        if (o is double) { return ((double)o).ToString("R", Inv); }
        if (o is float) { return ((float)o).ToString("R", Inv); }
        string s = Convert.ToString(o, Inv) ?? "";
        return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static IEnumerable<MemberInfo> Miembros(Type t)
    {
        List<MemberInfo> m = new List<MemberInfo>();
        m.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.Instance));
        m.AddRange(t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead));
        return m.OrderBy(x => x.Name, StringComparer.Ordinal);
    }

    private static object ValorDe(MemberInfo m, object o)
    {
        FieldInfo f = m as FieldInfo;
        return f != null ? f.GetValue(o) : ((PropertyInfo)m).GetValue(o, null);
    }

    /// <summary>clave TAB valor por cada dato simple. Las listas de dentro de una fila (nivel 2 o mas) van como cuenta y huella.</summary>
    private static void Plano(string clave, object o, List<string> lineas, int nivel)
    {
        if (o == null) { lineas.Add(clave + "\tnull"); return; }
        Type t = o.GetType();
        if (EsSimple(t)) { lineas.Add(clave + "\t" + Valor(o)); return; }
        IDictionary dic = o as IDictionary;
        if (dic != null)
        {
            lineas.Add(clave + ".N\t" + dic.Count);
            foreach (object k in dic.Keys.Cast<object>().OrderBy(x => Convert.ToString(x, Inv), StringComparer.Ordinal))
            {
                Plano(clave + "[" + Convert.ToString(k, Inv) + "]", dic[k], lineas, nivel + 1);
            }
            return;
        }

        IEnumerable e = o as IEnumerable;
        if (e != null)
        {
            List<object> items = e.Cast<object>().ToList();
            if (items.All(x => x == null || EsSimple(x.GetType())) && !(o is IList))
            {
                items = items.OrderBy(x => Convert.ToString(x, Inv), StringComparer.Ordinal).ToList();
            }

            if (nivel >= 2)
            {
                List<string> dentro = new List<string>();
                for (int i = 0; i < items.Count; i++) { Plano("[" + i + "]", items[i], dentro, nivel + 1); }
                lineas.Add(clave + ".N\t" + items.Count);
                lineas.Add(clave + ".huella\t" + Huella(string.Join("\n", dentro)));
                return;
            }

            lineas.Add(clave + ".N\t" + items.Count);
            for (int i = 0; i < items.Count; i++) { Plano(clave + "[" + i + "]", items[i], lineas, nivel + 1); }
            return;
        }

        if (nivel > 6) { lineas.Add(clave + "\t(profundo)"); return; }
        foreach (MemberInfo m in Miembros(t))
        {
            object v = ValorDe(m, o);
            if (v is string && (Personales.Contains(m.Name) || (m.Name == "Texto" && clave.Contains("OpcionesDocente"))))
            {
                v = "#" + Huella((string)v);
            }

            Plano(clave + "." + m.Name, v, lineas, nivel + 1);
        }
    }

    /// <summary>Lo de un resultado: todo menos Duracion; la linea de abajo sin la hora; las tablas, enteras o (sin "entero") con su cuenta y su huella.</summary>
    private static List<string> Volcar(object r, bool entero)
    {
        List<string> lineas = new List<string>();
        foreach (MemberInfo m in Miembros(r.GetType()))
        {
            if (m.Name == "Duracion") { continue; }
            object v = ValorDe(m, r);
            if (m.Name == "LineaResumen" && v != null)
            {
                v = Regex.Replace((string)v, " Le\u00eddo a las \\d\\d:\\d\\d \\(hora del servidor\\)\\.", "");
            }

            if (Tablas.Contains(m.Name) && !entero)
            {
                List<string> dentro = new List<string>();
                Plano(m.Name, v, dentro, 0);
                lineas.Add(m.Name + ".N\t" + ((IList)v).Count);
                lineas.Add(m.Name + ".huella\t" + Huella(string.Join("\n", dentro)));
                continue;
            }

            Plano(m.Name, v, lineas, 0);
        }
        return lineas;
    }

    // -----------------------------------------------------------------------
    // Foto (P1): antes y despues, solo lectura
    // -----------------------------------------------------------------------

    public static void Foto(string salida)
    {
        object l0 = Leer(De("Ciclo"), true);
        DateTime hoy = (DateTime)Get(l0, "Hoy");
        object cicloDesde = Get(l0, "CicloDesde");
        List<KeyValuePair<string, object>> periodos = new List<KeyValuePair<string, object>>
        {
            new KeyValuePair<string, object>("Ciclo", De("Ciclo")),
            new KeyValuePair<string, object>("Mes", De("Mes")),
            new KeyValuePair<string, object>("Semana", De("Semana")),
            new KeyValuePair<string, object>("Ultimos30", De("Ultimos30")),
            new KeyValuePair<string, object>("F(hoy-6,hoy)", EntreFechas(hoy.AddDays(-6), hoy)),
            new KeyValuePair<string, object>("F(2026-09-01,2026-09-30)", EntreFechas(D("2026-09-01"), D("2026-09-30"))),
            new KeyValuePair<string, object>("F(2026-07-10,2026-07-20)", EntreFechas(D("2026-07-10"), D("2026-07-20"))),
            new KeyValuePair<string, object>("F(hoy-399,hoy)", EntreFechas(hoy.AddDays(-399), hoy))
        };
        if (cicloDesde != null)
        {
            periodos.Add(new KeyValuePair<string, object>("F(ciclo_desde,hoy)", EntreFechas((DateTime)cicloDesde, hoy)));
        }

        int lineas = 0, calculos = 0;
        using (StreamWriter w = new StreamWriter(salida, false, new UTF8Encoding(false)))
        {
            w.NewLine = "\n";
            foreach (KeyValuePair<string, object> p in periodos)
            {
                foreach (string modo in new[] { "cero", "cache" })
                {
                    object l;
                    if (modo == "cero")
                    {
                        l = Leer(p.Value, true);
                    }
                    else
                    {
                        Leer(De("Ciclo"), true);
                        l = Leer(p.Value, false);
                    }

                    object g = Calc(p.Value, l);
                    List<int[]> filtros = new List<int[]> { new[] { 0, 0, 0 } };
                    foreach (object o in Lista(g, "OpcionesLaboratorio")) { int id = (int)Get(o, "Id"); if (id != 0) { filtros.Add(new[] { id, 0, 0 }); } }
                    foreach (object o in Lista(g, "OpcionesDocente").Cast<object>().Where(q => (int)Get(q, "Id") != 0).Take(3)) { filtros.Add(new[] { 0, (int)Get(o, "Id"), 0 }); }
                    foreach (object o in Lista(g, "OpcionesGrupo").Cast<object>().Where(q => (int)Get(q, "Id") != 0).Take(3)) { filtros.Add(new[] { 0, 0, (int)Get(o, "Id") }); }
                    if (p.Key == "Ciclo") { filtros.Add(new[] { 0, 999999, 0 }); }
                    foreach (int[] f in filtros)
                    {
                        bool general = f[0] == 0 && f[1] == 0 && f[2] == 0;
                        object r = general ? g : Calcular(l, Resolver(p.Value, l, f[0], f[1], f[2]));
                        calculos++;
                        string prefijo = p.Key + "|" + modo + "|" + f[0] + "/" + f[1] + "/" + f[2] + "|";
                        foreach (string linea in Volcar(r, general))
                        {
                            w.WriteLine(prefijo + linea);
                            lineas++;
                        }
                    }
                }
            }
        }

        L("Foto: " + periodos.Count + " periodos, " + calculos + " calculos, " + lineas + " lineas en " + salida + " (hoy " + S(hoy) + ", ciclo desde " + S(cicloDesde) + ").");
    }

    private static Dictionary<string, string> LeerVolcado(string archivo)
    {
        Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string linea in File.ReadLines(archivo))
        {
            int tab = linea.IndexOf('\t');
            if (tab > 0) { d[linea.Substring(0, tab)] = linea.Substring(tab + 1); }
        }
        return d;
    }

    /// <summary>Lo que difiere entre dos volcados, solo en lo que existe en los dos.</summary>
    public static void CompararFotos(string antes, string ahora)
    {
        Dictionary<string, string> a = LeerVolcado(antes), b = LeerVolcado(ahora);
        int comunes = 0;
        List<string> distintas = new List<string>();
        foreach (KeyValuePair<string, string> k in a)
        {
            string otro;
            if (!b.TryGetValue(k.Key, out otro)) { continue; }
            comunes++;
            if (otro != k.Value) { distintas.Add(k.Key); }
        }

        List<string> soloAntes = a.Keys.Where(k => !b.ContainsKey(k)).ToList();
        List<string> soloAhora = b.Keys.Where(k => !a.ContainsKey(k)).ToList();
        L("Comparacion: " + comunes + " datos en los dos, " + distintas.Count + " distintos; solo antes " + soloAntes.Count + ", solo ahora " + soloAhora.Count + ".");
        foreach (string k in distintas.Take(60))
        {
            L("  " + k + "\r\n     antes: " + a[k] + "\r\n     ahora: " + b[k]);
        }

        Dictionary<string, int> miembrosSoloAntes = soloAntes.GroupBy(k => Regex.Replace(k.Substring(k.LastIndexOf('|') + 1), @"\[\d+\]", "[]")).ToDictionary(x => x.Key, x => x.Count());
        foreach (KeyValuePair<string, int> m in miembrosSoloAntes.OrderBy(x => x.Key).Take(20)) { L("  solo antes: " + m.Key + " (" + m.Value + ")"); }
        Dictionary<string, int> miembrosSoloAhora = soloAhora.GroupBy(k => Regex.Replace(k.Substring(k.LastIndexOf('|') + 1), @"\[\d+\]", "[]")).ToDictionary(x => x.Key, x => x.Count());
        foreach (KeyValuePair<string, int> m in miembrosSoloAhora.OrderBy(x => x.Key).Take(40)) { L("  solo ahora: " + m.Key + " (" + m.Value + ")"); }

        // Lo que la especificacion espera distinto (P1): el aviso de las fechas de "Este ciclo" y "en este ciclo".
        List<string> esperadas = distintas.Where(k => (k.StartsWith("F(ciclo_desde,hoy)|") && k.EndsWith("|LineaResumen"))
            || (k.StartsWith("Ciclo|") && k.Contains("|0/999999/0|") && (k.EndsWith("|LineaResumen") || k.EndsWith("|Filtro.Aviso")))).ToList();
        List<string> otras = distintas.Except(esperadas).ToList();
        foreach (string k in esperadas)
        {
            bool bien = k.StartsWith("F(") ? b[k] == a[k] + " Son las fechas de \u00abEste ciclo\u00bb: para el derecho a evaluaci\u00f3n (80 %), el\u00edgelo en Periodo."
                || b[k].Contains("Son las fechas de \u00abEste ciclo")
                : b[k].Contains("Ese docente no tiene clases en este ciclo: se ve todo el personal.");
            Ver("esperada: " + k + " -> " + b[k], bien);
        }

        Ver("todo lo demas es igual (" + otras.Count + " distintos)", otras.Count == 0, string.Join("; ", otras.Take(10)));
        int periodos = a.Keys.Select(k => k.Substring(0, k.IndexOf('|'))).Distinct().Count();
        Ver("los dos volcados tienen lo mismo (" + periodos + " periodos, solo antes " + soloAntes.Count + ")", soloAntes.Count == 0 && comunes > 1000);
    }

    // -----------------------------------------------------------------------
    // Ciclos (P2): la regla, sin base
    // -----------------------------------------------------------------------

    private sealed class Clase
    {
        public int Id;
        public DateTime Desde;
        public DateTime Hasta;
        public bool ConHorario = true;
    }

    private static int siguienteId = 5000;

    private static List<Clase> Bloque(int cuantas, string desde, string hasta, bool conHorario = true)
    {
        List<Clase> l = new List<Clase>();
        for (int i = 0; i < cuantas; i++) { l.Add(new Clase { Id = siguienteId++, Desde = D(desde), Hasta = D(hasta), ConHorario = conHorario }); }
        return l;
    }

    private static List<Clase> LeerTsv(string archivo)
    {
        List<Clase> l = new List<Clase>();
        foreach (string linea in File.ReadAllLines(archivo))
        {
            string[] p = linea.Split('\t');
            if (p.Length < 4 || p[3] != "1") { continue; }
            l.Add(new Clase { Id = int.Parse(p[0], Inv), Desde = D(p[1]), Hasta = D(p[2]), ConHorario = p.Length < 7 || p[6] != "0" });
        }
        return l;
    }

    private static List<Clase> Corridas(List<Clase> clases, int dias)
    {
        return clases.Select(c => new Clase { Id = c.Id, Desde = c.Desde.AddDays(-dias), Hasta = c.Hasta.AddDays(-dias), ConHorario = c.ConHorario }).ToList();
    }

    private static IList ClasesLeidas(List<Clase> todas)
    {
        Type tClase = T("DigLab.conexion.ClaseLeida");
        IList lista = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tClase));
        foreach (Clase c in todas)
        {
            object x = Activator.CreateInstance(tClase, true);
            Set(x, "Id", c.Id);
            Set(x, "VigenteDesde", c.Desde);
            Set(x, "VigenteHasta", c.Hasta);
            lista.Add(x);
        }
        return lista;
    }

    private static string Rango(IEnumerable<int> ids)
    {
        List<int> l = ids.OrderBy(x => x).ToList();
        if (l.Count == 0) { return "-"; }
        List<string> partes = new List<string>();
        int a = l[0], b = l[0];
        for (int i = 1; i <= l.Count; i++)
        {
            if (i < l.Count && l[i] == b + 1) { b = l[i]; continue; }
            partes.Add(a == b ? a.ToString(Inv) : a + "-" + b);
            if (i < l.Count) { a = b = l[i]; }
        }
        return string.Join(",", partes);
    }

    /// <summary>Como CiclosJuez.Mostrar, con ArmarCiclos y CicloEnCursoDe de DigLab.</summary>
    private static string Mostrar(List<Clase> todas, DateTime hoy)
    {
        IList clases = ClasesLeidas(todas);
        HashSet<int> conHorario = new HashSet<int>(todas.Where(c => c.ConHorario).Select(c => c.Id));
        List<Clase> valen = todas.Where(c => c.Desde <= hoy && c.Hasta >= hoy).ToList();
        object cicloDesde = valen.Count == 0 ? null : (object)valen.Min(c => c.Desde);
        object cicloHasta = valen.Count == 0 ? null : (object)valen.Max(c => c.Hasta);
        Dictionary<DateTime, string> nombres = new Dictionary<DateTime, string>();
        StringBuilder sb = new StringBuilder();
        if (cicloDesde != null)
        {
            object enCurso = Call(Datos, "CicloEnCursoDe", clases, conHorario, cicloDesde, cicloHasta, hoy, nombres);
            DateTime desde = (DateTime)cicloDesde;
            bool conH = todas.Any(c => c.Desde >= desde && c.Desde <= hoy && c.ConHorario);
            sb.AppendFormat("   hoy {0:yyyy-MM-dd}: Este ciclo desde el {1:yyyy-MM-dd} ({2} clases{3})\r\n", hoy, desde, ((IEnumerable<int>)Get(enCurso, "Clases")).Count(),
                conH ? ", termina " + ((DateTime)Get(enCurso, "Hasta")).ToString("yyyy-MM-dd", Inv) : "");
        }
        else
        {
            sb.AppendFormat("   hoy {0:yyyy-MM-dd}: Este ciclo (no hay: entre ciclos)\r\n", hoy);
        }

        foreach (object k in (IList)Call(Datos, "ArmarCiclos", clases, conHorario, cicloDesde, hoy, nombres))
        {
            int siguen = (int)Get(k, "SiguenDespues");
            sb.AppendFormat("   Ciclo {0,-30} {1:yyyy-MM-dd} .. {2:yyyy-MM-dd}{3}  clases={4} ids={5}\r\n", Get(k, "Nombre"), Get(k, "Desde"), Get(k, "Hasta"),
                siguen > 0 ? " (" + siguen + " siguen, la ultima hasta " + ((DateTime)Get(k, "HastaDeSusClases")).ToString("yyyy-MM-dd", Inv) + ")" : "",
                ((IEnumerable<int>)Get(k, "Clases")).Count(), Rango(((IEnumerable<int>)Get(k, "Clases"))));
        }
        return sb.ToString();
    }

    private static string Casos()
    {
        StringBuilder sb = new StringBuilder();
        List<Clase> c;
        sb.AppendLine("S1 dos semestres pegados (Aplicar: desde = fin + 1):");
        c = Bloque(40, "2026-02-09", "2026-07-10"); c.AddRange(Bloque(40, "2026-07-11", "2026-12-11"));
        sb.Append(Mostrar(c, D("2026-09-01"))); sb.Append(Mostrar(c, D("2027-01-10")));
        sb.AppendLine("S2 semestre nuevo con importar_horarios sin recortar el anterior (se enciman 2 semanas):");
        c = Bloque(68, "2026-09-30", "2027-02-28"); c.AddRange(Bloque(70, "2027-02-15", "2027-07-31"));
        sb.Append(Mostrar(c, D("2027-02-20"))); sb.Append(Mostrar(c, D("2027-04-01"))); sb.Append(Mostrar(c, D("2027-09-01")));
        sb.AppendLine("S2b igual, el nuevo cargado laboratorio por laboratorio en 10 dias:");
        c = Bloque(68, "2026-09-30", "2027-02-28");
        c.AddRange(Bloque(18, "2027-02-15", "2027-07-31")); c.AddRange(Bloque(17, "2027-02-18", "2027-07-31"));
        c.AddRange(Bloque(18, "2027-02-22", "2027-07-31")); c.AddRange(Bloque(17, "2027-02-25", "2027-07-31"));
        sb.Append(Mostrar(c, D("2027-04-01"))); sb.Append(Mostrar(c, D("2027-09-01")));
        sb.AppendLine("S3 agregadas a mitad con las fechas del dialogo (hoy + 5 meses) y el siguiente semestre:");
        c = Bloque(68, "2026-09-30", "2027-02-28"); c.AddRange(Bloque(6, "2026-10-11", "2027-03-11"));
        c.AddRange(Bloque(1, "2026-11-20", "2027-04-20")); c.AddRange(Bloque(60, "2027-03-15", "2027-08-14"));
        sb.Append(Mostrar(c, D("2027-04-01"))); sb.Append(Mostrar(c, D("2027-05-01"))); sb.Append(Mostrar(c, D("2027-09-01")));
        sb.AppendLine("S4 curso de verano entre dos semestres:");
        c = Bloque(50, "2027-02-08", "2027-07-02"); c.AddRange(Bloque(5, "2027-07-05", "2027-08-13"));
        c.AddRange(Bloque(55, "2027-08-23", "2028-01-21"));
        sb.Append(Mostrar(c, D("2027-10-01"))); sb.Append(Mostrar(c, D("2028-03-01")));
        sb.AppendLine("S4b verano creado de antemano, empieza 4 dias antes del fin del semestre:");
        c = Bloque(50, "2027-02-08", "2027-07-02"); c.AddRange(Bloque(5, "2027-06-28", "2027-08-13"));
        sb.Append(Mostrar(c, D("2027-10-01")));
        sb.AppendLine("S5 una clase alargada mes y medio, y el siguiente semestre:");
        c = Bloque(29, "2026-02-09", "2026-07-10"); c.AddRange(Bloque(1, "2026-02-09", "2026-08-30"));
        c.AddRange(Bloque(30, "2026-08-24", "2027-01-15"));
        sb.Append(Mostrar(c, D("2026-08-26"))); sb.Append(Mostrar(c, D("2026-10-01")));
        sb.AppendLine("S6 clase suelta sin nada en 2025 (sin franjas):");
        c = Bloque(20, "2026-02-09", "2026-07-10"); c.AddRange(Bloque(1, "2025-05-01", "2025-09-30", false));
        sb.Append(Mostrar(c, D("2026-10-01")));
        sb.AppendLine("S6b la misma clase suelta, pero con horario (abre un ciclo de una clase):");
        c = Bloque(20, "2026-02-09", "2026-07-10"); c.AddRange(Bloque(1, "2025-05-01", "2025-09-30"));
        sb.Append(Mostrar(c, D("2026-10-01")));
        sb.AppendLine("S7 laboratorio nuevo a 2/3 del semestre (8 de 20 clases, fechas del dialogo):");
        c = Bloque(20, "2026-09-30", "2027-02-28"); c.AddRange(Bloque(8, "2027-01-08", "2027-06-08"));
        sb.Append(Mostrar(c, D("2027-07-01")));
        sb.AppendLine("S7b igual con 10 de 20 (la mitad): riesgo conocido:");
        c = Bloque(20, "2026-09-30", "2027-02-28"); c.AddRange(Bloque(10, "2027-01-08", "2027-06-08"));
        sb.Append(Mostrar(c, D("2027-07-01")));
        sb.AppendLine("S7c bloque grande a mitad que termina con el semestre (como teschi_demo: 40 de 28, fin 26 dias despues):");
        c = Bloque(28, "2026-08-05", "2027-02-01"); c.AddRange(Bloque(40, "2026-10-26", "2027-02-27"));
        sb.Append(Mostrar(c, D("2027-04-01")));
        sb.AppendLine("S8 tres ciclos aplicados con Aplicar (cada uno recorta al anterior) y 3 agregadas a mitad del segundo:");
        c = Bloque(68, "2026-09-30", "2027-02-05"); c.AddRange(Bloque(70, "2027-02-08", "2027-06-25"));
        c.AddRange(Bloque(3, "2027-03-01", "2027-06-25")); c.AddRange(Bloque(72, "2027-06-28", "2027-12-10"));
        sb.Append(Mostrar(c, D("2027-09-01")));
        sb.AppendLine("S9 la escuela hoy (68 clases del 30/09/2026 al 28/02/2027):");
        c = Bloque(68, "2026-09-30", "2027-02-28");
        sb.Append(Mostrar(c, D("2026-10-31"))); sb.Append(Mostrar(c, D("2027-03-05")));
        sb.AppendLine("S10 vacaciones de 40 dias entre semestres y el viejo de hace 2 anos:");
        c = Bloque(60, "2025-02-03", "2025-06-27"); c.AddRange(Bloque(62, "2025-08-11", "2026-01-09"));
        c.AddRange(Bloque(65, "2026-02-02", "2026-06-26"));
        sb.Append(Mostrar(c, D("2026-08-01"))); sb.Append(Mostrar(c, D("2026-10-31")));
        sb.AppendLine("S11 clase sin horario que empieza antes que todas (no abre ciclo):");
        c = Bloque(1, "2026-01-05", "2026-06-30", false); c.AddRange(Bloque(30, "2026-02-09", "2026-07-10"));
        sb.Append(Mostrar(c, D("2026-10-01")));
        return sb.ToString();
    }

    private static object NuevoCiclo(string nombre, bool aplicado, bool repetido, string desde, string hasta)
    {
        object c = Activator.CreateInstance(T("DigLab.conexion.CicloEscolar"), true);
        Set(c, "Nombre", nombre);
        Set(c, "NombreAplicado", aplicado);
        Set(c, "NombreRepetido", repetido);
        Set(c, "Desde", D(desde));
        Set(c, "Hasta", D(hasta));
        Set(c, "HastaDeSusClases", D(hasta));
        return c;
    }

    /// <summary>Una lectura vacia con ese hoy (para Resolver y los textos, sin base).</summary>
    private static object LecturaDe(DateTime hoy, DateTime? cicloDesde)
    {
        object l = Activator.CreateInstance(T("DigLab.conexion.LecturaEstadisticas"), true);
        object a = Activator.CreateInstance(T("DigLab.conexion.LecturaDeAsistencia"), true);
        Set(a, "AhoraServidor", hoy.AddHours(10));
        Set(a, "DesdeLectura", hoy.AddDays(-30));
        Set(a, "HastaLectura", hoy);
        Set(l, "Asistencia", a);
        Set(l, "CicloDesde", cicloDesde);
        return l;
    }

    public static string Ciclos(string carpetaTsv, bool conBase)
    {
        StringBuilder salida = new StringBuilder();
        if (!string.IsNullOrEmpty(carpetaTsv))
        {
            foreach (string b in new[] { "3317_otru", "3317_demo", "3318_otru" })
            {
                salida.AppendLine("== " + b);
                salida.Append(Mostrar(LeerTsv(Path.Combine(carpetaTsv, "c_" + b + ".tsv")), D("2026-10-31")));
            }
        }

        salida.AppendLine("== casos inventados");
        salida.Append(Casos());
        if (!string.IsNullOrEmpty(carpetaTsv))
        {
            salida.AppendLine("== teschi_demo corrida 364 y 728 dias atras (hoy 2026-10-31)");
            List<Clase> demo = LeerTsv(Path.Combine(carpetaTsv, "c_3317_demo.tsv"));
            salida.Append(Mostrar(Corridas(demo, 364), D("2026-10-31")));
            salida.Append(Mostrar(Corridas(demo, 728), D("2026-10-31")));
            salida.AppendLine("== teschi_otru corrida 364 dias");
            salida.Append(Mostrar(Corridas(LeerTsv(Path.Combine(carpetaTsv, "c_3317_otru.tsv")), 364), D("2026-10-31")));
            salida.AppendLine("== 3318 corrida 364 dias");
            salida.Append(Mostrar(Corridas(LeerTsv(Path.Combine(carpetaTsv, "c_3318_otru.tsv")), 364), D("2026-10-31")));
        }

        L(salida.ToString());

        // Las funciones de texto.
        L("== Funciones de texto");
        Func<object, string> texto = c => (string)Call(Datos, "TextoDeCiclo", c);
        Ver("TextoDeCiclo de sus meses: 'Ciclo septiembre 2026-febrero 2027'", texto(NuevoCiclo("Septiembre 2026-febrero 2027", false, false, "2026-09-30", "2027-02-28")) == "Ciclo septiembre 2026-febrero 2027");
        Ver("con NombreRepetido: '... (desde el 30/09/2026)'", texto(NuevoCiclo("Septiembre 2026-febrero 2027", false, true, "2026-09-30", "2027-02-28")) == "Ciclo septiembre 2026-febrero 2027 (desde el 30/09/2026)");
        Ver("con NombreAplicado '2027-1': 'Ciclo 2027-1'", texto(NuevoCiclo("2027-1", true, false, "2027-02-15", "2027-07-31")) == "Ciclo 2027-1");
        Ver("con NombreAplicado 'ciclo escolar 2027' (ya empieza con ciclo): 'Ciclo escolar 2027'", texto(NuevoCiclo("ciclo escolar 2027", true, false, "2027-02-15", "2027-07-31")) == "Ciclo escolar 2027");
        Ver("con NombreAplicado 'Ciclo 2027-1 ' (ya empieza con Ciclo, se recorta): 'Ciclo 2027-1'", texto(NuevoCiclo("Ciclo 2027-1 ", true, false, "2027-02-15", "2027-07-31")) == "Ciclo 2027-1");

        DateTime hoy = D("2026-10-31");
        Func<string, string, string> validar = (a, b) => (string)Call(Datos, "ValidarFechas", D(a), D(b), hoy);
        const string Cuatrocientos = "Elige como mucho 400 d\u00edas (un poco m\u00e1s de un a\u00f1o). Si hace falta m\u00e1s, hazlo en varias veces.";
        Ver("ValidarFechas (2024-01-01, 2025-03-01): el texto de los 400 dias", validar("2024-01-01", "2025-03-01") == Cuatrocientos, validar("2024-01-01", "2025-03-01"));
        Ver("ValidarFechas (2025-09-26, 2026-10-31): el texto de los 400 dias", validar("2025-09-26", "2026-10-31") == Cuatrocientos);
        Ver("ValidarFechas (2025-09-27, 2026-10-31): vale", validar("2025-09-27", "2026-10-31") == null);
        Ver("ValidarFechas (2024-06-01, 2024-12-31): vale (cualquier dia)", validar("2024-06-01", "2024-12-31") == null);
        Ver("ValidarFechas (2026-10-01, 2026-11-01): 'El \u00faltimo d\u00eda no puede ser despu\u00e9s de hoy.'", validar("2026-10-01", "2026-11-01") == "El \u00faltimo d\u00eda no puede ser despu\u00e9s de hoy.");
        Ver("ValidarFechas (2026-10-10, 2026-10-01): 'El primer d\u00eda no puede ser despu\u00e9s del \u00faltimo.'", validar("2026-10-10", "2026-10-01") == "El primer d\u00eda no puede ser despu\u00e9s del \u00faltimo.");

        object hastaHoy = DesdeHastaHoy(D("2026-10-01"));
        object f31 = Resolver(hastaHoy, LecturaDe(D("2026-10-31"), null), 0, 0, 0);
        object f01 = Resolver(hastaHoy, LecturaDe(D("2026-11-01"), null), 0, 0, 0);
        Ver("DesdeHastaHoy(01/10/2026) con Hoy = 31/10: del " + S(Get(f31, "Desde")) + " al " + S(Get(f31, "Hasta")) + ", HastaHoy " + S(Get(f31, "HastaHoy")),
            (DateTime)Get(f31, "Hasta") == D("2026-10-31") && (DateTime)Get(f31, "Desde") == D("2026-10-01") && (bool)Get(f31, "HastaHoy"));
        Ver("  con Hoy = 01/11: llega solo al " + S(Get(f01, "Hasta")), (DateTime)Get(f01, "Hasta") == D("2026-11-01"));
        Ver("  el combo dice 'Del 01/10/2026 a hoy'", (string)Call(Datos, "TextoDePeriodo", hastaHoy, null) == "Del 01/10/2026 a hoy");
        object viejas = Resolver(EntreFechas(D("2024-06-01"), D("2024-12-31")), LecturaDe(hoy, null), 0, 0, 0);
        Ver("EntreFechas(2024-06-01, 2024-12-31) no se recorta a los ultimos 400 dias: del " + S(Get(viejas, "Desde")), (DateTime)Get(viejas, "Desde") == D("2024-06-01") && (DateTime)Get(viejas, "Hasta") == D("2024-12-31"));
        object mananas = Resolver(EntreFechas(D("2026-10-20"), D("2026-11-20")), LecturaDe(hoy, null), 0, 0, 0);
        Ver("unas fechas que pasan de hoy llegan a hoy: al " + S(Get(mananas, "Hasta")), (DateTime)Get(mananas, "Hasta") == hoy);
        object sinCiclo = Resolver(De("Ciclo"), LecturaDe(hoy, null), 0, 0, 0);
        Ver("'Este ciclo' sin ningun ciclo: 'Ultimos 30 dias' sin aviso (" + S(Get(sinCiclo, "Tipo")) + ")", S(Get(sinCiclo, "Tipo")) == "Ultimos30" && Get(sinCiclo, "AvisoDelPeriodo") == null);
        object perdido = Resolver(DelCiclo(NuevoCiclo("Abril-septiembre 2026", false, false, "2026-04-07", "2026-09-04")), LecturaDe(hoy, null), 0, 0, 0);
        Ver("un ciclo que ya no esta, sin ningun otro: los ultimos 30 dias con su aviso ('" + S(Get(perdido, "AvisoDelPeriodo")) + "')",
            S(Get(perdido, "Tipo")) == "Ultimos30" && S(Get(perdido, "AvisoDelPeriodo")) == "Ese ciclo ya no est\u00e1 en la lista (cambiaron las fechas de sus clases): se ven los \u00faltimos 30 d\u00edas.");

        if (conBase)
        {
            L("== Lo que dice la base");
            object l = Leer(De("Ciclo"), true);
            object enCurso = Get(l, "CicloEnCurso");
            L("  hoy " + S(Get(l, "Hoy")) + ": Este ciclo " + (enCurso == null ? "(no hay)" : "desde el " + S(Get(enCurso, "Desde")) + " (" + ((IEnumerable<int>)Get(enCurso, "Clases")).Count()
                + " clases, termina " + S(Get(enCurso, "Hasta")) + ")"));
            foreach (object k in Lista(l, "CiclosPasados"))
            {
                L("  pasado: " + texto(k) + ", del " + S(Get(k, "Desde")) + " al " + S(Get(k, "Hasta")) + ", " + ((IEnumerable<int>)Get(k, "Clases")).Count() + " clases {" + Rango((IEnumerable<int>)Get(k, "Clases")) + "}"
                    + ((int)Get(k, "SiguenDespues") > 0 ? " (" + Get(k, "SiguenDespues") + " siguen hasta el " + S(Get(k, "HastaDeSusClases")) + ")" : "")
                    + ((bool)Get(k, "NombreAplicado") ? " [nombre aplicado]" : ""));
            }

            List<string> combo = new List<string>();
            foreach (object p in (IList)Call(Datos, "OpcionesDePeriodo", l)) { combo.Add((string)Call(Datos, "TextoDePeriodo", p, l)); }
            L("  combo: " + string.Join(" | ", combo));
        }

        return salida.ToString();
    }

    // -----------------------------------------------------------------------
    // Ciclo (P3): un ciclo pasado con datos de verdad, en una copia corrida
    // -----------------------------------------------------------------------

    private static object Sql(string sql)
    {
        object conexion = Call(Activator.CreateInstance(T("DigLab.Conexion"), true), "GetConexion");
        try
        {
            Type tCmd = conexion.GetType().Assembly.GetType("MySql.Data.MySqlClient.MySqlCommand");
            object cmd = Activator.CreateInstance(tCmd, new object[] { sql, conexion });
            return tCmd.GetMethod("ExecuteScalar", Type.EmptyTypes).Invoke(cmd, null);
        }
        finally
        {
            ((IDisposable)conexion).Dispose();
        }
    }

    private static Delegate EntreDias(DateTime desde, DateTime hasta)
    {
        Type tDia = T("DigLab.conexion.DiaDeAsistencia");
        ParameterExpression d = Expression.Parameter(tDia, "d");
        Expression fecha = Expression.PropertyOrField(d, "Fecha");
        Expression cuerpo = Expression.AndAlso(Expression.GreaterThanOrEqual(fecha, Expression.Constant(desde)), Expression.LessThanOrEqual(fecha, Expression.Constant(hasta)));
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(tDia, typeof(bool)), cuerpo, d).Compile();
    }

    public static void Ciclo(string resumen, string comparar)
    {
        Olvidar();
        object l = Leer(De("Ciclo"), true);
        IList pasados = Lista(l, "CiclosPasados");
        L("hoy " + S(Get(l, "Hoy")) + ", ciclo en curso: " + S(Get(l, "CicloDesde")) + "; " + pasados.Count + " ciclos pasados:");
        foreach (object k in pasados)
        {
            L("  " + Call(Datos, "TextoDeCiclo", k) + ", del " + S(Get(k, "Desde")) + " al " + S(Get(k, "Hasta")) + ", " + ((IEnumerable<int>)Get(k, "Clases")).Count() + " clases {"
                + Rango((IEnumerable<int>)Get(k, "Clases")) + "}" + ((int)Get(k, "SiguenDespues") > 0 ? " (" + Get(k, "SiguenDespues") + " siguen hasta el " + S(Get(k, "HastaDeSusClases")) + ")" : ""));
        }

        if (pasados.Count == 0)
        {
            Ver("hay algun ciclo pasado", false);
            return;
        }

        object x = pasados[0];
        DateTime desde = (DateTime)Get(x, "Desde"), hasta = (DateTime)Get(x, "Hasta");
        string nombre = (string)Call(Datos, "TextoDeCiclo", x);

        // a) "Este ciclo" entre ciclos: el ultimo que termino, con su aviso.
        object rc = Calc(De("Ciclo"), l);
        object fc = Get(rc, "Filtro");
        Ver("a) 'Este ciclo' sin ciclo en curso cae en '" + nombre + "' (" + S(Get(fc, "Tipo")) + ")", Get(l, "CicloDesde") == null && S(Get(fc, "Tipo")) == "CicloPasado"
            && (DateTime)Get(fc, "Desde") == desde);
        Ver("   con 'Hoy no hay ning\u00fan ciclo en curso: se ve el \u00faltimo que termin\u00f3.'", S(Get(rc, "LineaResumen")).Contains("Hoy no hay ning\u00fan ciclo en curso: se ve el \u00faltimo que termin\u00f3."), S(Get(rc, "LineaResumen")));
        Ver("   y la columna 'Derecho a evaluaci\u00f3n (80 %)'", S(Get(rc, "TituloDerecho")) == "Derecho a evaluaci\u00f3n (80 %)");

        // El ciclo pedido por su primer dia.
        object periodo = DelCiclo(x);
        object lx = Leer(periodo, false);
        object r = Calc(periodo, lx);
        object f = Get(r, "Filtro");
        L("  lblResumen: " + Get(r, "LineaResumen"));
        Ver("el ciclo pasado se calcula como ciclo (EsCiclo, solo sus clases)", (bool)Get(f, "EsCiclo") && Get(f, "Ciclo") != null && (bool)Get(r, "CicloCerrado"));
        Ver("lblResumen: '" + nombre + ": del " + Fecha(desde) + " al " + Fecha(hasta) + " (completo).'",
            S(Get(r, "LineaResumen")).StartsWith(nombre + ": del " + Fecha(desde) + " al " + Fecha(hasta) + " (completo). Le\u00eddo a las "));
        Ver("los dias que cuentan son los del ciclo (" + S(Get(r, "Desde")) + " a " + S(Get(r, "Hasta")) + ")", (DateTime)Get(r, "Desde") == desde && (DateTime)Get(r, "Hasta") == hasta);
        ISet<int> suyas = new HashSet<int>((IEnumerable<int>)Get(x, "Clases"));
        IList filas = Lista(r, "Clases");
        Ver("solo sus clases: " + filas.Count + " filas en Clases, todas del ciclo", filas.Count > 0 && filas.Cast<object>().All(c => suyas.Contains((int)Get(c, "ClaseId"))));
        Dictionary<int, int> profesor = new Dictionary<int, int>();
        foreach (object c in Lista(Get(lx, "Asistencia"), "Clases")) { profesor[(int)Get(c, "Id")] = (int)Get(c, "ProfesorId"); }
        HashSet<int> susDocentes = new HashSet<int>(suyas.Where(profesor.ContainsKey).Select(i => profesor[i]));
        List<int> ofrecidos = Lista(r, "OpcionesDocente").Cast<object>().Select(o => (int)Get(o, "Id")).Where(i => i != 0).ToList();
        Ver("Docente ofrece solo los suyos (" + ofrecidos.Count + " de " + susDocentes.Count + ")", ofrecidos.Count > 0 && ofrecidos.All(susDocentes.Contains));
        Ver("titulo 'Derecho a evaluaci\u00f3n (80 %)' y CicloEntero", S(Get(r, "TituloDerecho")) == "Derecho a evaluaci\u00f3n (80 %)" && (bool)Get(r, "CicloEntero"));
        string ayudaC1 = S(Get(Get(Get(r, "Cifras"), "Asistencia"), "Ayuda"));
        Ver("la ayuda de C1 dice lo de Mis grupos para el ciclo", ayudaC1.Contains("Quien entr\u00f3 a la lista despu\u00e9s del primer d\u00eda de su clase cuenta con falta en las clases de antes, como en Mis grupos.")
            && !ayudaC1.Contains("pueden bajar"), ayudaC1);
        Ver("la linea de Estudiantes: '(solo quienes registraron en el ciclo: ...)'", S(Get(r, "LineaEstudiantes")).EndsWith("(solo quienes registraron en el ciclo: DigLab no tiene la lista oficial de cada grupo)."), S(Get(r, "LineaEstudiantes")));
        object conAviso = Calcular(lx, Resolver(periodo, lx, 0, 999999, 0));
        Ver("un docente sin clases en el ciclo: 'Ese docente no tiene clases en este ciclo: se ve todo el personal.'", S(Get(Get(conAviso, "Filtro"), "Aviso")) == "Ese docente no tiene clases en este ciclo: se ve todo el personal.");

        // b) Cada clase: lo mismo que Mis grupos (DeLaClase) en los dias del ciclo.
        object asistencia = Activator.CreateInstance(T("DigLab.conexion.AsistenciaDatos"), true);
        Type tResumen = T("DigLab.conexion.ResumenAsistencia");
        Delegate cuenta = EntreDias(desde, hasta);
        int iguales = 0, conSesiones = 0;
        List<string> distintas = new List<string>();
        foreach (object fila in filas)
        {
            int id = (int)Get(fila, "ClaseId");
            object lista = Call(asistencia, "DeLaClase", id, null, null);
            object res = tResumen.GetMethod("De", new[] { lista.GetType(), cuenta.GetType() }).Invoke(null, new object[] { lista, cuenta });
            IList alumnos = Lista(res, "Alumnos");
            int xx = alumnos.Cast<object>().Sum(a => (int)Get(a, "Asistencias"));
            int nn = alumnos.Cast<object>().Sum(a => (int)Get(a, "Asistencias") + (int)Get(a, "Faltas"));
            conSesiones += nn > 0 ? 1 : 0;
            if ((int)Get(fila, "Estudiantes") == alumnos.Count && (int)Get(fila, "Entro") == xx && (int)Get(fila, "Veces") == nn) { iguales++; }
            else { distintas.Add(id + ": " + Get(fila, "Estudiantes") + "/" + Get(fila, "Entro") + "/" + Get(fila, "Veces") + " contra " + alumnos.Count + "/" + xx + "/" + nn); }
        }
        Ver("b) cada clase del ciclo da los mismos estudiantes, X y N que Mis grupos en sus dias (" + iguales + " de " + filas.Count + ", " + conSesiones + " con dias que cuentan)",
            distintas.Count == 0, string.Join("; ", distintas));

        // c) C2 y Docentes como Faltas (LeerCalendario de los dias del ciclo).
        object horarios = Activator.CreateInstance(T("DigLab.conexion.HorariosDatos"), true);
        object calendario = Call(horarios, "LeerCalendario", desde, hasta, false);
        Type tResumenDia = T("DigLab.ResumenDia");
        object total = Activator.CreateInstance(tResumenDia, true);
        Dictionary<int, object> porDocente = new Dictionary<int, object>();
        foreach (object c in Lista(calendario, "Clases"))
        {
            DateTime dia = (DateTime)Get(c, "Fecha");
            if (dia < desde || dia > hasta || !suyas.Contains((int)Get(c, "ClaseId"))) { continue; }
            Call(total, "Contar", Get(c, "Estado"));
            int doc = (int)Get(c, "ProfesorId");
            if (!porDocente.ContainsKey(doc)) { porDocente[doc] = Activator.CreateInstance(tResumenDia, true); }
            Call(porDocente[doc], "Contar", Get(c, "Estado"));
        }
        object cifras = Get(r, "Cifras");
        Ver("c) C2 como Faltas: con bitacoras " + Get(cifras, "ClasesConBitacorasX") + ", sin " + Get(cifras, "ClasesSinBitacoras") + ", con dadas " + Get(cifras, "ClasesConDadas") + ", canceladas " + Get(cifras, "Canceladas"),
            (int)Get(cifras, "ClasesConBitacorasX") == (int)Get(total, "ConBitacoras") && (int)Get(cifras, "ClasesSinBitacoras") == (int)Get(total, "Faltan")
            && (int)Get(cifras, "ClasesConDadas") == (int)Get(total, "SinBitacorasConAsistencia") && (int)Get(cifras, "Canceladas") == (int)Get(total, "Canceladas"),
            "Faltas: " + Get(total, "ConBitacoras") + "/" + Get(total, "Faltan") + "/" + Get(total, "SinBitacorasConAsistencia") + "/" + Get(total, "Canceladas"));
        bool docentesIguales = true;
        foreach (object d in Lista(r, "Docentes"))
        {
            object deFaltas;
            int id = (int)Get(d, "DocenteId");
            int con = porDocente.TryGetValue(id, out deFaltas) ? (int)Get(deFaltas, "ConBitacoras") : 0;
            int sin = deFaltas != null ? (int)Get(deFaltas, "Faltan") : 0;
            if ((int)Get(d, "ConBitacoras") != con || (int)Get(d, "SinBitacoras") != sin) { docentesIguales = false; }
        }
        Ver("   y cada docente, como Faltas (" + Lista(r, "Docentes").Count + ")", docentesIguales);

        // d) Bitacoras registradas, contra la base.
        object enLaBase = Sql("SELECT COUNT(DISTINCT rb.fk_codigo_accesos, rb.fk_usuario) FROM registros_bitacoras rb JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos "
            + "WHERE COALESCE(ca.fecha, DATE(ca.hora_registro)) BETWEEN '" + S(desde) + "' AND '" + S(hasta) + "'");
        Ver("d) bitacoras registradas " + Get(cifras, "BitacorasRegistradas") + " = las de la base " + S(enLaBase), Convert.ToInt32(enLaBase, Inv) == (int)Get(cifras, "BitacorasRegistradas"));

        // e) Con las mismas fechas como "Elegir fechas": el 80 % es el del periodo, con el aviso.
        object rf = Calc(EntreFechas(desde, hasta), Leer(EntreFechas(desde, hasta), false));
        Ver("e) con EntreFechas del ciclo: '80 % o m\u00e1s en el periodo'", S(Get(rf, "TituloDerecho")) == "80 % o m\u00e1s en el periodo");
        string avisoFechas = "Son las fechas del " + Call(Datos, "MinusculaInicial", nombre) + ": para el derecho a evaluaci\u00f3n (80 %), el\u00edgelo en Periodo.";
        Ver("   y el aviso '" + avisoFechas + "'", S(Get(Get(rf, "Filtro"), "AvisoDelPeriodo")) == avisoFechas && S(Get(rf, "LineaResumen")).Contains(avisoFechas), S(Get(rf, "LineaResumen")));

        // f) La cache: la de un periodo pasado no tapa a la de hoy, y se vuelve sin leer.
        Olvidar();
        object c1 = Leer(periodo, false);
        object c2 = Leer(periodo, false);
        object mes = Leer(De("Mes"), false);
        object c3 = Leer(periodo, false);
        Ver("f) Leer del ciclo dos veces da la misma lectura (" + S(Get(c1, "LeidaUtc")) + ")", ReferenceEquals(c1, c2));
        Ver("   'Este mes' lee otra (llega a hoy: " + S(Get(mes, "HastaLectura")) + ")", !ReferenceEquals(mes, c1) && (DateTime)Get(mes, "HastaLectura") == (DateTime)Get(mes, "Hoy"));
        Ver("   y el ciclo vuelve a dar la primera (guardadaPasada)", ReferenceEquals(c3, c1));
        Ver("   la del ciclo no cubre 'Este mes' ni 'Este ciclo'", !(bool)Call(Datos, "Cubre", c1, De("Mes")) && !(bool)Call(Datos, "Cubre", c1, EntreFechas((DateTime)Get(c1, "Hoy"), (DateTime)Get(c1, "Hoy"))));

        // g) La lectura acotada da lo mismo que una mas larga.
        Olvidar();
        object corta = Leer(periodo, true);
        DateTime hastaLarga = ((DateTime)Get(corta, "HastaLectura")).AddDays(27);
        object larga = Leer(EntreFechas(desde, hastaLarga), true);
        List<string> deCorta = Volcar(Calc(periodo, corta), true);
        List<string> deLarga = Volcar(Calc(periodo, larga), true);
        List<string> difieren = deCorta.Except(deLarga).Take(5).ToList();
        Ver("g) calculado con la lectura del ciclo (" + S(Get(corta, "DesdeLectura")) + " a " + S(Get(corta, "HastaLectura")) + ") y con una mas larga (a " + S(Get(larga, "HastaLectura"))
            + "): lo mismo (" + deCorta.Count + " datos)", deCorta.SequenceEqual(deLarga), string.Join(" | ", difieren));
        List<string> delLog = LineasDelLog("Estadisticas: leido de " + S(desde) + " a " + S(Get(corta, "HastaLectura")));
        L("  log: " + (delLog.Count > 0 ? delLog[0].Substring(delLog[0].IndexOf("Estadisticas:")) : "(nada)"));
        Ver("   el log dice 'leido de " + S(desde) + " a " + S(Get(corta, "HastaLectura")) + "'", delLog.Count > 0);

        // h) Lo que debe dar igual en otra copia corrida otra vez (sin tope de 400 dias).
        List<string> resumenLineas = new List<string>
        {
            "ciclo.clases\t" + suyas.Count,
            "Cifras\t" + string.Join(",", new[] { "Entraron", "Veces", "EstudiantesBajo", "ClasesConBitacorasX", "ClasesQueContaban", "ClasesSinBitacoras", "ClasesConDadas", "Canceladas",
                "BitacorasRegistradas", "EstudiantesDistintos", "FallasReportadas", "AvisosSinInternet" }.Select(n => S(Get(cifras, n)))),
            "filas\t" + string.Join(",", Tablas.Select(t => Lista(r, t).Count.ToString(Inv))),
            "estudiantes\t" + string.Join(",", Lista(r, "Estudiantes").Cast<object>().Select(e => Get(e, "Entro") + "/" + Get(e, "Veces")).OrderBy(s => s, StringComparer.Ordinal)),
            "clases\t" + string.Join(",", filas.Cast<object>().Select(c => Get(c, "ClaseId") + ":" + Get(c, "Entro") + "/" + Get(c, "Veces") + "/" + Get(c, "ConBitacoras") + "/" + Get(c, "QueContaban")).OrderBy(s => s, StringComparer.Ordinal)),
            "semanas\t" + Lista(Get(r, "Graficas"), "PorSemana").Count
        };
        if (!string.IsNullOrEmpty(resumen)) { File.WriteAllLines(resumen, resumenLineas, new UTF8Encoding(false)); L("  resumen en " + resumen); }
        if (!string.IsNullOrEmpty(comparar))
        {
            List<string> otro = File.ReadAllLines(comparar).ToList();
            Ver("h) la misma copia corrida otros dias da las mismas cifras, filas y listas", otro.SequenceEqual(resumenLineas), string.Join(" | ", resumenLineas.Except(otro).Take(3)));
        }
    }

    // -----------------------------------------------------------------------
    // Pdf (P6)
    // -----------------------------------------------------------------------

    private static object DatosDelReporte(object r, object lectura)
    {
        object d = Activator.CreateInstance(T("DigLab.DatosDelReporte"), true);
        Set(d, "Periodo", Call(Datos, "NombreDelPeriodo", r));
        Set(d, "NombreCorto", Call(Datos, "NombreCortoDelPeriodo", r));
        Set(d, "NotaDelPeriodo", Call(Datos, "NotaDelPeriodoParaElReporte", r));
        Set(d, "Desde", Get(r, "Desde"));
        Set(d, "Hasta", Get(r, "Hasta"));
        Set(d, "Laboratorio", "Todos los laboratorios");
        Set(d, "Docente", "Todo el personal docente");
        Set(d, "Grupo", "Todos los grupos");
        Set(d, "Agrupacion", Get(Get(r, "Graficas"), "Sugerida"));
        Set(d, "GeneradoPor", "ADMPRUEBA (administraci\u00f3n de DigLab)");
        Set(d, "GeneradoEn", Get(lectura, "AhoraServidor"));
        Set(d, "LeidoEn", Get(lectura, "AhoraServidor"));
        Set(d, "ConDocentes", true);
        return d;
    }

    /// <summary>El texto del PDF, con los espacios y saltos juntados en un espacio.</summary>
    public static string TextoDelPdf(byte[] pdf)
    {
        Assembly itext = Assembly.LoadFrom(Path.Combine(carpeta, "itextsharp.dll"));
        Type tLector = itext.GetType("iTextSharp.text.pdf.PdfReader", true);
        object lector = Activator.CreateInstance(tLector, new object[] { pdf });
        int paginas = (int)tLector.GetProperty("NumberOfPages").GetValue(lector, null);
        Type tExtractor = itext.GetType("iTextSharp.text.pdf.parser.PdfTextExtractor", true);
        MethodInfo deLaPagina = tExtractor.GetMethods().First(m => m.Name == "GetTextFromPage" && m.GetParameters().Length == 2);
        StringBuilder sb = new StringBuilder();
        for (int i = 1; i <= paginas; i++) { sb.Append(deLaPagina.Invoke(null, new object[] { lector, i })).Append(' '); }
        tLector.GetMethod("Close", Type.EmptyTypes).Invoke(lector, null);
        return Normalizar(sb.ToString());
    }

    /// <summary>Espacios y saltos juntados en uno; sin el salto de renglon despues de un guion (2026- febrero).</summary>
    private static string Normalizar(string texto)
    {
        return Regex.Replace(Regex.Replace(texto, @"\s+", " "), @"- ", "-");
    }

    private static void UnPdf(string carpetaSalida, string nombre, object periodo, string[] debeDecir, string archivo)
    {
        object l = Leer(periodo, false);
        object r = Calc(periodo, l);
        object datos = DatosDelReporte(r, l);
        byte[] pdf = (byte[])Call(T("DigLab.ReporteAuditoriaPdf"), "Crear", r, datos);
        string ruta = Path.Combine(carpetaSalida, "reporte_" + nombre + ".pdf");
        File.WriteAllBytes(ruta, pdf);
        string texto = TextoDelPdf(pdf);
        L("  " + nombre + ": " + ruta + " (" + pdf.Length + " bytes); periodo '" + Get(datos, "Periodo") + "', corto '" + Get(datos, "NombreCorto") + "'");
        foreach (string debe in debeDecir)
        {
            Ver("  el PDF dice '" + debe + "'", texto.Contains(Normalizar(debe)));
        }

        if (archivo != null)
        {
            string nombreArchivo = (string)Call(T("DigLab.ReporteAuditoriaPdf"), "NombreArchivo", datos, false);
            Ver("  NombreArchivo '" + nombreArchivo + "'", nombreArchivo == archivo);
        }
    }

    public static void Pdf(string carpetaSalida)
    {
        Directory.CreateDirectory(carpetaSalida);
        object l = Leer(De("Ciclo"), true);
        DateTime hoy = (DateTime)Get(l, "Hoy");
        IList pasados = Lista(l, "CiclosPasados");
        if (pasados.Count > 0)
        {
            object x = pasados[0];
            string nombre = (string)Call(Datos, "TextoDeCiclo", x);
            DateTime desde = (DateTime)Get(x, "Desde"), hasta = (DateTime)Get(x, "Hasta");
            UnPdf(carpetaSalida, "ciclo_pasado", DelCiclo(x), new[]
            {
                nombre + " \u00b7 " + "Del " + Fecha(desde) + " al " + Fecha(hasta),
                nombre + ", completo: del " + Fecha(desde) + " al " + Fecha(hasta) + ".",
                "no el cierre oficial del ciclo",
                "Ciclo completo",
                "no es un cierre congelado del ciclo"
            }, "Reporte de uso de laboratorios_" + S(desde) + " al " + S(hasta) + ".pdf");
        }
        else
        {
            Ver("hay un ciclo pasado para el PDF", false);
        }

        object enCurso = Get(l, "CicloEnCurso");
        if (enCurso != null)
        {
            DateTime desde = (DateTime)Get(enCurso, "Desde");
            string meses = (string)Call(Datos, "MinusculaInicial", Get(enCurso, "Nombre"));
            UnPdf(carpetaSalida, "este_ciclo", De("Ciclo"), new[]
            {
                "Este ciclo (en curso) \u00b7 Del " + Fecha(desde) + " al " + Fecha(hoy),
                "Este ciclo (" + meses + "), en curso hasta hoy: del " + Fecha(desde) + " al " + Fecha(hoy) + ".",
                "Del " + Fecha(desde) + " a hoy; el ciclo termina el " + Fecha((DateTime)Get(enCurso, "Hasta")) + "."
            }, null);
        }

        DateTime primero = hoy.AddDays(-20);
        UnPdf(carpetaSalida, "fechas_hasta_hoy", DesdeHastaHoy(primero), new[]
        {
            "Fechas elegidas, hasta hoy: del " + Fecha(primero) + " al " + Fecha(hoy) + ".",
            "El \u00faltimo d\u00eda es siempre hoy."
        }, null);

        // Los nombres del periodo (el renglon del reporte y del membrete del CSV).
        Func<object, string> nombreDe = p => (string)Call(Datos, "NombreDelPeriodo", Calc(p, Leer(p, false)));
        Ver("NombreDelPeriodo con 'Este mes': 'Este mes, hasta hoy'", nombreDe(De("Mes")) == "Este mes, hasta hoy");
        Ver("con 'Esta semana': 'Esta semana, hasta hoy'", nombreDe(De("Semana")) == "Esta semana, hasta hoy");
        Ver("con 'Ultimos 30 dias': '\u00daltimos 30 d\u00edas, hasta hoy'", nombreDe(De("Ultimos30")) == "\u00daltimos 30 d\u00edas, hasta hoy");
        Ver("con DesdeHastaHoy: 'Fechas elegidas, hasta hoy'", nombreDe(DesdeHastaHoy(primero)) == "Fechas elegidas, hasta hoy");
        Ver("con EntreFechas: 'Fechas elegidas'", nombreDe(EntreFechas(primero, hoy.AddDays(-2))) == "Fechas elegidas");
    }

    // -----------------------------------------------------------------------
    // Fechas (P4) y Consulta (P5)
    // -----------------------------------------------------------------------

    public static void Fechas()
    {
        object p = EntreFechas(D("2024-06-01"), D("2024-12-31"));
        object l = Leer(p, true);
        object r = Calc(p, l);
        object cifras = Get(r, "Cifras");
        int sesiones = ((IDictionary)Get(Get(l, "Asistencia"), "SesionPorId")).Values.Cast<object>().Count(s => (DateTime)Get(s, "Fecha") >= D("2024-06-01") && (DateTime)Get(s, "Fecha") <= D("2024-12-31"));
        L("  " + Get(r, "LineaResumen"));
        Ver("EntreFechas(2024-06-01, 2024-12-31): se lee de " + S(Get(l, "DesdeLectura")) + " a " + S(Get(l, "HastaLectura")), (DateTime)Get(l, "DesdeLectura") == D("2024-06-01") && (DateTime)Get(l, "HastaLectura") == D("2024-12-31"));
        object bitacorasBase = Sql("SELECT COUNT(DISTINCT rb.fk_codigo_accesos, rb.fk_usuario) FROM registros_bitacoras rb JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos "
            + "WHERE COALESCE(ca.fecha, DATE(ca.hora_registro)) BETWEEN '2024-06-01' AND '2024-12-31'");
        object sesionesBase = Sql("SELECT COUNT(*) FROM codigos_accesos ca WHERE COALESCE(ca.fecha, DATE(ca.hora_registro)) BETWEEN '2024-06-01' AND '2024-12-31'");
        Ver("  bitacoras registradas " + Get(cifras, "BitacorasRegistradas") + " = las de la base " + S(bitacorasBase) + " (11 en 3318), en " + sesiones + " sesiones = " + S(sesionesBase) + " (20)",
            (int)Get(cifras, "BitacorasRegistradas") == Convert.ToInt32(bitacorasBase, Inv) && sesiones == Convert.ToInt32(sesionesBase, Inv) && sesiones > 0);
        Ver("  asistencia y clases en '\u2014' (sin horarios entonces)", S(Get(Get(cifras, "Asistencia"), "Valor")) == "\u2014" && S(Get(Get(cifras, "ClasesQueSeDieronCifra"), "Valor")) == "\u2014");
        Ver("  lblResumen 'Del 01/06/2024 al 31/12/2024.' sin la frase de los 400 dias", S(Get(r, "LineaResumen")).StartsWith("Del 01/06/2024 al 31/12/2024. Le\u00eddo a las ") && !S(Get(r, "LineaResumen")).Contains("400"));
        Ver("  el log dice 'leido de 2024-06-01 a 2024-12-31'", LineasDelLog("Estadisticas: leido de 2024-06-01 a 2024-12-31").Count > 0);

        DateTime hoy = (DateTime)Get(l, "Hoy");
        object largo = DesdeHastaHoy(D("2024-06-01"));
        object rl = Calc(largo, Leer(largo, true));
        string desde = Fecha(hoy.AddDays(-399));
        L("  " + Get(rl, "LineaResumen"));
        Ver("'Del 01/06/2024 a hoy' (mas de 400 dias): se cuenta desde el " + desde, S(Get(rl, "LineaResumen")).StartsWith("Del " + desde + " al " + Fecha(hoy) + ". DigLab cuenta como mucho 400 d\u00edas seguidos: se cuenta desde el " + desde + "."));
        Ver("  y el combo dice 'Del 01/06/2024 a hoy'", (string)Call(Datos, "TextoDePeriodo", largo, null) == "Del 01/06/2024 a hoy");
    }

    public static void Consulta()
    {
        Type tConexion = T("DigLab.Conexion");
        object equipo = Call(Activator.CreateInstance(tConexion, true), "GetConexion");
        object[] args = { equipo, null };
        string problema = (string)T("DigLab.conexion.CuentaDeConsulta").GetMethod("Preparar", TI).Invoke(null, args);
        ((IDisposable)equipo).Dispose();
        if (problema != null)
        {
            Ver("se prepara la cuenta de solo consulta (" + problema + ")", false);
            return;
        }

        Type du = T("DigLab.conexion.Datos_User");
        tConexion.GetMethod("UsarCuentaDeSesion", TI).Invoke(null, new object[] { (string)args[1] });
        du.GetProperty("TipoUsuario", TI).SetValue(null, 4, null);
        try
        {
            Olvidar();
            object l = Leer(De("Ciclo"), true);
            IList pasados = Lista(l, "CiclosPasados");
            Ver("con digbit_consulta se leen los ciclos (" + pasados.Count + " pasados) y 'Este ciclo'", Get(l, "CicloEnCurso") != null || pasados.Count > 0);
            foreach (object k in pasados)
            {
                Ver("  '" + Call(Datos, "TextoDeCiclo", k) + "': nombre de sus meses", !(bool)Get(k, "NombreAplicado"));
                object r = Calc(DelCiclo(k), Leer(DelCiclo(k), false));
                Ver("  se calcula sin error (SoloConsulta " + S(Get(Get(r, "Filtro"), "SoloConsulta")) + ")", (bool)Get(Get(r, "Filtro"), "SoloConsulta"));
            }

            object viejas = EntreFechas(D("2024-06-01"), D("2024-12-31"));
            object rv = Calc(viejas, Leer(viejas, false));
            Ver("  y unas fechas de 2024 (" + Get(Get(rv, "Cifras"), "BitacorasRegistradas") + " bitacoras)", rv != null);
        }
        catch (Exception ex)
        {
            Ver("solo consulta sin error: " + ex.GetType().Name + ": " + ex.Message, false);
        }
        finally
        {
            tConexion.GetMethod("UsarCuentaDelEquipo", TI).Invoke(null, new object[0]);
            du.GetProperty("TipoUsuario", TI).SetValue(null, 3, null);
        }
    }
}
'@

Add-Type -TypeDefinition $codigo -ReferencedAssemblies System.Core
[ProbarPeriodos]::Cargar($Exe, $datos)
try {
    switch ($Modo) {
        'Foto' {
            if (-not $Salida) { throw 'Foto necesita -Salida <archivo>.' }
            [ProbarPeriodos]::Foto($Salida)
            if ($Comparar) { [ProbarPeriodos]::CompararFotos($Comparar, $Salida) }
        }
        'Ciclos' { $null = [ProbarPeriodos]::Ciclos($Tsv, -not [string]::IsNullOrWhiteSpace($CadenaConexion)) }
        'Ciclo' { [ProbarPeriodos]::Ciclo($Salida, $Comparar) }
        'Pdf' {
            if (-not $Salida) { throw 'Pdf necesita -Salida <carpeta>.' }
            [ProbarPeriodos]::Pdf($Salida)
        }
        'Fechas' { [ProbarPeriodos]::Fechas() }
        'Consulta' { [ProbarPeriodos]::Consulta() }
    }
}
catch {
    Write-Host ("ERROR: " + $_.Exception.GetBaseException().ToString()) -ForegroundColor Red
    Write-Host ([ProbarPeriodos]::Resultado())
    exit 2
}

Write-Host ([ProbarPeriodos]::Resultado())
if ([ProbarPeriodos]::Fallos -gt 0) { exit 1 }
