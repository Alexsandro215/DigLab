using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DigLab.Configurador
{
    /// <summary>
    /// Lo que el asistente de Inno Setup (instalador\DigLab.iss) no puede hacer por si
    /// mismo: comprobar la base y configurar el equipo. Sin ventana; habla por el archivo
    /// de --salida (Salida). Las contrasenas llegan en variables de entorno, nunca en la
    /// linea de comandos. Sin la marca del Setup (Sistema.VariableSetup), solo simula.
    ///
    /// Ordenes:
    ///   probar                    la conexion del entorno: ESTADO y los LAB
    ///   estado                    lo que hay instalado
    ///   memorias                  las memorias USB conectadas
    ///   validar-cuenta --cuenta   si vale para el kiosco: ESTADO OK o el motivo. Con --original,
    ///                             la que ya tiene el equipo, comprobando su contrasena
    ///   preparar                  antes de copiar: detener, Defender, guardar ajustes
    ///   configurar                despues de copiar
    ///   diagnostico               si quedo bien
    ///   revertir                  al desinstalar
    ///   prueba-registro           solo desarrollo: en una rama propia de HKCU
    /// Opciones: --destino --modo --cuenta --original --laboratorio --numero --memoria --actualizar
    /// --ajustes --borrar-cuenta --borrar-datos --simular --salida
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Dictionary<string, string> opciones = LeerOpciones(args);
            string orden = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            bool escribe = orden == "preparar" || orden == "configurar" || orden == "revertir";
            Sistema.Decidir(opciones.ContainsKey("simular"));
            // El diagnostico solo lee, pero va al log: es lo que se pide cuando algo no quedo bien.
            Salida.Preparar(Opcion(opciones, "salida"), (escribe || orden == "diagnostico") && !Sistema.Simular);

            // MySql.Data y lo suyo viajan junto al .exe, pero al desinstalar solo queda el
            // .exe, y esa orden no los pide.
            string aqui = AppDomain.CurrentDomain.BaseDirectory;
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string dll = Path.Combine(aqui, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
            };

            try
            {
                if (escribe)
                {
                    Sistema.ExigirPermiso();
                }

                return Ejecutar(orden, opciones);
            }
            catch (FalloDeInstalacion ex)
            {
                Salida.Error(ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Salida.Error(ex.GetType().Name + ": " + ex.Message);
                return 2;
            }
        }

        private static int Ejecutar(string orden, Dictionary<string, string> opciones)
        {
            string destino = Opcion(opciones, "destino") ?? Sistema.CarpetaPorOmision;
            string modo = Opcion(opciones, "modo") ?? "adm";
            string cuenta = Opcion(opciones, "cuenta");
            switch (orden)
            {
                case "probar":
                    string cadena = BaseDatos.CadenaDelEntorno();
                    if (cadena == null)
                    {
                        throw new FalloDeInstalacion("No vino ninguna conexión que probar.");
                    }

                    BaseDatos.Probar(cadena);
                    return 0;
                case "estado":
                    Pasos.Estado(destino);
                    return 0;
                case "memorias":
                    Pasos.Memorias();
                    return 0;
                case "validar-cuenta":
                    string porQue;
                    if (opciones.ContainsKey("original"))
                    {
                        string claveCuenta = Environment.GetEnvironmentVariable(Cuenta.VarClave);
                        Salida.Secreto(claveCuenta);
                        porQue = Cuenta.PorQueNoValeOriginal(cuenta, claveCuenta);
                    }
                    else
                    {
                        porQue = Cuenta.PorQueNoVale(cuenta);
                    }

                    Salida.Escribir("ESTADO", porQue ?? "OK");
                    return 0;
                case "preparar":
                    Pasos.Preparar(destino, modo, Opcion(opciones, "ajustes"));
                    return 0;
                case "configurar":
                    Pasos.Configurar(new Pasos.Datos
                    {
                        Destino = destino,
                        Modo = modo,
                        Cuenta = cuenta,
                        Original = opciones.ContainsKey("original"),
                        Laboratorio = Opcion(opciones, "laboratorio"),
                        NumeroMaquina = Opcion(opciones, "numero"),
                        Memoria = opciones.ContainsKey("memoria"),
                        Actualizar = opciones.ContainsKey("actualizar"),
                        RutaAjustes = Opcion(opciones, "ajustes")
                    });
                    return 0;
                case "diagnostico":
                    return Diagnostico.Revisar(destino, modo, cuenta) == 0 ? 0 : 3;
                case "revertir":
                    Pasos.Revertir(destino, opciones.ContainsKey("borrar-cuenta"), opciones.ContainsKey("borrar-datos"));
                    return 0;
                case "prueba-registro":
                    Kiosco.ProbarEnRamaPropia();
                    return 0;
                case "prueba-archivos":
                    ProbarArchivos(destino);
                    return 0;
                case "prueba-cuenta":
                    Cuenta.ProbarSinGuardar();
                    Servicio.ProbarLlamadaDeCierre();
                    return 0;
                default:
                    throw new FalloDeInstalacion("Orden desconocida: " + orden);
            }
        }

        /// <summary>Solo desarrollo y solo dentro de la carpeta temporal: los .config, la conexion y los permisos.</summary>
        private static void ProbarArchivos(string destino)
        {
            string temporal = Path.GetFullPath(Path.GetTempPath());
            if (!Path.GetFullPath(destino).StartsWith(temporal, StringComparison.OrdinalIgnoreCase))
            {
                throw new FalloDeInstalacion("Esta prueba solo corre dentro de " + temporal + ".");
            }

            Action<bool, string> ver = (bien, que) => Salida.Escribir(bien ? "BIEN" : "MAL", que);
            Directory.CreateDirectory(Path.Combine(destino, "Vigilante"));
            string config = Archivos.ConfigDigLab(destino);
            const string Vacio = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration>\r\n  <runtime><x /></runtime>\r\n  <appSettings>\r\n    <add key=\"Otro\" value=\"sigue\" />\r\n    <add key=\"Laboratorio\" value=\"\" />\r\n  </appSettings>\r\n</configuration>\r\n";
            File.WriteAllText(config, Vacio);
            File.WriteAllText(Archivos.ConfigVigilante(destino), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration />\r\n");

            Archivos.EscribirAjustes(config, new Dictionary<string, string> { { "ModoKiosco", "true" }, { "Laboratorio", "Cómputo & Redes «1»" }, { "NumeroMaquina", "12" }, { "CarpetaDatos", @"D:\Datos" }, { "ClaveRecuperacion", "clave=de-prueba-sin-valor" }, { "UnaClaveQueAunNoExiste", "sigue" } });
            Archivos.EscribirAjustes(Archivos.ConfigVigilante(destino), new Dictionary<string, string> { { "CarpetaDatos", @"D:\Datos" } });
            ver(Archivos.LeerAjuste(config, "Laboratorio") == "Cómputo & Redes «1»", "el laboratorio, con acentos y &");
            ver(Archivos.LeerAjuste(config, "Otro") == "sigue", "no toca lo demás del .config");
            ver(File.ReadAllText(config).Contains("<runtime>"), "conserva sus otras secciones");

            string ajustes = Path.Combine(destino, "ajustes.txt");
            Archivos.GuardarAjustes(destino, ajustes);
            ver(!File.ReadAllText(ajustes, System.Text.Encoding.UTF8).Contains("de-prueba") && !System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(ajustes)).Contains("de-prueba"), "los ajustes guardados no se leen en claro");
            File.WriteAllText(config, Vacio);
            File.WriteAllText(Archivos.ConfigVigilante(destino), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration />\r\n");
            Archivos.ReponerAjustes(destino, ajustes);
            ver(Archivos.LeerAjuste(config, "ModoKiosco") == "true" && Archivos.LeerAjuste(config, "NumeroMaquina") == "12", "al actualizar se reponen los ajustes");
            ver(Archivos.LeerAjuste(config, "ClaveRecuperacion") == "clave=de-prueba-sin-valor", "también la clave de recuperación, con un = dentro");
            ver(Archivos.LeerAjuste(config, "UnaClaveQueAunNoExiste") == "sigue", "y cualquier clave con valor, aunque nadie la conozca aquí");
            ver(Archivos.LeerAjuste(config, "Otro") == "sigue", "lo nuevo del .config se queda");
            ver(Archivos.LeerAjuste(Archivos.ConfigVigilante(destino), "CarpetaDatos") == @"D:\Datos", "la carpeta de datos, también en el vigilante");

            System.Data.Common.DbConnectionStringBuilder cadena = new System.Data.Common.DbConnectionStringBuilder();
            cadena["Database"] = "b";
            cadena["Server"] = "s";
            cadena["Password"] = "a;b\"c'<&>";
            Archivos.EscribirConexion(destino, cadena.ConnectionString);
            ver(Archivos.LeerConexion(destino) == cadena.ConnectionString, "la conexión vuelve igual, con una contraseña con ; comillas y <&>");

            File.WriteAllText(Archivos.RutaConexion(destino), "<connectionStrings><add name=\"DigLab\" connectionString=\"Database=NOMBRE_BD;Server=SERVIDOR\" /></connectionStrings>");
            ver(Archivos.LeerConexion(destino) == null, "la plantilla no cuenta como conexión");

            Archivos.SoloLectura(destino);
            System.Security.AccessControl.AuthorizationRuleCollection reglas = new DirectoryInfo(destino).GetAccessControl()
                .GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
            Func<string, System.Security.AccessControl.FileSystemRights?> derechos = sid => reglas.Cast<System.Security.AccessControl.FileSystemAccessRule>()
                .Where(r => r.IdentityReference.Value == sid).Select(r => (System.Security.AccessControl.FileSystemRights?)r.FileSystemRights).FirstOrDefault();
            ver(reglas.Count == 3, "solo tres reglas, sin herencia (" + reglas.Count + ")");
            ver(derechos("S-1-5-32-544") == System.Security.AccessControl.FileSystemRights.FullControl, "administradores: control total");
            ver(derechos("S-1-5-18") == System.Security.AccessControl.FileSystemRights.FullControl, "SYSTEM: control total");
            // Windows le agrega Synchronize a la regla: no da nada que escribir.
            System.Security.AccessControl.FileSystemRights? usuarios = derechos("S-1-5-32-545");
            ver(usuarios.HasValue && (usuarios.Value & ~(System.Security.AccessControl.FileSystemRights.ReadAndExecute | System.Security.AccessControl.FileSystemRights.Synchronize)) == 0, "usuarios: solo leer y ejecutar");
        }

        private static Dictionary<string, string> LeerOpciones(string[] args)
        {
            Dictionary<string, string> opciones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string clave = args[i].Substring(2);
                bool conValor = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                opciones[clave] = conValor ? args[++i] : "";
            }

            return opciones;
        }

        private static string Opcion(Dictionary<string, string> opciones, string clave)
        {
            string valor;
            return opciones.TryGetValue(clave, out valor) && valor.Length > 0 ? valor : null;
        }
    }
}
