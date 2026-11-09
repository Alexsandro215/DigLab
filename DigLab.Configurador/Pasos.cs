using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DigLab.Configurador
{
    /// <summary>
    /// Lo que el Setup le pide, en orden: preparar (antes de copiar los archivos),
    /// configurar (despues) y revertir (al desinstalar). El equipo se acuerda de su tipo
    /// en HKLM\SOFTWARE\DigLab, para actualizar y desinstalar sin volver a preguntar.
    /// </summary>
    internal static class Pasos
    {
        private const string RutaRegistro = @"SOFTWARE\DigLab";

        /// <summary>
        /// La cuenta que se volvio kiosco. Se escribe ANTES de tocarla, y Modo solo al terminar:
        /// asi una instalacion a medias se reintenta completa, pero se puede deshacer. Si no,
        /// al desinstalar la cuenta se quedaria con DigLab de shell y sin DigLab: pantalla negra.
        /// </summary>
        private const string ValorCuentaKiosco = "CuentaKiosco";

        /// <summary>
        /// "1" si esa cuenta es la que el equipo ya tenia. Junto a CuentaKiosco y a la misma hora:
        /// el desinstalador lo mira para no ofrecer nunca borrarla.
        /// </summary>
        private const string ValorCuentaOriginal = "CuentaOriginal";

        /// <summary>Se lo dice a DigLab: que suelte sus directivas al entregar el escritorio.</summary>
        public const string AjusteLiberarDirectivas = "LiberarDirectivas";

        /// <summary>adm, maestro o kiosco.</summary>
        public const string ModoKiosco = "kiosco";

        public static bool EsKiosco(string modo)
        {
            return string.Equals(modo, ModoKiosco, StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        // Lo que hay instalado
        // =====================================================================

        /// <summary>
        /// Para que el asistente sea corto al actualizar: de que tipo es este equipo y si su
        /// conexion sigue sirviendo. Una instalacion del instalador anterior no tiene la clave
        /// en HKLM: se deduce de su .config y del inicio automatico.
        /// </summary>
        public static void Estado(string destino)
        {
            string modo = null;
            string cuenta = null;
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                using (RegistryKey clave = equipo.OpenSubKey(RutaRegistro))
                {
                    if (clave != null)
                    {
                        modo = clave.GetValue("Modo") as string;
                        cuenta = clave.GetValue("Cuenta") as string;
                    }
                }

                // Con la marca, que se escribe al final: una instalacion que se quedo a medias
                // no cuenta como instalada, o reintentar se saltaria lo que falto.
                if (modo == null && File.Exists(Path.Combine(destino, "DigLab.exe")) && File.Exists(Path.Combine(destino, ".instalado")))
                {
                    bool kiosco = string.Equals(Archivos.LeerAjuste(Archivos.ConfigDigLab(destino), "ModoKiosco"), "true", StringComparison.OrdinalIgnoreCase);
                    modo = kiosco ? ModoKiosco : "adm";
                    if (kiosco)
                    {
                        using (RegistryKey winlogon = equipo.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
                        {
                            cuenta = winlogon == null ? null : winlogon.GetValue("DefaultUserName") as string;
                        }
                    }
                }
            }

            if (modo == null)
            {
                Salida.Escribir("INSTALADO", "NO");
                return;
            }

            Salida.Escribir("INSTALADO", "SI");
            Salida.Escribir("MODO", modo);
            Salida.Escribir("CUENTA", cuenta ?? "");
            Salida.Escribir("LABORATORIO", Archivos.LeerAjuste(Archivos.ConfigDigLab(destino), "Laboratorio") ?? "");
            string cadena = Archivos.LeerConexion(destino);
            if (cadena == null)
            {
                Salida.Escribir("CONEXION", "NO");
                return;
            }

            try
            {
                BaseDatos.Laboratorios(cadena);
                Salida.Escribir("CONEXION", "OK");
            }
            catch (Exception)
            {
                // Sin red o con otra contrasena: la que tiene se conserva. Lo decide el asistente.
                Salida.Escribir("CONEXION", "FALLA");
            }
        }

        // =====================================================================
        // Antes de copiar
        // =====================================================================

        public static void Preparar(string destino, string modo, string rutaAjustes)
        {
            Salida.Paso("Preparar el equipo");
            if (rutaAjustes != null && File.Exists(Archivos.ConfigDigLab(destino)))
            {
                Sistema.Hacer("Guardar los ajustes de este equipo", () => Archivos.GuardarAjustes(destino, rutaAjustes));
            }

            Sistema.Hacer("Detener DigLab y el vigilante si están corriendo", () => Servicio.DetenerTodo(destino));
            Defender.Poner(destino, EsKiosco(modo));
        }

        // =====================================================================
        // Despues de copiar
        // =====================================================================

        public sealed class Datos
        {
            public string Destino;
            public string Modo;
            public string Laboratorio;
            public string NumeroMaquina;
            public string Cuenta;

            /// <summary>La cuenta es la que el equipo ya tenia: no se crea ni se cambia nada de ella.</summary>
            public bool Original;
            public bool Actualizar;
            public string RutaAjustes;
        }

        public static void Configurar(Datos datos)
        {
            bool kiosco = EsKiosco(datos.Modo);
            string cadena = BaseDatos.CadenaDelEntorno();
            string contrasena = Environment.GetEnvironmentVariable(Cuenta.VarClave);
            Salida.Secreto(contrasena);

            if (datos.Actualizar)
            {
                ConfigurarActualizacion(datos);
                return;
            }

            // Antes de escribir nada: lo que haria que todo lo demas sobrara.
            if (kiosco)
            {
                string porQue;
                if (datos.Original)
                {
                    // Comprobarla es intentar entrar a Windows: solo al instalar de verdad.
                    porQue = Sistema.Simular ? null : Cuenta.PorQueNoValeOriginal(datos.Cuenta, contrasena);
                }
                else
                {
                    if (string.IsNullOrEmpty(contrasena))
                    {
                        throw new FalloDeInstalacion("Falta la contraseña de la cuenta del laboratorio: hace falta para el inicio de sesión automático.");
                    }

                    porQue = Cuenta.PorQueNoVale(datos.Cuenta);
                }

                if (porQue != null)
                {
                    throw new FalloDeInstalacion(porQue);
                }

                if (string.IsNullOrWhiteSpace(datos.Laboratorio))
                {
                    throw new FalloDeInstalacion("Falta el laboratorio de este equipo.");
                }
            }

            if (cadena == null && Archivos.LeerConexion(datos.Destino) == null)
            {
                throw new FalloDeInstalacion("Este equipo no tiene conexión a la base y no se indicó ninguna: quedaría sin base de datos.");
            }

            Salida.Paso("Configuración");
            string laboratorio = kiosco ? ComprobarLaboratorio(datos.Laboratorio, cadena ?? Archivos.LeerConexion(datos.Destino)) : "";
            Dictionary<string, string> ajustes = new Dictionary<string, string>
            {
                { "ModoKiosco", kiosco ? "true" : "false" },
                { "Laboratorio", laboratorio },
                { AjusteLiberarDirectivas, kiosco && datos.Original ? "true" : "false" }
            };
            if (kiosco && !string.IsNullOrWhiteSpace(datos.NumeroMaquina))
            {
                ajustes["NumeroMaquina"] = datos.NumeroMaquina.Trim();
            }

            Sistema.Hacer("ModoKiosco=" + ajustes["ModoKiosco"] + ", Laboratorio='" + laboratorio + "'"
                + (ajustes.ContainsKey("NumeroMaquina") ? ", NumeroMaquina='" + ajustes["NumeroMaquina"] + "'" : "")
                + ", " + AjusteLiberarDirectivas + "=" + ajustes[AjusteLiberarDirectivas] + " en DigLab.exe.config",
                () => Archivos.EscribirAjustes(Archivos.ConfigDigLab(datos.Destino), ajustes));
            if (cadena != null)
            {
                Sistema.Hacer("Escribir connections.config (con la contraseña cifrada para este equipo)", () => Archivos.EscribirConexion(datos.Destino, cadena));
            }
            else
            {
                CifrarConexionSiHaceFalta(datos.Destino);
            }

            Sistema.Hacer("Dejar " + datos.Destino + " en solo lectura para los usuarios normales", () => Archivos.SoloLectura(datos.Destino));

            if (kiosco)
            {
                ConfigurarKiosco(datos, contrasena ?? "");
            }

            Sistema.Hacer("Escribir la marca de instalación", () => Archivos.Marca(datos.Destino));
            Sistema.Hacer("Recordar el tipo de equipo", () => Recordar(datos.Modo, kiosco ? datos.Cuenta : ""));
        }

        private static void ConfigurarKiosco(Datos datos, string contrasena)
        {
            CopiarMemorias(datos.Destino);

            string exeDigLab = Path.Combine(datos.Destino, "DigLab.exe");
            Sistema.Hacer("Anotar la cuenta del kiosco, para poder deshacerlo aunque esto no termine", () => AnotarCuentaKiosco(datos.Cuenta, datos.Original));
            if (datos.Original)
            {
                Salida.Paso("Cuenta «" + datos.Cuenta + "», la que ya tiene el equipo: no se cambia su contraseña ni sus permisos");
            }
            else
            {
                Salida.Paso("Cuenta local estándar «" + datos.Cuenta + "»");
                Sistema.Hacer(Cuenta.Existe(datos.Cuenta) ? "Ya existe: se fija la contraseña indicada, sin caducidad" : "Crear la cuenta (sin privilegios, contraseña sin caducidad)",
                    () => Cuenta.CrearOActualizar(datos.Cuenta, contrasena));
                Sistema.Hacer("Crear su perfil de Windows si aún no lo tiene", () => Cuenta.CrearPerfil(datos.Cuenta, contrasena));
            }

            Salida.Paso("Shell y directivas en el perfil de la cuenta");
            foreach (Kiosco.ValorUsuario valor in Kiosco.ValoresUsuario(exeDigLab))
            {
                Salida.Escribir(Sistema.Simular ? "SIMULADO" : "INFO", valor.Que + ": " + valor.Clave + " -> " + valor.Nombre + " = " + valor.Valor);
            }

            Sistema.Hacer("Escribir el shell y las directivas, e impedir que la cuenta cambie su propio shell", () =>
            {
                SecurityIdentifier sid = Cuenta.Sid(datos.Cuenta);
                Kiosco.ConRegistroDeLaCuenta(sid, raiz =>
                {
                    Kiosco.EscribirValores(raiz, Kiosco.ValoresUsuario(exeDigLab));
                    Kiosco.ProtegerShell(raiz, Kiosco.ClaveShell, sid);
                    if (datos.Original)
                    {
                        // Sin esto el kiosco funciona igual, pero el escritorio sale limitado.
                        try
                        {
                            Kiosco.PermitirSoltarDirectivas(raiz, sid);
                        }
                        catch (Exception ex)
                        {
                            Salida.Aviso("DigLab no podrá quitar los límites del escritorio tras la bitácora: " + ex.Message);
                        }
                    }
                });
            });

            Salida.Paso("Inicio de sesión automático");
            Sistema.Hacer("Winlogon: AutoAdminLogon=1, DefaultUserName=" + datos.Cuenta + ", DefaultDomainName=" + Environment.MachineName
                + ", DefaultPassword=(la indicada), AutoRestartShell=1, ForceAutoLogon=1; DevicePasswordLessBuildVersion=0; sin contraseña al despertar",
                () => Kiosco.InicioAutomatico(datos.Cuenta, contrasena));

            Salida.Paso("Servicio DigLab.Vigilante");
            string exeVigilante = Path.Combine(datos.Destino, "Vigilante", "DigLab.Vigilante.exe");
            Sistema.Hacer("Instalar y arrancar desde " + exeVigilante + " (también en modo seguro)", () => Servicio.Instalar(exeVigilante));
        }

        /// <summary>
        /// Instalar encima: los archivos ya son los nuevos. Se conservan el tipo de equipo,
        /// la conexion (el Setup no trae connections.config), la cuenta y sus directivas; solo
        /// se devuelven los ajustes y se vuelve a arrancar el vigilante.
        /// </summary>
        private static void ConfigurarActualizacion(Datos datos)
        {
            Salida.Paso("Actualizar");
            if (datos.RutaAjustes != null && File.Exists(datos.RutaAjustes))
            {
                Sistema.Hacer("Devolver los ajustes de este equipo", () => Archivos.ReponerAjustes(datos.Destino, datos.RutaAjustes));
            }

            CifrarConexionSiHaceFalta(datos.Destino);
            Sistema.Hacer("Dejar " + datos.Destino + " en solo lectura para los usuarios normales", () => Archivos.SoloLectura(datos.Destino));
            if (EsKiosco(datos.Modo))
            {
                CopiarMemorias(datos.Destino);
                string exeVigilante = Path.Combine(datos.Destino, "Vigilante", "DigLab.Vigilante.exe");
                Sistema.Hacer("Devolver el reinicio automático de DigLab", () => Kiosco.ReinicioDelShell(true));
                Sistema.Hacer("Arrancar el vigilante", () =>
                {
                    if (Servicio.Existe())
                    {
                        Servicio.Arrancar();
                    }
                    else
                    {
                        Servicio.Instalar(exeVigilante);
                    }
                });
            }

            Sistema.Hacer("Escribir la marca de instalación", () => Archivos.Marca(datos.Destino));
            Sistema.Hacer("Recordar el tipo de equipo", () => Recordar(datos.Modo, datos.Cuenta));
        }

        /// <summary>
        /// El nombre se comprueba contra la base: un acento o un espacio de mas deja el equipo
        /// rechazando TODOS los codigos, y antes eso se descubria en la primera clase. Sin
        /// distinguir mayusculas y sin espacios al borde, que es como se compara de verdad; se
        /// guarda como lo escribe la base. Sin base no se puede comprobar: se avisa.
        /// </summary>
        private static string ComprobarLaboratorio(string laboratorio, string cadena)
        {
            string buscado = laboratorio.Trim();
            List<string> nombres;
            try
            {
                nombres = BaseDatos.Laboratorios(cadena);
            }
            catch (Exception ex)
            {
                Salida.Aviso("No se pudo consultar la base (" + ex.Message + "). El equipo queda instalado, pero nadie comprobó que el laboratorio exista.");
                return buscado;
            }

            string canonico = nombres.FirstOrDefault(n => string.Equals(n.Trim(), buscado, StringComparison.OrdinalIgnoreCase));
            if (canonico == null)
            {
                throw new FalloDeInstalacion("El laboratorio «" + buscado + "» no existe en la base. Los que hay: " + string.Join(", ", nombres) + ".");
            }

            return canonico;
        }

        /// <summary>
        /// Las llaves de emergencia (db/28): ya no se autoriza ninguna memoria al instalar
        /// (pedido del 2026-11-07). Se registran en DigLab (Laboratorios > Memorias de
        /// emergencia) y cualquiera abre cualquier equipo. Aqui solo se copia la lista de la
        /// base al equipo, para que valga aunque arranque sin servidor; despues la renueva
        /// el servicio DigLab.Vigilante cada 10 minutos. Lo hace DigLab.exe
        /// --memorias-emergencia, el mismo codigo que usa el vigilante. Nunca detiene la
        /// instalacion: sin la lista, el vigilante la copia en cuanto haya conexion.
        /// </summary>
        private static void CopiarMemorias(string destino)
        {
            Salida.Paso("Memorias de emergencia");
            Sistema.Hacer("Copiar de la base las memorias registradas en DigLab (Laboratorios > Memorias de emergencia)", () =>
            {
                int codigo;
                string resultado = LanzarCopiaDeMemorias(Path.Combine(destino, "DigLab.exe"), out codigo);
                switch (codigo)
                {
                    case 0:
                        Salida.Escribir("INFO", resultado);
                        break;
                    case 3:
                        Salida.Aviso("La base todavía no registra memorias de emergencia (falta db/28): este equipo solo acepta la que "
                            + "se le autorizó en una instalación anterior, si la tiene.");
                        break;
                    case 1:
                        Salida.Aviso("No se pudo leer la base: el servicio DigLab.Vigilante copiará las memorias en cuanto haya conexión. ("
                            + resultado + ")");
                        break;
                    default:
                        Salida.Aviso("No se pudieron copiar las memorias de emergencia: " + resultado
                            + " El servicio DigLab.Vigilante lo intentará otra vez.");
                        break;
                }
            });
        }

        /// <summary>
        /// La conexion que el equipo ya tenia, en claro (de antes del cifrado, o de
        /// deploy\configurar_equipo.ps1): se reescribe cifrada para este equipo (pedido del
        /// 2026-11-08: que el estudiante que abre connections.config no vea la contrasena).
        /// La cuenta de Windows y todo lo demas no cambian.
        /// </summary>
        private static void CifrarConexionSiHaceFalta(string destino)
        {
            if (!Archivos.ConexionEnClaro(destino))
            {
                return;
            }

            Sistema.Hacer("Cifrar la contraseña de la base que ya tenía este equipo (connections.config)",
                () => Archivos.EscribirConexion(destino, Archivos.LeerConexion(destino)));
        }

        /// <summary>DigLab.exe --memorias-emergencia: lo que dijo, y su codigo de salida (-1 si no termino o no esta).</summary>
        private static string LanzarCopiaDeMemorias(string exe, out int codigo)
        {
            codigo = -1;
            if (!File.Exists(exe))
            {
                return "No está " + exe + ".";
            }

            ProcessStartInfo inicio = new ProcessStartInfo(exe, "--memorias-emergencia")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            };
            using (Process proceso = Process.Start(inicio))
            {
                Task<string> salida = proceso.StandardOutput.ReadToEndAsync();
                if (!proceso.WaitForExit(90000))
                {
                    try
                    {
                        proceso.Kill();
                    }
                    catch (Exception)
                    {
                        // Ya termino, o no se deja.
                    }

                    return "DigLab.exe no terminó en 90 s.";
                }

                codigo = proceso.ExitCode;
                return salida.Wait(5000) ? salida.Result.Trim() : "";
            }
        }

        private static void Recordar(string modo, string cuenta)
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey clave = equipo.CreateSubKey(RutaRegistro))
            {
                clave.SetValue("Modo", modo ?? "", RegistryValueKind.String);
                clave.SetValue("Cuenta", cuenta ?? "", RegistryValueKind.String);
                if (EsKiosco(modo) && !string.IsNullOrWhiteSpace(cuenta))
                {
                    clave.SetValue(ValorCuentaKiosco, cuenta, RegistryValueKind.String);
                }
            }
        }

        private static void AnotarCuentaKiosco(string cuenta, bool original)
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey clave = equipo.CreateSubKey(RutaRegistro))
            {
                clave.SetValue(ValorCuentaKiosco, cuenta, RegistryValueKind.String);
                clave.SetValue(ValorCuentaOriginal, original ? "1" : "0", RegistryValueKind.String);
            }
        }

        /// <summary>Si el kiosco de este equipo se quedo en la cuenta que ya tenia.</summary>
        public static bool CuentaEsOriginal()
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey clave = equipo.OpenSubKey(RutaRegistro))
            {
                return clave != null && (clave.GetValue(ValorCuentaOriginal) as string) == "1";
            }
        }

        // =====================================================================
        // Al desinstalar
        // =====================================================================

        /// <summary>
        /// Deshace lo del kiosco. Sigue aunque algo falle: dejar el kiosco a medio deshacer
        /// es peor, y el equipo tiene que poder volver a entrar. Lo que borra datos va aparte.
        /// </summary>
        public static void Revertir(string destino, bool borrarCuenta, bool borrarDatos)
        {
            string cuenta = null;
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey clave = equipo.OpenSubKey(RutaRegistro))
            {
                if (clave != null)
                {
                    cuenta = clave.GetValue(ValorCuentaKiosco) as string;
                }
            }

            bool original = CuentaEsOriginal();
            Salida.Paso("Deshacer la configuración de este equipo");
            Intentar("Detener DigLab y el vigilante", () => Servicio.DetenerTodo(destino));
            Intentar("Quitar el servicio DigLab.Vigilante", Servicio.Quitar);
            Defender.Quitar(destino);

            // Sin saber que era un kiosco de DigLab no se toca Winlogon: en el equipo del
            // administrador el inicio de sesion automatico puede ser suyo.
            if (!string.IsNullOrWhiteSpace(cuenta))
            {
                Intentar("Apagar el inicio de sesión automático y borrar la contraseña guardada", Kiosco.QuitarInicioAutomatico);
                if (Cuenta.Existe(cuenta))
                {
                    string exeDigLab = Path.Combine(destino, "DigLab.exe");
                    Intentar("Quitar el shell y las directivas de «" + cuenta + "»", () =>
                        Kiosco.ConRegistroDeLaCuenta(Cuenta.Sid(cuenta), raiz => Kiosco.BorrarValores(raiz, Kiosco.ValoresUsuario(exeDigLab))));
                    if (original)
                    {
                        Intentar("Devolverle a «" + cuenta + "» sus permisos sobre su registro", () =>
                        {
                            SecurityIdentifier sid = Cuenta.Sid(cuenta);
                            Kiosco.ConRegistroDeLaCuenta(sid, raiz =>
                            {
                                Kiosco.DesprotegerShell(raiz, Kiosco.ClaveShell, sid);
                                Kiosco.QuitarPermisoDirectivas(raiz, sid);
                            });
                        });
                    }

                    if (borrarCuenta)
                    {
                        if (SePuedeBorrar(cuenta, original))
                        {
                            Intentar("Borrar la cuenta «" + cuenta + "» y su perfil", () => Cuenta.Borrar(cuenta));
                        }
                        else
                        {
                            Salida.Aviso("La cuenta «" + cuenta + "» no se borra: es la del equipo, o es administradora.");
                        }
                    }
                }
            }

            if (Servicio.ApagoReinicioDelShell)
            {
                Intentar("Devolver el reinicio automático del escritorio de Windows", () => Kiosco.ReinicioDelShell(true));
            }

            if (borrarDatos)
            {
                Intentar("Borrar los datos locales de DigLab", BorrarDatos);
            }

            Intentar("Olvidar el tipo de equipo", () =>
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    equipo.DeleteSubKeyTree(RutaRegistro, false);
                }
            });
        }

        /// <summary>
        /// El Setup no ofrece borrar la cuenta original; esto es por si algo lo pide igual. Borrar
        /// la cuenta de todos los dias, o la que esta desinstalando, no tiene vuelta: en la duda, no.
        /// </summary>
        private static bool SePuedeBorrar(string cuenta, bool original)
        {
            if (original || string.Equals(cuenta, Environment.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                return !Cuenta.EsAdministradora(cuenta);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Intentar(string descripcion, Action accion)
        {
            try
            {
                Sistema.Hacer(descripcion, accion);
            }
            catch (Exception ex)
            {
                Salida.Aviso(descripcion + ": " + ex.Message);
            }
        }

        private static void BorrarDatos()
        {
            List<string> carpetas = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DigLab")
            };
            string usuarios = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (usuarios != null && Directory.Exists(usuarios))
            {
                foreach (string perfil in Directory.GetDirectories(usuarios))
                {
                    carpetas.Add(Path.Combine(perfil, "AppData", "Local", "DigLab"));
                }
            }

            foreach (string carpeta in carpetas.Where(Directory.Exists))
            {
                try
                {
                    Directory.Delete(carpeta, true);
                }
                catch (Exception ex)
                {
                    Salida.Aviso("No se pudo borrar " + carpeta + ": " + ex.Message);
                }
            }
        }
    }
}
