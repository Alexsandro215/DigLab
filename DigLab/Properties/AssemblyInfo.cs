using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// La información general de un ensamblado se controla mediante el siguiente 
// conjunto de atributos. Cambie estos valores de atributo para modificar la información
// asociada con un ensamblado.
[assembly: AssemblyTitle("DigLab")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("DigLab")]
[assembly: AssemblyCopyright("Copyright ©  2024")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Si establece ComVisible en false, los tipos de este ensamblado no estarán visibles 
// para los componentes COM.  Si es necesario obtener acceso a un tipo en este ensamblado desde 
// COM, establezca el atributo ComVisible en true en este tipo.
[assembly: ComVisible(false)]

// El siguiente GUID sirve como id. de typelib si este proyecto se expone a COM.
[assembly: Guid("35eb3151-bf24-46e9-8bf5-384a8a0df1b3")]

// La información de versión de un ensamblado consta de los cuatro valores siguientes:
//
//      Versión principal
//      Versión secundaria
//      Número de compilación
//      Revisión
//
// Puede especificar todos los valores o usar los valores predeterminados de número de compilación y de revisión
// utilizando el carácter "*", como se muestra a continuación:
// [assembly: AssemblyVersion("1.0.*")]
// 1.1: modos de acceso a las clases (db/11). deploy\diagnostico_equipo.ps1 avisa
// de los equipos con una anterior, que pediria siempre el codigo fijo.
// 1.2: memorias de emergencia en la base (db/28), reinicio al terminar la clase, conexion cifrada,
// lista en Excel, fallas resueltas (db/26) y suspensiones (db/27).
// 1.2.1: el horario en Excel (cuadricula con lista desplegable y color del docente) y sin
// "Dar de baja el horario actual". Sin migracion.
// 1.2.3: actualizar los equipos desde la base (db/29): "Version de DigLab..." en Laboratorios,
// aviso al abrir DigLab, boton en el kiosco y el vigilante que la instala solo.
// 1.3.0: apagar los equipos desde Laboratorios (ordenes_equipos), indicador de internet y
// version a la vista en el kiosco, sin "Crear cuenta" ni contrasena o correo de estudiantes
// en la administracion, suspension hasta el inicio del dia, los cambios de cada version con
// sus autores, y Bitacoras ya no pinta de verde una sesion con avisos de estudiantes pendientes.
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]
