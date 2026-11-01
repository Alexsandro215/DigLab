<#
.SYNOPSIS
    Prueba la ficha del docente ("Ver todo lo suyo", 2026-10-31) contra una base LOCAL de pruebas
    (127.0.0.1, puerto 3317 o 3318). Nunca contra la de la escuela. SOLO LEE.

.DESCRIPTION
    Carga DigBit.exe por reflexion. Nunca ensena una ventana que se active: la ficha va dentro
    (TopLevel = false) de un formulario que no se activa, fuera de la pantalla y transparente, y
    sus fotos se sacan con DrawToBitmap (nunca la pantalla). En la salida no van nombres ni
    matriculas: los docentes van por su id.

      -Modo Logica    T1 a T8 sin pantalla: docente fijo contra Estadisticas (T1), sin clases (T2),
                      sumas (T3), listas nuevas (T4), igual que Mis grupos (T5), igual que Faltas
                      (T6), "Para revisar" (T7) y un ciclo pasado leido de dos formas (T8).
      -Modo Pantalla  La ficha de verdad: cada pestana, "Ver >", el PDF (T11), "Ver en Estadisticas",
                      la lista dia por dia, lo que depende de las migraciones (T9) y el boton de
                      Docentes (parte de T13); con fotos a 1920x1040, 1366x728 y 1000x560.
      -Modo Consulta  T10: con la cuenta digbit_consulta, 10 fichas con todas sus pestanas, sin PDF
                      ni archivos, sin botones que escriban ni codigos, y CHECKSUM igual antes y despues.
      -Modo Kiosco    T12 (en otro proceso, con DIGBIT_MODO_KIOSCO=1): sin PDF ni CSV, Ctrl+C no copia y,
                      si el portapapeles estaba vacio, queda vacio al cerrar.
      -Modo Todo      Los cuatro, cada uno en su proceso.

    En todos: CHECKSUM TABLE de las tablas que la ficha lee, antes y despues (nada cambio).

.EXAMPLE
    powershell -NoProfile -File deploy\probar_ficha_docente.ps1 -Modo Todo -Exe ...\ficha1\DigBit.exe -Base teschi_demo
#>
[CmdletBinding()]
param(
    [ValidateSet('Logica', 'Pantalla', 'Consulta', 'Kiosco', 'Todo')]
    [string]$Modo = 'Todo',
    [Parameter(Mandatory = $true)] [string]$Exe,
    [ValidatePattern('^(teschi_(demo|otru)|escuela_prueba)$')] [string]$Base = 'teschi_demo',
    [ValidateSet(3317, 3318)] [int]$Puerto = 3317,
    [string]$Salida = 'C:\Users\Usuario\AppData\Local\Temp\claude\a--Digbit-2-0-Produccion\a367f222-a048-4689-bee2-1427e868f141\scratchpad\ficha',
    [switch]$SinFotos,
    # Solo para las fotos y la pantalla: el docente (idusuarios); sin el, el de mas clases que se dieron.
    [int]$Docente = 0
)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $Salida | Out-Null
$cadena = "Database=$Base;Server=127.0.0.1;Port=$Puerto;User Id=root;Password=;Connection Timeout=5"
if ($cadena -match 'teschi\.edu\.mx') { throw 'Nunca contra la base de la escuela.' }

if ($Modo -eq 'Todo') {
    $resumen = @()
    foreach ($m in 'Logica', 'Pantalla', 'Consulta', 'Kiosco') {
        $args2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Modo', $m, '-Exe', $Exe, '-Base', $Base, '-Puerto', $Puerto, '-Salida', $Salida)
        if ($SinFotos) { $args2 += '-SinFotos' }
        if ($Docente -ne 0) { $args2 += @('-Docente', $Docente) }
        $o = & powershell @args2 2>&1 | ForEach-Object { "$_" }
        $o | Out-File -Encoding utf8 (Join-Path $Salida ("resultado_" + $m + "_" + $Base + "_" + $Puerto + ".txt"))
        $linea = @($o | Where-Object { $_ -match '^RESULTADO' }) | Select-Object -Last 1
        $fallas = @($o | Where-Object { $_ -match '^\[FALLO\]|^ERROR' })
        $resumen += ("{0,-9} {1}{2}" -f $m, $(if ($linea) { $linea } else { '(sin RESULTADO)' }), $(if ($fallas.Count) { "`n     " + ($fallas -join "`n     ") } else { '' }))
    }

    $resumen
    $resumen | Out-File -Encoding utf8 (Join-Path $Salida ("resumen_" + $Base + "_" + $Puerto + ".txt"))
    return
}

$env:DIGBIT_CONNECTION_STRING = $cadena
$env:DIGBIT_MODO_KIOSCO = $(if ($Modo -eq 'Kiosco') { '1' } else { 'false' })
$env:DIGBIT_CARPETA_DATOS = Join-Path $env:TEMP ('digbit_ficha_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $env:DIGBIT_CARPETA_DATOS | Out-Null
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
# Como DigBit (Program.cs): estilos visuales y el texto con GDI (sin esto las etiquetas se dibujan con
# GDI+ y las fotos no son como la pantalla de verdad: lineas cortadas a la mitad, otros anchos).
try { [System.Windows.Forms.Application]::EnableVisualStyles(); [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false) } catch { }

$codigo = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public class FormQuietoFicha : Form
{
    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80;
            return cp;
        }
    }
}

public static class PruebaFicha
{
    private const BindingFlags TI = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static Assembly asm;
    private static string carpeta;
    private static string carpetaDatos;
    private static readonly StringBuilder log = new StringBuilder();
    public static int Oks, Fallos;
    private static readonly List<string> excepciones = new List<string>();
    private static Type Datos;
    private static Type tFichaDatos;

    // -----------------------------------------------------------------
    // Reflexion
    // -----------------------------------------------------------------

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

    private static void Set(object o, string n, object v)
    {
        Type t = o is Type ? (Type)o : o.GetType();
        object instancia = o is Type ? null : o;
        while (t != null)
        {
            FieldInfo f = t.GetField(n, TI | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(instancia, v); return; }
            PropertyInfo p = t.GetProperty(n, TI | BindingFlags.DeclaredOnly);
            if (p != null) { p.SetValue(instancia, v, null); return; }
            t = t.BaseType;
        }
        throw new Exception("No hay " + n);
    }

    private static object Call(object o, string n, params object[] a)
    {
        Type t = o is Type ? (Type)o : o.GetType();
        object instancia = o is Type ? null : o;
        while (t != null)
        {
            MethodInfo m = t.GetMethods(TI | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == n && x.GetParameters().Length == a.Length
                && x.GetParameters().Select((p, i) => a[i] == null || p.ParameterType.IsInstanceOfType(a[i]) || (p.ParameterType.IsByRef)).All(b => b));
            if (m != null)
            {
                try { return m.Invoke(instancia, a); }
                catch (TargetInvocationException e) { throw e.InnerException; }
            }
            t = t.BaseType;
        }
        throw new Exception("No hay metodo " + n + "(" + a.Length + ")");
    }

    private static IList Lista(object o, string n) { return (IList)Get(o, n); }
    private static int I(object o, string n) { return Convert.ToInt32(Get(o, n), Inv); }
    private static bool B(object o, string n) { return (bool)Get(o, n); }
    private static string S(object o) { return o == null ? "null" : Convert.ToString(o, Inv); }
    private static string Fecha(DateTime d) { return d.ToString("dd/MM/yyyy", Inv); }

    private static void L(string texto) { log.AppendLine(texto); }

    private static void Ver(string que, bool bien)
    {
        if (bien) { Oks++; } else { Fallos++; }
        L((bien ? "[OK   ] " : "[FALLO] ") + que);
    }

    public static string Resultado()
    {
        Ver("sin excepciones en el hilo de la pantalla (" + excepciones.Count + ")", excepciones.Count == 0);
        foreach (string e in excepciones) { L("  " + e); }
        L("");
        L("RESULTADO: " + Oks + " bien, " + Fallos + " fallos");
        return log.ToString();
    }

    private static void Bombear(double segundos)
    {
        DateTime fin = DateTime.Now.AddSeconds(segundos);
        while (DateTime.Now < fin) { Application.DoEvents(); Thread.Sleep(15); }
    }

    private static bool Esperar(Func<bool> condicion, double segundos)
    {
        DateTime fin = DateTime.Now.AddSeconds(segundos);
        while (DateTime.Now < fin)
        {
            Application.DoEvents();
            if (condicion()) { return true; }
            Thread.Sleep(20);
        }
        return condicion();
    }

    // -----------------------------------------------------------------
    // Carga y lo comun
    // -----------------------------------------------------------------

    public static void Cargar(string exe, string datos)
    {
        carpeta = Path.GetDirectoryName(exe);
        carpetaDatos = datos;
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string ruta = Path.Combine(carpeta, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(ruta) ? Assembly.LoadFrom(ruta) : null;
        };
        Application.ThreadException += (s, e) => excepciones.Add(e.Exception.ToString());
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        asm = Assembly.LoadFrom(exe);
        Datos = T("DigBit.conexion.EstadisticasDatos");
        tFichaDatos = T("DigBit.conexion.FichaDocenteDatos");
        T("DigBit.Infraestructura.Log").GetMethod("Configurar", TI).Invoke(null, new object[] { datos, "ficha" });
        Type du = T("DigBit.conexion.Datos_User");
        du.GetMethod("SetUser", TI).Invoke(null, new object[] { "ADMPRUEBA" });
        du.GetProperty("TipoUsuario", TI).SetValue(null, 3, null);
    }

    private static object Periodo(string fabrica, params object[] a)
    {
        return Call(T("DigBit.conexion.PeriodoElegido"), fabrica, a);
    }

    private static object De(string tipo) { return Periodo("De", Enum.Parse(T("DigBit.conexion.TipoPeriodo"), tipo)); }
    private static object Leer(object p, bool forzar) { return Call(Datos, "Leer", p, forzar); }
    private static void Olvidar() { Call(Datos, "Olvidar"); }
    private static object Resolver(object p, object l, int d, bool fijo) { return Call(Datos, "Resolver", p, l, 0, d, 0, fijo); }
    private static object ResolverComoEstadisticas(object p, object l, int d) { return Call(Datos, "Resolver", p, l, 0, d, 0); }
    private static object Calcular(object l, object f) { return Call(Datos, "Calcular", l, f); }

    private static object Profesores()
    {
        return Call(Activator.CreateInstance(T("DigBit.conexion.ProfesoresDatos")), "Listar");
    }

    private static object ProfesorDe(IList profesores, int id)
    {
        foreach (object p in profesores) { if (I(p, "Id") == id) { return p; } }
        return null;
    }

    private static object Enlaces()
    {
        try { return Call(T("DigBit.conexion.EnlacesSiia"), "Leer"); }
        catch (Exception ex) { L("  (sin enlaces: " + ex.GetType().Name + ")"); return Get(T("DigBit.conexion.LecturaEnlaces"), "Ninguna"); }
    }

    private static object PorAsignar()
    {
        try { return Call(Activator.CreateInstance(T("DigBit.conexion.HorariosDatos"), true), "LeerHorasPorAsignar"); }
        catch (Exception ex) { L("  (sin falta asignar: " + ex.GetType().Name + ")"); return null; }
    }

    private static object Armar(object l, object r, int d, object profesor, object enlaces, object porAsignar)
    {
        return tFichaDatos.GetMethod("Armar", TI).Invoke(null, new object[] { l, r, d, profesor, enlaces, porAsignar });
    }

    /// <summary>Las opciones del combo Periodo (sin "Elegir fechas") y unas fechas de hoy - 120 a hoy.</summary>
    private static List<object> Periodos(object l)
    {
        List<object> todos = new List<object>();
        foreach (object p in (IList)Call(Datos, "OpcionesDePeriodo", l)) { todos.Add(p); }
        DateTime hoy = (DateTime)Get(l, "Hoy");
        todos.Add(Periodo("EntreFechas", hoy.AddDays(-120), hoy));
        return todos;
    }

    private static string NombreDe(object p, object l) { return (string)Call(Datos, "TextoDePeriodo", p, l); }

    // -----------------------------------------------------------------
    // Volcado (todos los campos y propiedades publicos, como JSON)
    // -----------------------------------------------------------------

    private static string Volcado(object o)
    {
        StringBuilder sb = new StringBuilder();
        Volcar(sb, o, 0);
        return sb.ToString();
    }

    private static void Volcar(StringBuilder sb, object o, int prof)
    {
        if (o == null) { sb.Append("null"); return; }
        Type t = o.GetType();
        if (o is string) { sb.Append('"').Append((string)o).Append('"'); return; }
        if (t.IsPrimitive || o is decimal) { sb.Append(Convert.ToString(o, Inv)); return; }
        if (o is DateTime) { sb.Append(((DateTime)o).ToString("o", Inv)); return; }
        if (o is TimeSpan) { sb.Append(((TimeSpan)o).ToString()); return; }
        if (t.IsEnum) { sb.Append(o.ToString()); return; }
        if (o is Color) { sb.Append(((Color)o).ToArgb()); return; }
        if (o is IDictionary)
        {
            sb.Append('{');
            List<string> partes = new List<string>();
            foreach (DictionaryEntry e in (IDictionary)o) { StringBuilder x = new StringBuilder(); Volcar(x, e.Key, prof + 1); x.Append(':'); Volcar(x, e.Value, prof + 1); partes.Add(x.ToString()); }
            partes.Sort(StringComparer.Ordinal);
            sb.Append(string.Join(",", partes)).Append('}');
            return;
        }
        if (o is IEnumerable)
        {
            sb.Append('[');
            bool primero = true;
            foreach (object x in (IEnumerable)o) { if (!primero) { sb.Append(','); } Volcar(sb, x, prof + 1); primero = false; }
            sb.Append(']');
            return;
        }
        if (prof > 8) { sb.Append("..."); return; }
        sb.Append('{');
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            sb.Append(f.Name).Append(':');
            Volcar(sb, f.GetValue(o), prof + 1);
            sb.Append(',');
        }
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            sb.Append(p.Name).Append(':');
            Volcar(sb, p.GetValue(o, null), prof + 1);
            sb.Append(',');
        }
        sb.Append('}');
    }

    private static string LoQueSeCompara(object r)
    {
        object g = Get(r, "Graficas");
        return Volcado(new object[] { Get(r, "Cifras"), Get(r, "Clases"), Get(r, "Docentes"), Get(r, "Equipos"), Get(g, "PorSemana"), Get(g, "PorMes") });
    }

    private static string PrimeraDiferencia(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) { i++; }
        int desde = Math.Max(0, i - 80);
        return "en " + i + ": ..." + a.Substring(desde, Math.Min(160, a.Length - desde)) + "  vs  ..." + b.Substring(desde, Math.Min(160, b.Length - desde));
    }

    // =================================================================
    // T1 a T8 (sin pantalla)
    // =================================================================

    private static int t5Listas, t5Clases;

    public static void Logica()
    {
        Olvidar();
        IList profesores = (IList)Profesores();
        object enlaces = Enlaces();
        object porAsignar = PorAsignar();
        L("Docentes en la base: " + profesores.Count + "; db/25 (falta asignar): " + (porAsignar != null));
        object l0 = Leer(De("Ciclo"), true);
        DateTime hoy = (DateTime)Get(l0, "Hoy");
        List<object> periodos = Periodos(l0);
        int comparados = 0;
        foreach (object p in periodos)
        {
            object l = Leer(p, false);
            object general = Calcular(l, Resolver(p, l, 0, false));
            List<int> docentes = Lista(general, "OpcionesDocente").Cast<object>().Select(o => I(o, "Id")).Where(i => i != 0).ToList();
            object rg = general;
            L("\n===== " + NombreDe(p, l) + ": del " + Fecha((DateTime)Get(rg, "Desde")) + " al " + Fecha((DateTime)Get(rg, "Hasta")) + ", " + docentes.Count + " docentes con clases =====");
            object calendario = Call(Activator.CreateInstance(T("DigBit.conexion.HorariosDatos"), true), "LeerCalendario", (DateTime)Get(rg, "Desde"), (DateTime)Get(rg, "Hasta"), false);
            foreach (int d in docentes)
            {
                object fijo = Calcular(l, Resolver(p, l, d, true));
                object comoEst = Calcular(l, ResolverComoEstadisticas(p, l, d));
                string a = LoQueSeCompara(fijo);
                string b = LoQueSeCompara(comoEst);
                comparados++;
                if (a != b) { Ver("T1 docente " + d + ": docente fijo = Estadisticas con el elegido (" + a.Length + " car.) " + PrimeraDiferencia(a, b), false); }
                else { Oks++; }
                object datos = Armar(l, fijo, d, ProfesorDe(profesores, d), enlaces, porAsignar);
                Sumas(d, fijo, datos);
                ListasNuevas(d, fijo, datos);
                ParaRevisar(d, fijo, datos, porAsignar);
                ComoFaltas(d, fijo, datos, calendario);
                if (S(Get(Get(fijo, "Filtro"), "Tipo")) == "Ciclo")
                {
                    ComoMisGrupos(d, l, fijo);
                }

                if (S(Get(Get(fijo, "Filtro"), "Tipo")) == "CicloPasado")
                {
                    AyudaDelCicloPasado(d, l, fijo, general);
                }
            }

            L("  T1, T3, T4, T6 y T7 de " + docentes.Count + " docentes" + (S(Get(Get(general, "Filtro"), "Tipo")) == "Ciclo" ? "; T5 (Mis grupos) de " + t5Listas + " listas y " + t5Clases + " clases" : "") + " (los que salen bien no se listan)");
            SinClases(p, l, profesores, docentes, enlaces);
        }

        L("\nT1: " + comparados + " docente-periodo comparados.");
        CicloPasado(l0, profesores, enlaces);
    }

    /// <summary>
    /// La ayuda del Periodo de la ficha con un ciclo pasado: sus clases ("N clases suyas de M del ciclo"),
    /// no las de toda la escuela; Estadisticas (lo general) sigue con "completo (M clases)".
    /// </summary>
    private static void AyudaDelCicloPasado(int d, object l, object fijo, object general)
    {
        string ayuda = (string)Call(Datos, "AyudaDelPeriodo", fijo, l);
        int suyas = Lista(fijo, "Clases").Count;
        int delCiclo = I(fijo, "ClasesDelPeriodo");
        string esperado = (string)Call(Datos, "Contar", suyas, "clase suya", "clases suyas") + " de " + (string)Call(Datos, "Miles", delCiclo) + " del ciclo)";
        string deTodos = (string)Call(Datos, "AyudaDelPeriodo", general, l);
        bool bien = ayuda != null && ayuda.Contains(esperado) && deTodos != null && deTodos.Contains("(" + (string)Call(Datos, "Contar", delCiclo, "clase", "clases") + ")");
        if (!bien) { Ver("ayuda del ciclo pasado, docente " + d + ": '" + ayuda + "' (se esperaba '" + esperado + "'); general: '" + deTodos + "'", false); } else { Oks++; }
    }

    /// <summary>T3: las sumas de la fila, de sus clases dia por dia, de los tramos, del mes y del "Mostrar".</summary>
    private static void Sumas(int d, object r, object datos)
    {
        object c = Get(r, "Cifras");
        object f = Get(datos, "Fila");
        IList dias = Lista(datos, "Dias");
        List<string> mal = new List<string>();
        int seDieron = dias.Cast<object>().Count(x => B(x, "SeDio"));
        int sinRegistro = dias.Cast<object>().Count(x => B(x, "SinRegistro"));
        int canceladas = dias.Cast<object>().Count(x => B(x, "Cancelada"));
        if (f == null) { mal.Add("sin su fila"); }
        else
        {
            if (I(f, "SeDieron") != seDieron || seDieron != I(c, "ClasesQueSeDieron")) { mal.Add("se dieron " + I(f, "SeDieron") + "/" + seDieron + "/" + I(c, "ClasesQueSeDieron")); }
            if (I(f, "SinRegistro") != sinRegistro || sinRegistro != I(c, "ClasesSinRegistro")) { mal.Add("sin registro " + I(f, "SinRegistro") + "/" + sinRegistro + "/" + I(c, "ClasesSinRegistro")); }
            if (I(f, "Canceladas") != canceladas || canceladas != I(c, "Canceladas")) { mal.Add("canceladas " + I(f, "Canceladas") + "/" + canceladas + "/" + I(c, "Canceladas")); }
            if (I(datos, "SinRegistro") != I(f, "SinRegistro") || I(datos, "Canceladas") != I(f, "Canceladas") || I(datos, "SeDieron") != I(f, "SeDieron")
                || I(datos, "Todas") != Lista(f, "SusClasesDia").Count
                || I(datos, "SinRegistro") + I(datos, "Canceladas") + I(datos, "SeDieron") + I(datos, "AunNoONoCuentan") != I(datos, "Todas"))
            {
                mal.Add("las cuentas de Mostrar");
            }
        }

        object g = Get(r, "Graficas");
        foreach (string cual in new[] { "PorSemana", "PorMes" })
        {
            IList tramos = Lista(g, cual);
            int sd = tramos.Cast<object>().Sum(t => I(t, "SeDieron"));
            int sr = tramos.Cast<object>().Sum(t => I(t, "SinRegistro"));
            int ca = tramos.Cast<object>().Sum(t => I(t, "Canceladas"));
            if (sd != I(c, "ClasesQueSeDieron") || sr != I(c, "ClasesSinRegistro") || ca != I(c, "Canceladas")) { mal.Add(cual + " " + sd + "/" + sr + "/" + ca); }
        }

        IDictionary mes = (IDictionary)Get(datos, "Mes");
        int rojos = 0, faltan = 0;
        foreach (DictionaryEntry e in mes) { int x = I(e.Value, "Faltan"); faltan += x; rojos += x > 0 ? 1 : 0; }
        int diasRojos = dias.Cast<object>().Where(x => B(x, "SinRegistro")).Select(x => ((DateTime)Get(x, "Fecha")).Date).Distinct().Count();
        if (faltan != sinRegistro || rojos != diasRojos) { mal.Add("el mes: " + faltan + " faltan en " + rojos + " dias rojos (" + sinRegistro + " en " + diasRojos + ")"); }
        if (mal.Count > 0) { Ver("T3 docente " + d + ": " + string.Join("; ", mal), false); } else { Oks++; }
    }

    /// <summary>T4: las filas de ReportesDocentes y AsistenciasDadas son las que se cuentan.</summary>
    private static void ListasNuevas(int d, object r, object datos)
    {
        object c = Get(r, "Cifras");
        object f = Get(datos, "Fila");
        List<string> mal = new List<string>();
        int susReportes = f == null ? 0 : I(f, "SusReportes");
        if (Lista(datos, "SusReportes").Count != susReportes) { mal.Add("sus reportes " + Lista(datos, "SusReportes").Count + " de " + susReportes); }
        if (Lista(r, "ReportesDocentes").Count != I(c, "FallasDeDocentes")) { mal.Add("reportes " + Lista(r, "ReportesDocentes").Count + " de " + I(c, "FallasDeDocentes")); }
        int dio = f == null ? 0 : I(f, "AsistenciasQueDio");
        if (Lista(datos, "SusDadas").Count != dio) { mal.Add("dadas " + Lista(datos, "SusDadas").Count + " de " + dio); }
        if (mal.Count > 0) { Ver("T4 docente " + d + ": " + string.Join("; ", mal), false); } else { Oks++; }
    }

    /// <summary>T7: cada numero de "Para revisar" es el de su fuente, y cada regla sale cuando debe.</summary>
    private static void ParaRevisar(int d, object r, object datos, object porAsignar)
    {
        IList pendientes = Lista(datos, "Pendientes");
        object f = Get(datos, "Fila");
        List<string> mal = new List<string>();
        Func<string, List<object>> regla = n => pendientes.Cast<object>().Where(p => (string)Get(p, "Regla") == n).ToList();
        int sinRegistro = f == null ? 0 : I(f, "SinRegistro");
        List<object> r1 = regla("R1");
        if ((sinRegistro > 0) != (r1.Count == 1) || (r1.Count == 1 && I(r1[0], "Numero") != sinRegistro)) { mal.Add("R1"); }
        foreach (object p in regla("R2"))
        {
            object clase = Lista(r, "Clases").Cast<object>().FirstOrDefault(x => I(x, "ClaseId") == I(p, "ClaseId"));
            if (clase == null || I(clase, "Bajo") != I(p, "Numero") || I(p, "Numero") <= 0) { mal.Add("R2 clase " + I(p, "ClaseId")); }
        }
        int conBajo = Lista(r, "Clases").Cast<object>().Count(x => I(x, "Bajo") > 0);
        if (regla("R2").Count != Math.Min(3, conBajo)) { mal.Add("R2 cuantas " + regla("R2").Count + " de " + conBajo); }
        object susHoras = Get(datos, "PorAsignar");
        int minutos = susHoras == null ? 0 : ((IList)susHoras).Cast<object>().Sum(x => I(x, "Minutos"));
        List<object> r3 = regla("R3");
        if ((minutos > 0) != (r3.Count == 1) || (r3.Count == 1 && I(r3[0], "Numero") != minutos)) { mal.Add("R3 (" + minutos + " min)"); }
        int repetidos = Lista(r, "Equipos").Cast<object>().Count(e => S(Get(e, "Tipo")) == "Equipo" && B(e, "Repetido"));
        List<object> r4 = regla("R4");
        if ((repetidos > 0) != (r4.Count == 1) || (r4.Count == 1 && I(r4[0], "Numero") != repetidos)) { mal.Add("R4"); }
        int reportes = Lista(datos, "SusReportes").Count;
        List<object> r5 = regla("R5");
        if ((reportes > 0 && B(r, "ConReportesDocentes")) != (r5.Count == 1) || (r5.Count == 1 && I(r5[0], "Numero") != reportes)) { mal.Add("R5"); }
        int dio = f == null ? 0 : I(f, "AsistenciasQueDio");
        List<object> r6 = regla("R6");
        if ((dio > 0 && B(r, "ConDadas")) != (r6.Count == 1) || (r6.Count == 1 && I(r6[0], "Numero") != dio)) { mal.Add("R6"); }
        int canceladas = f == null ? 0 : I(f, "Canceladas");
        List<object> r7 = regla("R7");
        if ((canceladas > 0) != (r7.Count == 1) || (r7.Count == 1 && I(r7[0], "Numero") != canceladas)) { mal.Add("R7"); }
        int noCuentan = Lista(datos, "Dias").Cast<object>().Count(x => S(Get(x, "Estado")) == "NoContaba");
        List<object> r8 = regla("R8");
        if ((noCuentan > 0) != (r8.Count == 1) || (r8.Count == 1 && I(r8[0], "Numero") != noCuentan)) { mal.Add("R8"); }
        bool alarma = pendientes.Cast<object>().Any(p => S(Get(p, "Tono")) == "Rojo" || S(Get(p, "Tono")) == "Naranja");
        bool r10 = regla("R10").Count == 1;
        if (r10 != (!alarma && f != null && I(f, "QueContaban") > 0)) { mal.Add("R10"); }
        if (mal.Count > 0) { Ver("T7 docente " + d + ": " + string.Join(", ", mal), false); } else { Oks++; }
    }

    /// <summary>
    /// T6: el mes de la ficha = sus clases de LeerCalendario (Horarios > Faltas) agrupadas por dia con
    /// ResumenDia.Contar. Con un ciclo ("Este ciclo" o uno pasado) solo son del periodo las clases del
    /// ciclo, como en Estadisticas: las de otro ciclo que caen en esas fechas se anotan aparte (por diseno).
    /// </summary>
    private static void ComoFaltas(int d, object r, object datos, object calendario)
    {
        Type tResumen = T("DigBit.ResumenDia");
        Dictionary<DateTime, object> resumenes = new Dictionary<DateTime, object>();
        bool esCiclo = B(Get(r, "Filtro"), "EsCiclo");
        HashSet<int> delPeriodo = new HashSet<int>(Lista(r, "Clases").Cast<object>().Select(x => I(x, "ClaseId")));
        HashSet<DateTime> deOtroCiclo = new HashSet<DateTime>();
        foreach (object c in Lista(calendario, "Clases"))
        {
            if (I(c, "ProfesorId") != d) { continue; }
            if (esCiclo && !delPeriodo.Contains(I(c, "ClaseId"))) { deOtroCiclo.Add(((DateTime)Get(c, "Fecha")).Date); continue; }
            DateTime dia = ((DateTime)Get(c, "Fecha")).Date;
            object resumen;
            if (!resumenes.TryGetValue(dia, out resumen)) { resumen = Activator.CreateInstance(tResumen); resumenes[dia] = resumen; }
            Call(resumen, "Contar", Get(c, "Estado"));
        }

        IDictionary mes = (IDictionary)Get(datos, "Mes");
        Func<object, string> cuentas = x => x == null ? "-" : I(x, "Faltan") + "/" + I(x, "ConBitacoras") + "/" + I(x, "SinBitacorasConAsistencia") + "/" + I(x, "PorVenir") + "/" + I(x, "NoCuentan") + "/" + I(x, "Canceladas");
        List<string> mal = new List<string>();
        foreach (DateTime dia in resumenes.Keys.Union(mes.Keys.Cast<DateTime>()).OrderBy(x => x))
        {
            object a = resumenes.ContainsKey(dia) ? resumenes[dia] : null;
            object b = mes.Contains(dia) ? mes[dia] : null;
            if (cuentas(a) != cuentas(b)) { mal.Add(Fecha(dia) + " Faltas " + cuentas(a) + " ficha " + cuentas(b)); }
        }

        if (mal.Count > 0) { Ver("T6 docente " + d + ": " + mal.Count + " dias distintos: " + string.Join("; ", mal.Take(4)), false); } else { Oks++; }
        if (deOtroCiclo.Count > 0)
        {
            L("  (T6 por diseno, docente " + d + ": " + deOtroCiclo.Count + " dias con clases suyas de otro ciclo en estas fechas, que Faltas ensena y el ciclo no cuenta)");
        }
    }

    /// <summary>T5: cada clase suya activa y vigente hasta 30 dias atras, leida como Mis grupos (DeLaClase) = la lista de la lectura.</summary>
    private static void ComoMisGrupos(int d, object l, object r)
    {
        DateTime hoy = (DateTime)Get(l, "Hoy");
        object a = Get(l, "Asistencia");
        IDictionary listas = (IDictionary)Get(l, "Listas");
        IDictionary tramos = (IDictionary)Call(Datos, "TramosDeHoy", Get(l, "Calendario"), hoy);
        object asistencia = Activator.CreateInstance(T("DigBit.conexion.AsistenciaDatos"));
        int clases = 0;
        foreach (object c in Lista(a, "Clases"))
        {
            int id = I(c, "Id");
            if (I(c, "ProfesorId") != d || (DateTime)Get(c, "VigenteHasta") < hoy.AddDays(-30) || !listas.Contains(id)) { continue; }
            object tramo = tramos.Contains(id) ? tramos[id] : null;
            object deMisGrupos = Call(asistencia, "DeLaClase", id, tramo == null ? (TimeSpan?)null : (TimeSpan)Get(tramo, "Inicio"), tramo == null ? (TimeSpan?)null : (TimeSpan)Get(tramo, "Fin"));
            string x = ListaEnTexto(deMisGrupos);
            string y = ListaEnTexto(listas[id]);
            clases++;
            t5Listas++;
            if (x != y) { Ver("T5 docente " + d + ", clase " + id + ": Mis grupos = la lectura " + PrimeraDiferencia(x, y), false); } else { Oks++; }
        }

        // Con Este ciclo, Entro y Veces de cada par = las cuentas de ResumenAsistencia.De de su lista entera.
        Type tResumen = T("DigBit.conexion.ResumenAsistencia");
        MethodInfo de = tResumen.GetMethods().First(m => m.Name == "De" && m.GetParameters().Length == 1);
        foreach (object fila in Lista(r, "Clases"))
        {
            int id = I(fila, "ClaseId");
            if (!listas.Contains(id)) { continue; }
            object resumen = de.Invoke(null, new[] { listas[id] });
            t5Clases++;
            Dictionary<int, object> porAlumno = Lista(resumen, "Alumnos").Cast<object>().ToDictionary(x => I(Get(x, "Alumno"), "Id"));
            List<string> mal = new List<string>();
            foreach (object par in Lista(fila, "Lista"))
            {
                object ra;
                if (!porAlumno.TryGetValue(I(par, "AlumnoId"), out ra)) { mal.Add("falta " + I(par, "AlumnoId")); continue; }
                int asis = I(ra, "Asistencias");
                int veces = asis + I(ra, "Faltas");
                if (I(par, "Entro") != asis || I(par, "Veces") != veces) { mal.Add(I(par, "AlumnoId") + ": " + I(par, "Entro") + "/" + I(par, "Veces") + " vs " + asis + "/" + veces); }
            }

            if (mal.Count > 0) { Ver("T5 docente " + d + ", clase " + id + " (Este ciclo): Entro y Veces = ResumenAsistencia.De: " + string.Join("; ", mal.Take(3)), false); } else { Oks++; }
        }
    }

    private static string ListaEnTexto(object lista)
    {
        StringBuilder sb = new StringBuilder();
        foreach (object dia in Lista(lista, "Dias")) { sb.Append(Fecha((DateTime)Get(dia, "Fecha"))).Append(B(dia, "EnCurso") ? "*" : "").Append(','); }
        sb.Append('|');
        foreach (object al in Lista(lista, "Alumnos"))
        {
            sb.Append(I(al, "Id")).Append(':');
            foreach (string conjunto in new[] { "Presente", "Retiradas", "SinBitacora" })
            {
                sb.Append(string.Join(".", ((IEnumerable)Get(al, conjunto)).Cast<DateTime>().OrderBy(x => x).Select(Fecha))).Append(';');
            }
            sb.Append(string.Join(".", ((IDictionary)Get(al, "Dadas")).Keys.Cast<DateTime>().OrderBy(x => x).Select(Fecha))).Append(';');
            sb.Append(Fecha((DateTime)Get(al, "Entro"))).Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>T2: un docente sin clases en el periodo nunca ensena lo de la escuela; sus bitacoras son las de sus sesiones.</summary>
    private static void SinClases(object p, object l, IList profesores, List<int> conClases, object enlaces)
    {
        object a = Get(l, "Asistencia");
        List<int> sin = profesores.Cast<object>().Select(x => I(x, "Id")).Where(i => !conClases.Contains(i)).Take(6).ToList();
        sin.Add(987654);
        int bien = 0;
        foreach (int d in sin)
        {
            object r = Calcular(l, Resolver(p, l, d, true));
            DateTime desde = (DateTime)Get(r, "Desde");
            DateTime hasta = (DateTime)Get(r, "Hasta");
            IDictionary porSesion = (IDictionary)Get(a, "BitacorasPorSesion");
            HashSet<long> pares = new HashSet<long>();
            foreach (object s in ((IDictionary)Get(a, "SesionPorId")).Values)
            {
                object doc = Get(s, "DocenteId");
                DateTime fecha = (DateTime)Get(s, "Fecha");
                if (doc == null || (int)doc != d || fecha < desde || fecha > hasta || !porSesion.Contains(I(s, "Id"))) { continue; }
                foreach (object b in (IList)porSesion[I(s, "Id")]) { pares.Add(((long)I(s, "Id") << 32) | (uint)I(b, "AlumnoId")); }
            }

            object datos = Armar(l, r, d, ProfesorDe(profesores, d), enlaces, null);
            object c = Get(r, "Cifras");
            bool ok = I(Get(r, "Filtro"), "Docente") == d && Lista(r, "Docentes").Count == 0 && Lista(r, "Clases").Count == 0 && I(c, "ClasesQueContaban") == 0
                && S(Get(datos, "SinClases")).StartsWith("No tuvo clases del horario", StringComparison.Ordinal) && I(c, "BitacorasRegistradas") == pares.Count;
            if (!ok)
            {
                Ver("T2 docente " + d + " sin clases: filtro " + I(Get(r, "Filtro"), "Docente") + ", " + Lista(r, "Clases").Count + " clases, bitacoras " + I(c, "BitacorasRegistradas")
                    + " (sus sesiones: " + pares.Count + "), '" + S(Get(datos, "SinClases")) + "'", false);
            }
            else { bien++; Oks++; }
        }

        // La guarda: cifras de la escuela con su nombre, nunca.
        object general = Calcular(l, Resolver(p, l, 0, false));
        bool lanzo = false;
        try { Armar(l, general, conClases.Count > 0 ? conClases[0] : 1, null, enlaces, null); }
        catch (TargetInvocationException ex) { lanzo = ex.InnerException is InvalidOperationException; }
        Ver("  T2: " + bien + " de " + sin.Count + " docentes sin clases (y uno que no existe) bien; Armar no acepta lo de toda la escuela", lanzo && bien == sin.Count);
    }

    /// <summary>T8: un ciclo pasado de los ultimos 400 dias, leido con la ventana que acaba en su ultimo dia y leido hasta hoy, da lo mismo.</summary>
    private static void CicloPasado(object l0, IList profesores, object enlaces)
    {
        DateTime hoy = (DateTime)Get(l0, "Hoy");
        IList ciclos = Lista(l0, "CiclosPasados");
        L("\n===== T8: ciclos pasados: " + ciclos.Count + " =====");
        foreach (object c in ciclos)
        {
            DateTime desde = (DateTime)Get(c, "Desde");
            if (desde < hoy.AddDays(-399))
            {
                L("  " + Call(Datos, "TextoDeCiclo", c) + ": empieza hace mas de 400 dias, se salta");
                continue;
            }

            object p = Periodo("DelCiclo", c);
            Olvidar();
            object l1 = Leer(p, true);
            object general1 = Calcular(l1, Resolver(p, l1, 0, false));
            List<int> docentes = Lista(general1, "OpcionesDocente").Cast<object>().Select(o => I(o, "Id")).Where(i => i != 0).ToList();
            Olvidar();
            object l2 = Leer(Periodo("DesdeHastaHoy", desde), true);
            L("  " + Call(Datos, "TextoDeCiclo", c) + ": lectura acotada hasta " + Fecha((DateTime)Get(l1, "HastaLectura")) + ", la otra hasta " + Fecha((DateTime)Get(l2, "HastaLectura"))
                + "; " + docentes.Count + " docentes");
            int bien = 0;
            foreach (int d in docentes)
            {
                string a = LoQueSeCompara(Calcular(l1, Resolver(p, l1, d, true)));
                string b;
                try { b = LoQueSeCompara(Calcular(l2, Resolver(p, l2, d, true))); }
                catch (Exception ex) { Ver("T8 docente " + d + ": la lectura hasta hoy no cubre el ciclo (" + ex.Message + ")", false); continue; }
                if (a != b) { Ver("T8 docente " + d + ": " + PrimeraDiferencia(a, b), false); } else { bien++; Oks++; }
            }

            L("  T8: " + bien + " de " + docentes.Count + " iguales");
        }

        Olvidar();
    }

    // =================================================================
    // La pantalla (T9, T11, Ver en Estadisticas, la lista dia por dia, Docentes)
    // =================================================================

    private static Form ventana;
    private static FormQuietoFicha anfitrion;

    private static void Meter(Form f, int ancho, int alto)
    {
        ventana = f;
        f.WindowState = FormWindowState.Normal;
        f.TopLevel = false;
        f.FormBorderStyle = FormBorderStyle.None;
        f.MinimumSize = Size.Empty;
        f.Location = Point.Empty;
        f.ClientSize = new Size(ancho, alto);
        anfitrion = new FormQuietoFicha
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-9000, -9000),
            ShowInTaskbar = false,
            Opacity = 0,
            FormBorderStyle = FormBorderStyle.None,
            ClientSize = new Size(ancho, alto)
        };
        anfitrion.Controls.Add(f);
        f.Show();
        anfitrion.Show();
        Bombear(0.4);
    }

    private static void Sacar()
    {
        if (ventana != null) { ventana.Close(); ventana.Dispose(); ventana = null; }
        if (anfitrion != null) { anfitrion.Close(); anfitrion.Dispose(); anfitrion = null; }
    }

    private static void Tamano(int ancho, int alto)
    {
        ventana.ClientSize = new Size(ancho, alto);
        anfitrion.ClientSize = new Size(ancho, alto);
        Bombear(0.4);
    }

    private static Form NuevaFicha(object profesor, object enlaces)
    {
        return (Form)Activator.CreateInstance(T("DigBit.FichaDocente"), new[] { profesor, enlaces });
    }

    private static bool Leida(Form ficha)
    {
        if ((int)Get(ficha, "lecturas") == 0) { Call(ficha, "Recargar", (string)null); }
        return Esperar(() => !(bool)Get(ficha, "Leyendo") && Get(ficha, "Datos") != null, 90);
    }

    private static void Foto(string nombre, string salida)
    {
        Application.DoEvents();
        ventana.Refresh();
        using (Bitmap b = new Bitmap(ventana.Width, ventana.Height))
        {
            ventana.DrawToBitmap(b, new Rectangle(0, 0, ventana.Width, ventana.Height));
            string archivo = Path.Combine(salida, nombre + ".png");
            b.Save(archivo, ImageFormat.Png);
            L("  foto: " + archivo + " (" + ventana.Width + " x " + ventana.Height + ")");
        }
    }

    /// <summary>Lo que se ve dentro de esa pestana (o de la ventana) no se encima ni se sale.</summary>
    private static List<string> Encimados(Control padre, Size tam)
    {
        List<Control> visibles = padre.Controls.Cast<Control>().Where(c => c.Visible).ToList();
        List<string> problemas = new List<string>();
        for (int x = 0; x < visibles.Count; x++)
        {
            Rectangle r = visibles[x].Bounds;
            if (r.Right > tam.Width + 1 || r.Bottom > tam.Height + 1 || r.Left < 0 || r.Top < 0) { problemas.Add(Nombre(visibles[x]) + " sale " + r); }
            for (int y = x + 1; y < visibles.Count; y++)
            {
                if (r.IntersectsWith(visibles[y].Bounds)) { problemas.Add(Nombre(visibles[x]) + " " + r + " encima de " + Nombre(visibles[y]) + " " + visibles[y].Bounds); }
            }
        }

        return problemas;
    }

    private static string Nombre(Control c)
    {
        if (!string.IsNullOrEmpty(c.AccessibleName)) { return c.GetType().Name + "'" + c.AccessibleName + "'"; }
        if (!string.IsNullOrEmpty(c.Text) && c.Text.Length < 30) { return c.GetType().Name + "'" + c.Text + "'"; }
        return c.GetType().Name;
    }

    /// <summary>El docente con mas clases que se dieron en "Este ciclo" (para la pantalla y las fotos).</summary>
    private static int ElDeMasClases(IList profesores)
    {
        object p = De("Ciclo");
        object l = Leer(p, false);
        object general = Calcular(l, Resolver(p, l, 0, false));
        object mejor = Lista(general, "Docentes").Cast<object>().OrderByDescending(f => I(f, "SeDieron") + I(f, "SinRegistro") + I(f, "SusReportes") + I(f, "AsistenciasQueDio"))
            .ThenBy(f => I(f, "DocenteId")).FirstOrDefault();
        return mejor != null ? I(mejor, "DocenteId") : profesores.Count > 0 ? I(profesores[0], "Id") : 0;
    }

    /// <summary>
    /// El docente de la ficha: el pedido, el de mas clases o, en una base sin docentes (el esquema
    /// de la escuela recien creado), uno que no existe: su ficha lee igual y dice que no tuvo clases.
    /// </summary>
    private static object ElDocente(IList profesores, ref int d)
    {
        if (d == 0) { d = ElDeMasClases(profesores); }
        object profesor = ProfesorDe(profesores, d);
        if (profesor != null || d != 0) { return profesor; }
        d = 999999;
        profesor = Activator.CreateInstance(T("DigBit.conexion.ProfesorInfo"));
        Set(profesor, "Id", d);
        Set(profesor, "Matricula", "SINDOCENTES");
        Set(profesor, "Nombre", "Docente de prueba");
        Set(profesor, "Activo", true);
        L("  (la base no tiene docentes: la ficha de uno que no existe, id " + d + ", sin clases)");
        return profesor;
    }

    public static void Pantalla(string salida, bool conFotos, int docente)
    {
        IList profesores = (IList)Profesores();
        object enlaces = Enlaces();
        int d = docente;
        object profesor = ElDocente(profesores, ref d);
        L("Docente " + d + (profesor == null ? " (no esta en la lista)" : ""));
        Meter(NuevaFicha(profesor, enlaces), 1350, 689);
        Ver("lee y arma la ficha", Leida(ventana));
        object datos = Get(ventana, "Datos");
        object r = Get(datos, "R");
        object l = Get(datos, "Lectura");
        TabControl tabs = (TabControl)Get(ventana, "tabs");
        L("  " + ((Label)Get(ventana, "lblResumen")).Text);
        L("  pestanas: " + string.Join(", ", tabs.TabPages.Cast<TabPage>().Select(t => t.Text)));
        L("  cifras: " + string.Join(" | ", ((Array)Get(ventana, "cifras")).Cast<object>().Select(c => S(Get(c, "Valor")) + " " + S(Get(c, "Etiqueta")))));
        Ver("la barra de titulo dice 'Docente: ...' y no lleva codigos", ventana.Text.StartsWith("Docente: ", StringComparison.Ordinal));
        Control selector = (Control)Get(ventana, "selector");
        Ver("el selector de Periodo no es una parada del tabulador: el primer Tab llega a su combo", !selector.TabStop && !selector.CanSelect && ((Control)Get(selector, "Combo")).CanSelect);

        // Las cinco cifras son las de Estadisticas con su filtro (C2 se dieron, C1, C3, C4 y C5).
        object deR = Get(r, "Cifras");
        string[] esperadas = { "ClasesQueSeDieronCifra", "Asistencia", "Bitacoras", "Fallas", "SinInternet" };
        Array cifras = (Array)Get(ventana, "cifras");
        bool iguales = true;
        for (int i = 0; i < 5; i++) { iguales &= S(Get(cifras.GetValue(i), "Valor")) == S(Get(Get(deR, esperadas[i]), "Valor")); }
        Ver("las cinco cifras son las de Estadisticas con Docente = el", iguales);

        // T9: lo que depende de las migraciones.
        bool con15 = B(r, "ConReportesDocentes"), con22 = B(r, "ConDadas"), con17 = (bool)Get(Get(l, "Asistencia"), "ConRetiro");
        L("  migraciones: db/15 " + con15 + ", db/17 " + con17 + ", db/22 " + con22 + ", db/24 " + B(enlaces, "Disponible") + ", db/25 " + (Get(datos, "PorAsignar") != null || !B(datos, "LlegaAHoy")));
        Ver("T9 'Asistencias que dio' solo con db/22 (" + con22 + ")", tabs.TabPages.Contains((TabPage)Get(ventana, "tpDadas")) == con22);
        // Visible solo dice la verdad con su pestana a la vista.
        tabs.SelectedTab = (TabPage)Get(ventana, "tpFallas");
        Bombear(0.2);
        // Sin db/15, el aviso de la actualizacion; con ella y sin reportes, una linea en vez de la tabla vacia.
        bool conReportes = con15 && Lista(datos, "SusReportes").Count > 0;
        Label sinReportes = (Label)Get(ventana, "lblSinReportes");
        Ver("T9 sin db/15, 'Lo que reporto' dice que falta la actualizacion y no hay tabla (" + con15 + "; con reportes " + conReportes + ")",
            sinReportes.Visible == !conReportes && ((DataGridView)Get(Get(ventana, "tReportes"), "Grid")).Visible == conReportes
            && (con15 || sinReportes.Text.Contains("db/15")) && (!con15 || !sinReportes.Text.Contains("db/15")));
        tabs.SelectedIndex = 0;
        Ver("T9 'Falta asignar' solo con horas suyas pendientes", ((Button)Get(ventana, "btnFaltaAsignar")).Visible == (Get(datos, "PorAsignar") != null && Lista(datos, "PorAsignar").Count > 0));
        Ver("T9 el subtitulo dice el enlace con el SIIA solo con db/24", B(enlaces, "Disponible") || !((Label)Get(ventana, "lblSubtitulo")).Text.Contains("SIIA"));

        // "Ver >" de Para revisar.
        IList pendientes = Lista(datos, "Pendientes");
        L("  para revisar: " + string.Join(" | ", pendientes.Cast<object>().Select(p => Get(p, "Regla") + " " + Get(p, "Tono"))));
        object r1 = pendientes.Cast<object>().FirstOrDefault(p => (string)Get(p, "Regla") == "R1");
        if (r1 != null)
        {
            Call(ventana, "IrA", r1);
            Bombear(0.3);
            Ver("Ver > de R1: 'Sus clases y faltas' con 'Sin registro' (" + Lista(Get(ventana, "tDias"), "visibles").Count + " filas)",
                tabs.SelectedTab == Get(ventana, "tpClases") && S(Get(ventana, "mostrar")) == "SinRegistro"
                && Lista(Get(ventana, "tDias"), "visibles").Count == I(datos, "SinRegistro"));
            ((RadioButton[])Get(ventana, "rbMostrar"))[0].Checked = true;
        }

        object r2 = pendientes.Cast<object>().FirstOrDefault(p => (string)Get(p, "Regla") == "R2");
        if (r2 != null)
        {
            Call(ventana, "IrA", r2);
            Bombear(0.3);
            object elegida = Get(Get(ventana, "tClases"), "Elegido");
            Ver("Ver > de R2: 'Sus grupos' con esa clase elegida", tabs.SelectedTab == Get(ventana, "tpGrupos") && elegida != null && I(elegida, "ClaseId") == I(r2, "ClaseId"));
        }

        // La semana del mes: la tabla deja solo esa semana.
        IList dias = Lista(datos, "Dias");
        if (dias.Count > 0)
        {
            DateTime fecha = (DateTime)Get(dias[0], "Fecha");
            DateTime lunes = fecha.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));
            Set(ventana, "semana", (DateTime?)lunes);
            Call(ventana, "PonerDias");
            int enLaSemana = dias.Cast<object>().Count(x => (DateTime)Get(x, "Fecha") >= lunes && (DateTime)Get(x, "Fecha") <= lunes.AddDays(6));
            Ver("un clic en una semana del mes deja la tabla en esa semana (" + enLaSemana + ")", Lista(Get(ventana, "tDias"), "visibles").Count == enLaSemana && ((Label)Get(ventana, "lblSemana")).Text.StartsWith("Semana del ", StringComparison.Ordinal));
            Call(ventana, "QuitarSemana");
            Ver("  y 'Quitar' vuelve a todas", Lista(Get(ventana, "tDias"), "visibles").Count == dias.Count);
        }

        Periodos(d);
        Pdf(salida, profesor, r, datos);
        ListaDiaPorDia(salida, conFotos, l, r, profesor);
        EnEstadisticas(d, r);

        if (conFotos)
        {
            int[][] tamanos = { new[] { 1904, 1001 }, new[] { 1350, 689 }, new[] { 984, 521 } };
            string[] nombres = { "1920x1040", "1366x728", "1000x560" };
            for (int t = 0; t < tamanos.Length; t++)
            {
                Tamano(tamanos[t][0], tamanos[t][1]);
                List<string> deVentana = Encimados(ventana, ventana.ClientSize).Where(x => !x.Contains("FormQuieto")).ToList();
                Ver("  " + nombres[t] + ": la cabecera, las cifras, las pestanas y los botones no se enciman" + (deVentana.Count == 0 ? "" : ": " + string.Join("; ", deVentana)), deVentana.Count == 0);
                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    Bombear(0.3);
                    if (tabs.SelectedTab == Get(ventana, "tpGrupos"))
                    {
                        Call(Get(ventana, "tClases"), "ElegirFila", new Func<object, bool>(x => true));
                    }

                    TabPage pagina = tabs.SelectedTab;
                    Bombear(0.2);
                    List<string> problemas = Encimados(pagina, pagina.ClientSize);
                    Ver("  " + nombres[t] + " " + pagina.Text + ": nada encimado ni fuera" + (problemas.Count == 0 ? "" : ": " + string.Join("; ", problemas)), problemas.Count == 0);
                    Foto("ficha_" + nombres[t] + "_" + (i + 1) + "_" + Archivo(pagina.Text), salida);
                }
            }

            tabs.SelectedIndex = 0;
        }

        Sacar();
        Docentes(salida, conFotos);
    }

    /// <summary>El periodo de la ficha: "Este mes" desde su combo relee con el; la ficha lo recuerda aparte y Estadisticas no cambia.</summary>
    private static void Periodos(int d)
    {
        ComboBox combo = (ComboBox)Get(Get(ventana, "selector"), "Combo");
        object deEstadisticas = Get(T("DigBit.VentanaEstadisticas"), "ultimoPeriodo");
        int mes = -1, ciclo = -1;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            object p = Get(combo.Items[i], "Periodo");
            if (p == null) { continue; }
            if (S(Get(p, "Tipo")) == "Mes") { mes = i; }
            if (S(Get(p, "Tipo")) == "Ciclo" && ciclo < 0) { ciclo = i; }
        }

        L("  periodo: " + string.Join(" | ", combo.Items.Cast<object>().Select(x => x.ToString())));
        if (mes < 0) { Ver("el combo Periodo de la ficha tiene 'Este mes'", false); return; }
        combo.SelectedIndex = mes;
        bool leyo = Esperar(() => !(bool)Get(ventana, "Leyendo") && S(Get(Get(Get(Get(ventana, "Datos"), "R"), "Filtro"), "Tipo")) == "Mes", 60);
        object recordado = Get(T("DigBit.FichaDocente"), "ultimoPeriodo");
        Ver("'Este mes' en el combo de la ficha relee con el mes; la ficha lo recuerda y Estadisticas no cambia",
            leyo && S(Get(recordado, "Tipo")) == "Mes" && Get(T("DigBit.VentanaEstadisticas"), "ultimoPeriodo") == deEstadisticas
            && I(Get(Get(Get(ventana, "Datos"), "R"), "Filtro"), "Docente") == d);
        if (ciclo >= 0)
        {
            combo.SelectedIndex = ciclo;
            Esperar(() => !(bool)Get(ventana, "Leyendo") && S(Get(Get(Get(Get(ventana, "Datos"), "R"), "Filtro"), "Tipo")) != "Mes", 60);
        }
    }

    private static string Archivo(string texto)
    {
        string sinAcentos = new string(texto.Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        return sinAcentos.ToLowerInvariant().Replace(' ', '_');
    }

    /// <summary>T11: el PDF con PdfTextExtractor: las cifras, "Se dieron X de N", sin matriculas sin la casilla, el nombre del archivo y el log.</summary>
    private static void Pdf(string salida, object profesor, object r, object datos)
    {
        L("\n===== T11: el reporte del docente (PDF) =====");
        object[] sinListas = { false, null };
        byte[] pdf = (byte[])Call(ventana, "ReporteEnBytes", sinListas);
        object d = sinListas[1];
        object[] conListasArgs = { true, null };
        byte[] pdfListas = (byte[])Call(ventana, "ReporteEnBytes", conListasArgs);
        File.WriteAllBytes(Path.Combine(salida, "ficha_reporte_sin_listas.pdf"), pdf);
        File.WriteAllBytes(Path.Combine(salida, "ficha_reporte_con_listas.pdf"), pdfListas);
        string texto = TextoDelPdf(pdf);
        string textoListas = TextoDelPdf(pdfListas);
        object c = Get(r, "Cifras");
        string[] cinco = { "ClasesQueSeDieronCifra", "Asistencia", "Bitacoras", "Fallas", "SinInternet" };
        List<string> faltan = cinco.Select(n => S(Get(Get(c, n), "Valor"))).Where(v => !texto.Contains(v)).ToList();
        Ver("T11 el PDF lleva las cinco cifras de la pantalla" + (faltan.Count == 0 ? "" : " (faltan " + string.Join(", ", faltan) + ")"), faltan.Count == 0);
        Ver("T11 dice 'Reporte del docente' y no 'Reporte de uso de laboratorios'", texto.Contains("Reporte del docente") && !texto.Contains("Reporte de uso de laboratorios"));
        if (I(c, "ClasesQueContaban") > 0)
        {
            string seDieron = "Se dieron " + Call(Datos, "XdeN", I(c, "ClasesQueSeDieron"), I(c, "ClasesQueContaban"));
            Ver("T11 '" + seDieron + "' en el PDF", texto.Contains(seDieron));
        }

        HashSet<string> matriculas = new HashSet<string>();
        foreach (object clase in Lista(r, "Clases")) { foreach (object par in Lista(clase, "Lista")) { string m = S(Get(par, "Matricula")); if (m.Length >= 5) { matriculas.Add(m); } } }
        foreach (object dada in Lista(datos, "SusDadas")) { string m = S(Get(dada, "Matricula")); if (m.Length >= 5) { matriculas.Add(m); } }
        int sinCasilla = matriculas.Count(m => texto.Contains(m));
        int conCasilla = matriculas.Count(m => textoListas.Contains(m));
        Ver("T11 sin la casilla no aparece ninguna matricula (" + sinCasilla + " de " + matriculas.Count + ")", sinCasilla == 0);
        Ver("T11 con la casilla aparecen las de sus listas (" + conCasilla + " de " + matriculas.Count + ")", matriculas.Count == 0 || conCasilla > 0);
        Ver("T11 'Lista de cada grupo' solo con la casilla", !texto.Contains("Lista de cada grupo") && (Lista(r, "Clases").Count == 0 || textoListas.Contains("Lista de cada grupo")));
        Ver("T11 las tres notas del final", texto.Contains("Sin registro no siempre es falta del docente") && texto.Contains("no es la lista oficial de inscritos") && texto.Contains("no sustituye el cierre oficial del ciclo"));
        string labs = S(Get(d, "Laboratorio"));
        List<string> nombresLabs = labs.StartsWith("Sus laboratorios: ", StringComparison.Ordinal) ? labs.Substring(18).Split(new[] { ", " }, StringSplitOptions.None).ToList() : new List<string>();
        Ver("T11 'Sus laboratorios' sin repetir (" + labs + ")", nombresLabs.Count == nombresLabs.Distinct(StringComparer.CurrentCultureIgnoreCase).Count()
            && (Lista(r, "Clases").Count == 0 || nombresLabs.Count > 0));
        string nombre = (string)Call(ventana, "NombreDelPdf", d);
        string apellido = S(Get(profesor, "ApellidoPaterno"));
        Ver("T11 el archivo lleva el numero y no el nombre",
            nombre.StartsWith("Reporte del docente " + S(Get(profesor, "Matricula")).Trim() + " ", StringComparison.Ordinal) && (apellido.Length < 3 || !nombre.Contains(apellido)) && nombre.EndsWith(".pdf", StringComparison.Ordinal));
        string registro = string.Join("\n", Directory.GetFiles(carpetaDatos, "*.log", SearchOption.AllDirectories).Select(f => File.ReadAllText(f)));
        bool conId = registro.Contains("reporte del docente id " + I(profesor, "Id"));
        bool conNombre = (apellido.Length >= 3 && registro.Contains(apellido)) || matriculas.Any(m => registro.Contains(m));
        Ver("T11 el log dice el id del docente y no nombres ni matriculas", conId && !conNombre);
        L("  " + pdf.Length + " bytes sin listas, " + pdfListas.Length + " con listas; " + texto.Length + " caracteres de texto");
    }

    private static string TextoDelPdf(byte[] pdf)
    {
        Assembly itext = Assembly.LoadFrom(Path.Combine(carpeta, "itextsharp.dll"));
        Type tReader = itext.GetType("iTextSharp.text.pdf.PdfReader", true);
        Type tExtractor = itext.GetType("iTextSharp.text.pdf.parser.PdfTextExtractor", true);
        object reader = Activator.CreateInstance(tReader, new object[] { pdf });
        int paginas = (int)tReader.GetProperty("NumberOfPages").GetValue(reader, null);
        MethodInfo deLaPagina = tExtractor.GetMethods().First(m => m.Name == "GetTextFromPage" && m.GetParameters().Length == 2);
        StringBuilder sb = new StringBuilder();
        for (int i = 1; i <= paginas; i++) { sb.AppendLine((string)deLaPagina.Invoke(null, new[] { reader, (object)i })); }
        tReader.GetMethod("Close").Invoke(reader, null);
        return sb.ToString().Replace("\n", " ");
    }

    /// <summary>La lista dia por dia de su primera clase: solo para ver y en tercera persona.</summary>
    private static void ListaDiaPorDia(string salida, bool conFotos, object l, object r, object profesor)
    {
        IList clases = Lista(r, "Clases");
        if (clases.Count == 0)
        {
            L("  (sin clases: no hay lista dia por dia)");
            return;
        }

        Form ficha = ventana;
        FormQuietoFicha anfitrionFicha = anfitrion;
        Form lista = (Form)Activator.CreateInstance(T("DigBit.VentanaListaDeClase"), new[] { l, clases[0], (object)S(Get(profesor, "NombreCompleto")) });
        Meter(lista, 1350, 689);
        object hoja = Get(lista, "Hoja");
        Ver("la lista dia por dia: la hoja no deja cambiar nada (PuedeCambiar false) y habla en tercera persona",
            !(bool)Get(hoja, "PuedeCambiar") && (bool)Get(hoja, "EnTerceraPersona") && (bool)Get(Get(lista, "Tablero"), "EnTerceraPersona"));
        Ver("  y su titulo dice 'Lista de asistencia: ...'", lista.Text.StartsWith("Lista de asistencia: ", StringComparison.Ordinal));
        List<DataGridView> deLaHoja = Todos(lista).OfType<DataGridView>().ToList();
        Ver("  la administracion si la copia (Ctrl+C con encabezados) y el subtitulo dice la hora de la lectura de la ficha ("
            + ((DateTime)Get(l, "AhoraServidor")).ToString("HH:mm", Inv) + ")",
            deLaHoja.Count > 0 && deLaHoja.All(g => g.ClipboardCopyMode == DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText)
            && ((Label)Get(lista, "lblSubtitulo")).Text.Contains("a las " + ((DateTime)Get(l, "AhoraServidor")).ToString("HH:mm", Inv)));
        if (conFotos) { Foto("ficha_lista_dia_por_dia_1366x728", salida); }
        Sacar();
        ventana = ficha;
        anfitrion = anfitrionFicha;
    }

    /// <summary>"Ver en Estadisticas": Estadisticas con el, el mismo periodo y en Clases; sus cifras son las de la ficha.</summary>
    private static void EnEstadisticas(int d, object r)
    {
        if (Lista(r, "Clases").Count == 0)
        {
            // Sin clases en el periodo, Estadisticas volveria a todo el personal: el boton solo avisa.
            int abiertas = 0;
            Action<int, object> contar = (x, y) => abiertas++;
            EventInfo evento = ventana.GetType().GetEvent("VerEnEstadisticas", TI);
            Delegate manejador = Delegate.CreateDelegate(evento.EventHandlerType, contar.Target, contar.Method);
            evento.AddEventHandler(ventana, manejador);
            Call(ventana, "AbrirEnEstadisticas");
            evento.RemoveEventHandler(ventana, manejador);
            string aviso = ((Label)Get(ventana, "lblAviso")).Text;
            Ver("Ver en Estadisticas sin clases en el periodo: no abre Estadisticas (que ensenaria todo el personal) y avisa ('" + aviso + "')",
                abiertas == 0 && aviso.Contains("Sin clases"));
            return;
        }

        Form ficha = ventana;
        FormQuietoFicha anfitrionFicha = anfitrion;
        Form est = (Form)Activator.CreateInstance(T("DigBit.VentanaEstadisticas"), true);
        object periodo = Get(ficha, "Periodo");
        Call(est, "MostrarDocente", d, periodo);
        Meter(est, 1350, 689);
        if ((int)Get(est, "lecturas") == 0) { Call(est, "Recargar", (string)null); }
        Type tPest = T("DigBit.conexion.PestanaEstadisticas");
        object clases = Enum.Parse(tPest, "Clases");
        bool listo = Esperar(() => !(bool)Get(est, "leyendoBase") && Call(est, "ResultadoDe", clases) != null, 90);
        object re = listo ? Call(est, "ResultadoDe", clases) : null;
        bool bien = re != null && I(Get(re, "Filtro"), "Docente") == d && (int)Get(Get(est, "tabs"), "SelectedIndex") == (int)clases
            && I(Get(re, "Cifras"), "ClasesQueSeDieron") == I(Get(r, "Cifras"), "ClasesQueSeDieron")
            && S(Get(Get(Get(re, "Cifras"), "Asistencia"), "Valor")) == S(Get(Get(Get(r, "Cifras"), "Asistencia"), "Valor"));
        Ver("Ver en Estadisticas: Clases con el docente " + d + " y el mismo periodo; mismas clases que se dieron y misma asistencia", bien);

        // Estadisticas con OTRA lectura (como si la suya fuera de hace mas de 10 minutos): "Ver en
        // Estadisticas" calcula con la de la ficha, no con la suya ni leyendo otra (solo lee: F5).
        object deLaFicha = Get(ficha, "LecturaVista");
        object otra = Leer(periodo, true);
        Call(est, "Recargar", (string)null);
        bool conLaOtra = Esperar(() => !(bool)Get(est, "leyendoBase") && Get(est, "lectura") == otra && Call(est, "ResultadoDe", clases) != null, 90);
        Call(est, "MostrarDocente", d, periodo, deLaFicha);
        bool conLaDeLaFicha = Esperar(() => !(bool)Get(est, "leyendoBase") && Get(est, "lectura") == deLaFicha && Call(est, "ResultadoDe", clases) != null, 90);
        object re2 = conLaDeLaFicha ? Call(est, "ResultadoDe", clases) : null;
        Ver("Ver en Estadisticas con otra lectura en Estadisticas (" + conLaOtra + "): calcula con la de la ficha, con el docente " + d + " y sus mismas cifras",
            conLaOtra && otra != deLaFicha && conLaDeLaFicha && re2 != null && I(Get(re2, "Filtro"), "Docente") == d
            && I(Get(re2, "Cifras"), "ClasesQueSeDieron") == I(Get(r, "Cifras"), "ClasesQueSeDieron")
            && S(Get(Get(Get(re2, "Cifras"), "Asistencia"), "Valor")) == S(Get(Get(Get(r, "Cifras"), "Asistencia"), "Valor")));
        Sacar();
        ventana = ficha;
        anfitrion = anfitrionFicha;
    }

    /// <summary>
    /// T13 (parte): en Docentes, "Ver todo lo suyo..." abajo, el primero de las acciones de la fila
    /// (2026-10-31: "Ver todo lo suyo...", "Editar", "Mas acciones" y "Cerrar"), sin pisar la linea de
    /// estado ni otro boton, a 1000, 1366 y 1920.
    /// </summary>
    private static void Docentes(string salida, bool conFotos)
    {
        L("\n===== Docentes con 'Ver todo lo suyo...' =====");
        // Con las acciones juntas abajo la administracion ya baja a 1000, como la responsable.
        foreach (int ancho in new[] { 984, 1350, 1904 })
        {
            Form docentes = (Form)Activator.CreateInstance(T("DigBit.AdministrarProfesores"));
            Meter(docentes, ancho, 650);
            Call(docentes, "Recargar", (string)null);
            Bombear(0.3);
            Button ver = (Button)Get(docentes, "btnVerTodo");
            Label resumen = (Label)Get(docentes, "lblResumen");
            List<Button> abajo = docentes.Controls.OfType<Button>().Where(b => b.Visible && b.Top == ver.Top).OrderBy(b => b.Left).ToList();
            bool bien = abajo.Count >= 2 && abajo[0] == ver && abajo.Last().Text == "Cerrar" && resumen.Right <= ver.Left
                && !abajo.Any(b => b != ver && b.Bounds.IntersectsWith(ver.Bounds)) && ver.Right <= docentes.ClientSize.Width && ver.Top > docentes.ClientSize.Height - 80;
            DataGridView grid = (DataGridView)Get(docentes, "grid");
            if (grid.Rows.Count > 0) { grid.Rows[0].Selected = true; grid.CurrentCell = grid.Rows[0].Cells[0]; Bombear(0.2); }
            Ver("  a " + (ancho + 16) + ": el boton " + ver.Bounds + " abajo, el primero (" + string.Join(", ", abajo.Select(b => b.Text)) + "), sin pisar la linea de estado"
                + "; con alguien elegido se habilita (" + ver.Enabled + ")", bien && (grid.Rows.Count == 0 || ver.Enabled));
            if (conFotos) { Foto("docentes_" + (ancho + 16) + "_con_ver_todo", salida); }
            Sacar();
        }
    }

    // =================================================================
    // T10: solo consulta
    // =================================================================

    private static readonly string[] TablasQueNoCambian = { "codigos_accesos", "registros_bitacoras", "asistencias_dadas", "asistencias_retiradas", "horario_excepciones", "reportes_profesor", "horas_por_asignar", "usuarios", "clases", "horarios" };

    public static string Sumas(string cadenaRoot)
    {
        Assembly mysql = Assembly.LoadFrom(Path.Combine(carpeta, "MySql.Data.dll"));
        Type tCon = mysql.GetType("MySql.Data.MySqlClient.MySqlConnection", true);
        using (IDisposable con = (IDisposable)Activator.CreateInstance(tCon, cadenaRoot))
        {
            tCon.GetMethod("Open").Invoke(con, null);
            List<string> partes = new List<string>();
            foreach (string tabla in TablasQueNoCambian)
            {
                object cmd = tCon.GetMethod("CreateCommand").Invoke(con, null);
                cmd.GetType().GetProperty("CommandText").SetValue(cmd, "CHECKSUM TABLE `" + tabla + "`", null);
                using (IDisposable lector = (IDisposable)cmd.GetType().GetMethod("ExecuteReader", Type.EmptyTypes).Invoke(cmd, null))
                {
                    Type tl = lector.GetType();
                    if ((bool)tl.GetMethod("Read").Invoke(lector, null))
                    {
                        object v = tl.GetMethod("GetValue").Invoke(lector, new object[] { 1 });
                        partes.Add(tabla + "=" + (v == null || v is DBNull ? "no existe" : Convert.ToString(v, Inv)));
                    }
                }
            }

            return string.Join(" ", partes);
        }
    }

    /// <summary>
    /// La base de la cuenta digbit_consulta si no es esta: la unica con sus permisos (db/18 se
    /// aplico en otra) u otra con distinta consulta_clave. Solo lee; nunca imprime claves. Null
    /// si la cuenta deberia servir aqui (y entonces el fallo es de verdad).
    /// </summary>
    private static string OtraBaseDeLaCuenta(string cadenaRoot)
    {
        Assembly mysql = Assembly.LoadFrom(Path.Combine(carpeta, "MySql.Data.dll"));
        Type tCon = mysql.GetType("MySql.Data.MySqlClient.MySqlConnection", true);
        using (IDisposable con = (IDisposable)Activator.CreateInstance(tCon, cadenaRoot))
        {
            tCon.GetMethod("Open").Invoke(con, null);
            Func<string, List<string>> leer = sql =>
            {
                object cmd = tCon.GetMethod("CreateCommand").Invoke(con, null);
                cmd.GetType().GetProperty("CommandText").SetValue(cmd, sql, null);
                List<string> filas = new List<string>();
                using (IDisposable lector = (IDisposable)cmd.GetType().GetMethod("ExecuteReader", Type.EmptyTypes).Invoke(cmd, null))
                {
                    Type tl = lector.GetType();
                    while ((bool)tl.GetMethod("Read").Invoke(lector, null))
                    {
                        object v = tl.GetMethod("GetValue").Invoke(lector, new object[] { 0 });
                        filas.Add(v == null || v is DBNull ? null : Convert.ToString(v, Inv));
                    }
                }

                return filas;
            };

            string actual = leer("SELECT DATABASE()").FirstOrDefault();
            List<string> conPermisos = leer("SELECT TABLE_SCHEMA FROM information_schema.SCHEMA_PRIVILEGES WHERE GRANTEE LIKE '%digbit_consulta%' "
                + "UNION SELECT TABLE_SCHEMA FROM information_schema.TABLE_PRIVILEGES WHERE GRANTEE LIKE '%digbit_consulta%'").Where(x => x != null).Distinct().ToList();
            if (conPermisos.Count > 0 && !conPermisos.Contains(actual))
            {
                return string.Join(", ", conPermisos);
            }

            string esta = leer("SELECT valor FROM configuracion WHERE clave = 'consulta_clave'").FirstOrDefault();
            foreach (string otra in leer("SELECT TABLE_SCHEMA FROM information_schema.TABLES WHERE TABLE_NAME = 'configuracion' AND TABLE_SCHEMA <> DATABASE() AND TABLE_SCHEMA LIKE 'teschi%' ORDER BY TABLE_SCHEMA = 'teschi_otru' DESC, TABLE_SCHEMA"))
            {
                string suya = leer("SELECT valor FROM `" + otra.Replace("`", "") + "`.configuracion WHERE clave = 'consulta_clave'").FirstOrDefault();
                if (!string.IsNullOrEmpty(suya) && suya != esta)
                {
                    return otra;
                }
            }

            return null;
        }
    }

    public static void Consulta(string cadenaRoot, string salida, bool conFotos)
    {
        string antes = Sumas(cadenaRoot);
        IList profesores = (IList)Profesores();
        // Los codigos de las clases (para buscar que no se vean), leidos aun con la cuenta de la administracion.
        HashSet<string> codigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (object p in profesores) { string c = S(Get(p, "Codigo")); if (c != "null" && c.Trim().Length >= 4) { codigos.Add(c.Trim()); } }

        Type tConexion = T("DigBit.Conexion");
        object equipo = Call(Activator.CreateInstance(tConexion, true), "GetConexion");
        object[] args = { equipo, null };
        string problema = (string)T("DigBit.conexion.CuentaDeConsulta").GetMethod("Preparar", TI).Invoke(null, args);
        ((IDisposable)equipo).Dispose();
        if (problema != null)
        {
            // El usuario digbit_consulta es UNO por servidor: si db/18 le dio permisos solo en otra
            // base, o esta base guarda otra clave, aqui no se puede probar sin escribir (T10 se
            // prueba en esa base).
            if (problema.Contains("db/18_encargados.sql). Pide a quien administra el servidor que la aplique"))
            {
                L("[SALTA] T10 aqui: esta base no tiene db/18 (sin cuenta de solo consulta, como la escuela hoy): la responsable no puede entrar y T10 no aplica.");
                return;
            }

            string otra = OtraBaseDeLaCuenta(cadenaRoot);
            if (otra != null)
            {
                L("[SALTA] T10 aqui: la cuenta digbit_consulta de este servidor es de " + otra + " (sus permisos o su clave; no se escribe nada para cambiarlo): T10 se prueba con -Base " + otra + ".");
                return;
            }

            Ver("T10 se prepara la cuenta de solo consulta (" + problema + ")", false);
            return;
        }
        Type du = T("DigBit.conexion.Datos_User");
        tConexion.GetMethod("UsarCuentaDeSesion", TI).Invoke(null, new object[] { (string)args[1] });
        du.GetProperty("TipoUsuario", TI).SetValue(null, 4, null);
        Olvidar();
        try
        {
            IList deConsulta = (IList)Profesores();
            object enlaces = Enlaces();
            List<object> diez = deConsulta.Cast<object>().Take(10).ToList();
            L("T10 con digbit_consulta: " + diez.Count + " fichas; " + codigos.Count + " codigos que no deben verse");
            int bien = 0;
            foreach (object profesor in diez)
            {
                Meter(NuevaFicha(profesor, enlaces), 1350, 689);
                bool leida = Leida(ventana);
                TabControl tabs = (TabControl)Get(ventana, "tabs");
                List<string> mal = new List<string>();
                if (!leida) { mal.Add("no se leyo"); }
                for (int i = 0; i < tabs.TabPages.Count; i++) { tabs.SelectedIndex = i; Bombear(0.15); }
                Button pdf = (Button)Get(ventana, "btnPdf");
                if (pdf == null || pdf.Enabled || pdf.AccessibleDescription != (string)Get(Datos, "SinCsvParaSoloConsulta")) { mal.Add("el PDF no esta apagado con su motivo"); }
                if (!ventana.Text.EndsWith("(solo consulta)", StringComparison.Ordinal)) { mal.Add("la barra no dice (solo consulta)"); }
                List<string> textos = new List<string>();
                Recorrer(ventana, textos);
                string[] queEscriben = { "Editar", "Dar de baja", "Reactivar", "Cancelar", "No tomar en cuenta", "Dar asistencia", "Quitar la asistencia", "Restablecer", "Ponerla", "Ya no hace falta" };
                List<string> escriben = ventana.Controls.Cast<Control>().SelectMany(Todos).OfType<ButtonBase>().Select(b => b.Text).Where(t => queEscriben.Any(q => t.StartsWith(q, StringComparison.OrdinalIgnoreCase))).ToList();
                if (escriben.Count > 0) { mal.Add("botones que escriben: " + string.Join(", ", escriben)); }
                List<string> conCodigo = codigos.Where(cod => textos.Any(t => t.IndexOf(cod, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                if (conCodigo.Count > 0) { mal.Add(conCodigo.Count + " codigos a la vista"); }
                if (mal.Count == 0) { bien++; Oks++; } else { Ver("T10 ficha del docente " + I(profesor, "Id") + ": " + string.Join("; ", mal), false); }
                if (conFotos && bien == 1 && mal.Count == 0) { Foto("ficha_solo_consulta_1366x728", salida); }

                // La lista dia por dia: copiar y guardar, apagados con su motivo.
                object datos = Get(ventana, "Datos");
                if (datos != null && Lista(Get(datos, "R"), "Clases").Count > 0 && bien == 1)
                {
                    Form ficha = ventana;
                    FormQuietoFicha anf = anfitrion;
                    Form lista = (Form)Activator.CreateInstance(T("DigBit.VentanaListaDeClase"), new[] { Get(datos, "Lectura"), Lista(Get(datos, "R"), "Clases")[0], (object)"Docente" });
                    Meter(lista, 1350, 689);
                    Button copiar = (Button)Get(lista, "btnCopiar");
                    Button csv = (Button)Get(lista, "btnCsv");
                    // 2026-10-31: lo que la responsable nunca puede hacer no se ve (apagado siempre parecia descompuesto).
                    Ver("T10 en la lista dia por dia, 'Copiar la lista' y 'Guardar en Excel' no se ven",
                        !copiar.Visible && !csv.Visible);
                    List<DataGridView> deLaHoja = Todos(lista).OfType<DataGridView>().ToList();
                    Ver("T10 y la hoja tampoco se copia con Ctrl+A y Ctrl+C (ClipboardCopyMode = Disable)",
                        deLaHoja.Count > 0 && deLaHoja.All(g => g.ClipboardCopyMode == DataGridViewClipboardCopyMode.Disable));
                    Sacar();
                    ventana = ficha;
                    anfitrion = anf;
                }

                Sacar();
            }

            Ver("T10: " + bien + " de " + diez.Count + " fichas leidas enteras, sin PDF, sin botones que escriban ni codigos", bien == diez.Count && diez.Count > 0);

            // Docentes de la responsable: el boton abajo, antes de "Ver horario" y "Ver sus bitacoras", y el doble clic abre la ficha.
            Form docentes = (Form)Activator.CreateInstance(T("DigBit.AdministrarProfesores"));
            Meter(docentes, 984, 650);
            Call(docentes, "Recargar", (string)null);
            Button ver = (Button)Get(docentes, "btnVerTodo");
            Label subtitulo = docentes.Controls.OfType<Label>().ElementAt(1);
            List<Button> abajo = docentes.Controls.OfType<Button>().Where(b => b.Visible && b.Top == ver.Top).OrderBy(b => b.Left).ToList();
            Ver("T10 Docentes de la responsable: abajo '" + string.Join(", ", abajo.Select(b => b.Text)) + "' " + ver.Bounds + ", el subtitulo dice 'Doble clic para ver todo lo suyo.'",
                string.Join(", ", abajo.Select(b => b.Text)) == "Ver todo lo suyo..., Ver horario, Ver sus bit\u00e1coras, Cerrar" && subtitulo.Text.Contains("Doble clic para ver todo lo suyo."));
            if (conFotos) { Foto("docentes_1000_solo_consulta", salida); }
            Sacar();
        }
        finally
        {
            tConexion.GetMethod("UsarCuentaDelEquipo", TI).Invoke(null, new object[0]);
            du.GetProperty("TipoUsuario", TI).SetValue(null, 3, null);
            Olvidar();
        }

        string despues = Sumas(cadenaRoot);
        L("  CHECKSUM antes:   " + antes);
        L("  CHECKSUM despues: " + despues);
        Ver("T10 CHECKSUM de las tablas igual antes y despues de abrir las fichas", antes == despues);
    }

    private static IEnumerable<Control> Todos(Control c)
    {
        yield return c;
        foreach (Control h in c.Controls) { foreach (Control x in Todos(h)) { yield return x; } }
    }

    /// <summary>Todo el texto a la vista: de cada control y de cada celda (con su ayuda).</summary>
    private static void Recorrer(Control raiz, List<string> textos)
    {
        foreach (Control c in Todos(raiz))
        {
            if (!string.IsNullOrEmpty(c.Text)) { textos.Add(c.Text); }
            if (!string.IsNullOrEmpty(c.AccessibleDescription)) { textos.Add(c.AccessibleDescription); }
            DataGridView g = c as DataGridView;
            if (g == null) { continue; }
            foreach (DataGridViewRow fila in g.Rows)
            {
                foreach (DataGridViewCell celda in fila.Cells) { textos.Add(Convert.ToString(celda.Value, Inv)); textos.Add(celda.ToolTipText ?? ""); }
            }
        }
    }

    // =================================================================
    // T12: kiosco
    // =================================================================

    public static void Kiosco()
    {
        bool activo = (bool)Get(T("DigBit.Infraestructura.Kiosco"), "Activo");
        Ver("T12 el modo kiosco esta activo en este proceso", activo);
        IList profesores = (IList)Profesores();
        object enlaces = Enlaces();
        int d = 0;
        Meter(NuevaFicha(ElDocente(profesores, ref d), enlaces), 1350, 689);
        Ver("  lee la ficha", Leida(ventana));
        Ver("T12 sin 'Reporte del docente (PDF)...'", Get(ventana, "btnPdf") == null && !ventana.Controls.OfType<Button>().Any(b => b.Text.StartsWith("Reporte", StringComparison.Ordinal)));
        List<DataGridView> grids = Todos(ventana).OfType<DataGridView>().ToList();
        Ver("T12 Ctrl+C no copia: las " + grids.Count + " tablas con ClipboardCopyMode = Disable", grids.Count > 0 && grids.All(g => g.ClipboardCopyMode == DataGridViewClipboardCopyMode.Disable));
        object datos = Get(ventana, "Datos");
        IList clases = Lista(Get(datos, "R"), "Clases");
        if (clases.Count > 0)
        {
            Form ficha = ventana;
            FormQuietoFicha anf = anfitrion;
            Form lista = (Form)Activator.CreateInstance(T("DigBit.VentanaListaDeClase"), new[] { Get(datos, "Lectura"), clases[0], (object)"Docente" });
            Meter(lista, 1350, 689);
            List<DataGridView> deLaHoja = Todos(lista).OfType<DataGridView>().ToList();
            Ver("T12 en la lista dia por dia: sin 'Copiar la lista' ni 'Guardar en Excel' y la hoja sin copiar",
                !((Button)Get(lista, "btnCopiar")).Visible && !((Button)Get(lista, "btnCsv")).Visible && deLaHoja.All(g => g.ClipboardCopyMode == DataGridViewClipboardCopyMode.Disable));
            Sacar();
            ventana = ficha;
            anfitrion = anf;
        }

        // El portapapeles: solo si esta vacio (nunca se toca lo que tenga quien usa el equipo).
        bool vacio;
        try { IDataObject antes = Clipboard.GetDataObject(); vacio = antes == null || antes.GetFormats().Length == 0; }
        catch (Exception) { vacio = false; }
        if (!vacio)
        {
            L("  (el portapapeles tiene algo de quien usa el equipo: no se prueba que se vacie al cerrar)");
            Sacar();
            return;
        }

        Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "prueba de la ficha"), false);
        Sacar();
        Bombear(0.2);
        bool quedoVacio;
        try { IDataObject despues = Clipboard.GetDataObject(); quedoVacio = despues == null || despues.GetFormats().Length == 0; }
        catch (Exception) { quedoVacio = false; }
        Ver("T12 al cerrar la ficha, lo que DigBit dejo en el portapapeles se vacia", quedoVacio);
    }
}
'@

Add-Type -TypeDefinition $codigo -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Core
[PruebaFicha]::Cargar($Exe, $env:DIGBIT_CARPETA_DATOS)
$sumasAntes = $null
try {
    if ($Modo -ne 'Consulta') { $sumasAntes = [PruebaFicha]::Sumas($cadena) }
    switch ($Modo) {
        'Logica' { [PruebaFicha]::Logica() }
        'Pantalla' { [PruebaFicha]::Pantalla($Salida, -not $SinFotos, $Docente) }
        'Consulta' { [PruebaFicha]::Consulta($cadena, $Salida, -not $SinFotos) }
        'Kiosco' { [PruebaFicha]::Kiosco() }
    }
    if ($sumasAntes) {
        $sumasDespues = [PruebaFicha]::Sumas($cadena)
        if ($sumasAntes -ne $sumasDespues) { Write-Host "[FALLO] CHECKSUM distinto antes y despues:`n  $sumasAntes`n  $sumasDespues"; [PruebaFicha]::Fallos++ }
        else { Write-Host "[OK   ] CHECKSUM de las tablas igual antes y despues"; [PruebaFicha]::Oks++ }
    }
}
catch {
    Write-Host ("ERROR: " + $_.Exception.GetBaseException().ToString())
    Write-Host ([PruebaFicha]::Resultado())
    exit 2
}

Write-Host ([PruebaFicha]::Resultado())
if ([PruebaFicha]::Fallos -gt 0) { exit 1 }
