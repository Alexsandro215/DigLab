using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace DigLab.Configurador
{
    /// <summary>
    /// Lo que convierte un equipo en kiosco: DigLab como shell SOLO de la cuenta del
    /// laboratorio (los administradores siguen con explorer.exe), sus directivas y el
    /// inicio de sesion automatico. Es lo de deploy\configurar_equipo.ps1, con las mismas
    /// claves y en el mismo orden, sin PowerShell.
    /// </summary>
    internal static class Kiosco
    {
        private const string RutaWinlogonUsuario = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private const string RutaWinlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private const string RutaPasswordLess = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device";
        private const string RamaCargada = "DigLabCuenta";
        private const string PoliticasSistema = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string PoliticasExplorador = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        /// <summary>La clave con el shell de la cuenta, relativa a su registro.</summary>
        public const string ClaveShell = RutaWinlogonUsuario;

        /// <summary>
        /// En la cuenta original, lo que DigLab quita al entregar el escritorio y vuelve a poner
        /// al arrancar. Las mismas que DigLab\Infraestructura\DirectivasKiosco.cs.
        /// </summary>
        public static readonly string[] QueSeSueltan = { "DisableTaskMgr", "NoRun", "NoControlPanel" };

        /// <summary>Un valor en el registro de la cuenta (rutas relativas a su HKU\SID).</summary>
        internal sealed class ValorUsuario
        {
            public string Clave;
            public string Nombre;
            public object Valor;
            public string Que;

            public ValorUsuario(string clave, string nombre, object valor, string que)
            {
                Clave = clave;
                Nombre = nombre;
                Valor = valor;
                Que = que;
            }
        }

        public static List<ValorUsuario> ValoresUsuario(string exeDigLab)
        {
            const string Sistema = PoliticasSistema;
            const string Explorador = PoliticasExplorador;
            const string Escritorio = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
            return new List<ValorUsuario>
            {
                new ValorUsuario(RutaWinlogonUsuario, "Shell", exeDigLab, "DigLab como shell de la cuenta"),
                new ValorUsuario(Sistema, "DisableTaskMgr", 1, "sin Administrador de tareas"),
                new ValorUsuario(Sistema, "DisableLockWorkstation", 1, "sin bloquear el equipo"),
                new ValorUsuario(Sistema, "DisableChangePassword", 1, "sin cambiar contrasena"),
                new ValorUsuario(Explorador, "NoRun", 1, "sin Ejecutar (Win+R)"),
                new ValorUsuario(Explorador, "NoLogoff", 1, "sin Cerrar sesion en Ctrl+Alt+Supr"),
                new ValorUsuario(Explorador, "StartMenuLogOff", 1, "sin Cerrar sesion en Inicio"),
                new ValorUsuario(Escritorio, "ScreenSaveActive", "0", "sin protector de pantalla"),
                new ValorUsuario(Escritorio, "ScreenSaverIsSecure", "0", "protector sin contrasena"),
                // Atajos de accesibilidad. Cinco pulsaciones de Mayus abren el dialogo de
                // Teclas especiales, que lo lanza Winlogon y aparece INCLUSO en una sesion
                // sin shell; desde su enlace se llega al Panel de control. Quitar el bit 4
                // (HOTKEYACTIVE) deja la funcion en Configuracion y mata el atajo.
                new ValorUsuario(@"Control Panel\Accessibility\StickyKeys", "Flags", "506", "sin atajo de Teclas especiales (5 x Mayus)"),
                new ValorUsuario(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58", "sin atajo de Teclas de alternancia"),
                new ValorUsuario(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122", "sin atajo de Teclas filtro"),
                new ValorUsuario(@"Control Panel\Accessibility\HighContrast", "Flags", "122", "sin atajo de Contraste alto"),
                // Cierra la puerta de atras del dialogo de accesibilidad. OJO: tambien afecta
                // al escritorio que se le entrega al alumno tras la bitacora.
                new ValorUsuario(Explorador, "NoControlPanel", 1, "sin Panel de control ni Configuracion")
            };
        }

        /// <summary>
        /// Abre el registro de la cuenta: su HKU\SID si tiene sesion abierta, o su
        /// NTUSER.DAT cargado a mano. Con reg.exe, que ya sabe tomar los privilegios que
        /// hacen falta para cargar una colmena.
        /// </summary>
        public static void ConRegistroDeLaCuenta(SecurityIdentifier sid, Action<RegistryKey> accion)
        {
            using (RegistryKey usuarios = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64))
            {
                using (RegistryKey abierta = usuarios.OpenSubKey(sid.Value, true))
                {
                    if (abierta != null)
                    {
                        accion(abierta);
                        return;
                    }
                }

                string perfil = Cuenta.Perfil(sid);
                if (perfil == null)
                {
                    throw new FalloDeInstalacion("La cuenta del laboratorio no tiene perfil todavía: no se puede escribir su registro.");
                }

                string ntuser = Path.Combine(perfil, "NTUSER.DAT");
                if (!File.Exists(ntuser))
                {
                    throw new FalloDeInstalacion("No se encontró " + ntuser + ".");
                }

                // Recien cerrada la sesion, Windows puede seguir soltando ese archivo.
                for (int intento = 1; ; intento++)
                {
                    try
                    {
                        Sistema.ExigirCero("reg.exe", "load \"HKU\\" + RamaCargada + "\" \"" + ntuser + "\"");
                        break;
                    }
                    catch (FalloDeInstalacion) when (intento < 5)
                    {
                        System.Threading.Thread.Sleep(3000);
                    }
                }

                try
                {
                    using (RegistryKey cargada = usuarios.OpenSubKey(RamaCargada, true))
                    {
                        accion(cargada);
                    }
                }
                finally
                {
                    // Sin esto la colmena sigue ocupada y no se puede descargar.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Sistema.Ejecutar("reg.exe", "unload \"HKU\\" + RamaCargada + "\"");
                }
            }
        }

        public static void EscribirValores(RegistryKey raiz, IEnumerable<ValorUsuario> valores)
        {
            foreach (ValorUsuario valor in valores)
            {
                using (RegistryKey clave = raiz.CreateSubKey(valor.Clave))
                {
                    clave.SetValue(valor.Nombre, valor.Valor, valor.Valor is int ? RegistryValueKind.DWord : RegistryValueKind.String);
                }
            }
        }

        public static void BorrarValores(RegistryKey raiz, IEnumerable<ValorUsuario> valores)
        {
            foreach (ValorUsuario valor in valores)
            {
                using (RegistryKey clave = raiz.OpenSubKey(valor.Clave, true))
                {
                    if (clave != null)
                    {
                        clave.DeleteValue(valor.Nombre, false);
                    }
                }
            }
        }

        /// <summary>
        /// La clave Shell vive en la colmena del PROPIO alumno, que la hereda con control
        /// total: podria poner Shell=explorer.exe y entrar a un escritorio completo sin
        /// pasar por DigLab. Se le deja solo lectura, con el mismo patron con que Windows
        /// protege la clave de Policies: un descriptor NUEVO y escrito entero (quitar reglas
        /// de uno existente falla, porque las heredadas se vuelven copias y esta clave lleva
        /// ACEs de contenedores de aplicacion sin nombre). Se relee para comprobarlo.
        /// </summary>
        public static void ProtegerShell(RegistryKey raiz, string ruta, SecurityIdentifier sidCuenta)
        {
            const InheritanceFlags Hereda = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            RegistrySecurity acl = new RegistrySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (KeyValuePair<string, RegistryRights> par in new[]
            {
                new KeyValuePair<string, RegistryRights>("S-1-5-18", RegistryRights.FullControl),
                new KeyValuePair<string, RegistryRights>("S-1-5-32-544", RegistryRights.FullControl),
                new KeyValuePair<string, RegistryRights>("S-1-5-12", RegistryRights.ReadKey),
                new KeyValuePair<string, RegistryRights>("S-1-15-2-1", RegistryRights.ReadKey)
            })
            {
                acl.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(par.Key), par.Value, Hereda, PropagationFlags.None, AccessControlType.Allow));
            }

            acl.AddAccessRule(new RegistryAccessRule(sidCuenta, RegistryRights.ReadKey, Hereda, PropagationFlags.None, AccessControlType.Allow));
            using (RegistryKey clave = raiz.OpenSubKey(ruta, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey))
            {
                clave.SetAccessControl(acl);
            }

            string sobra = PermisoDeMas(raiz, ruta, sidCuenta);
            if (sobra != null)
            {
                throw new FalloDeInstalacion("La cuenta sigue con " + sobra + " sobre su clave Winlogon: podría devolver el shell a explorer.exe.");
            }
        }

        /// <summary>
        /// Lo contrario de ProtegerShell, para la cuenta original al desinstalar: es la cuenta de
        /// todos los dias y vuelve a mandar en su clave, con los permisos que hereda. La regla
        /// explicita no sobra: un descriptor sin ninguna se escribiria como "todos, control total".
        /// </summary>
        public static void DesprotegerShell(RegistryKey raiz, string ruta, SecurityIdentifier sidCuenta)
        {
            RegistrySecurity acl = new RegistrySecurity();
            acl.SetAccessRuleProtection(false, false);
            acl.AddAccessRule(new RegistryAccessRule(sidCuenta, RegistryRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            using (RegistryKey clave = raiz.OpenSubKey(ruta, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey))
            {
                if (clave != null)
                {
                    clave.SetAccessControl(acl);
                }
            }
        }

        /// <summary>
        /// En la cuenta original, que DigLab pueda soltar y poner sus directivas. Windows protege
        /// esas claves: ni la propia cuenta las escribe sin elevar, aunque sea administradora.
        /// Solo escribir valores, y solo en esas dos claves.
        /// </summary>
        public static void PermitirSoltarDirectivas(RegistryKey raiz, SecurityIdentifier sidCuenta)
        {
            foreach (string ruta in new[] { PoliticasSistema, PoliticasExplorador })
            {
                using (RegistryKey clave = raiz.OpenSubKey(ruta, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey))
                {
                    RegistrySecurity acl = clave.GetAccessControl(AccessControlSections.Access);
                    acl.AddAccessRule(new RegistryAccessRule(sidCuenta, RegistryRights.ReadKey | RegistryRights.SetValue, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
                    clave.SetAccessControl(acl);
                }
            }
        }

        public static void QuitarPermisoDirectivas(RegistryKey raiz, SecurityIdentifier sidCuenta)
        {
            foreach (string ruta in new[] { PoliticasSistema, PoliticasExplorador })
            {
                using (RegistryKey clave = raiz.OpenSubKey(ruta, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey))
                {
                    if (clave != null)
                    {
                        RegistrySecurity acl = clave.GetAccessControl(AccessControlSections.Access);
                        acl.RemoveAccessRuleAll(new RegistryAccessRule(sidCuenta, RegistryRights.ReadKey, AccessControlType.Allow));
                        clave.SetAccessControl(acl);
                    }
                }
            }
        }

        /// <summary>Si la cuenta puede escribir valores en las dos claves, con una regla suya.</summary>
        public static bool PuedeSoltarDirectivas(RegistryKey raiz, SecurityIdentifier sidCuenta)
        {
            foreach (string ruta in new[] { PoliticasSistema, PoliticasExplorador })
            {
                using (RegistryKey clave = raiz.OpenSubKey(ruta, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions))
                {
                    if (clave == null)
                    {
                        return false;
                    }

                    bool tiene = false;
                    foreach (RegistryAccessRule regla in clave.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)))
                    {
                        tiene |= regla.AccessControlType == AccessControlType.Allow && regla.IdentityReference.Value == sidCuenta.Value
                            && (regla.RegistryRights & RegistryRights.SetValue) == RegistryRights.SetValue;
                    }

                    if (!tiene)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Si Windows vuelve a abrir el shell cuando muere. Se apaga para actualizar o desinstalar
        /// desde la propia cuenta del kiosco; con DigLab de shell, si no, vuelve a abrirlo y su
        /// archivo sigue en uso. Vale al momento, sin reiniciar.
        /// </summary>
        public static void ReinicioDelShell(bool encendido)
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey winlogon = equipo.CreateSubKey(RutaWinlogon))
            {
                winlogon.SetValue("AutoRestartShell", encendido ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        /// <summary>El permiso de mas que tiene la cuenta sobre la clave (distinto de leer), o null.</summary>
        public static string PermisoDeMas(RegistryKey raiz, string ruta, SecurityIdentifier sidCuenta)
        {
            using (RegistryKey clave = raiz.OpenSubKey(ruta, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions))
            {
                if (clave == null)
                {
                    return null;
                }

                foreach (RegistryAccessRule regla in clave.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    if (regla.AccessControlType == AccessControlType.Allow && regla.RegistryRights != RegistryRights.ReadKey
                        && regla.IdentityReference.Value == sidCuenta.Value)
                    {
                        return regla.RegistryRights.ToString();
                    }
                }
            }

            return null;
        }

        public static void InicioAutomatico(string cuenta, string contrasena)
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                using (RegistryKey winlogon = equipo.CreateSubKey(RutaWinlogon))
                {
                    winlogon.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
                    winlogon.SetValue("DefaultUserName", cuenta, RegistryValueKind.String);
                    winlogon.SetValue("DefaultDomainName", Environment.MachineName, RegistryValueKind.String);
                    winlogon.SetValue("DefaultPassword", contrasena, RegistryValueKind.String);
                    winlogon.DeleteValue("AutoLogonCount", false);
                    // Si DigLab termina, Windows lo relanza.
                    winlogon.SetValue("AutoRestartShell", 1, RegistryValueKind.DWord);
                    // AutoAdminLogon sirve para el ARRANQUE. Cuando el vigilante cierra la
                    // sesion al terminar la clase no hay arranque: sin esto el equipo se queda
                    // en la pantalla de contrasena y el siguiente alumno no puede entrar.
                    winlogon.SetValue("ForceAutoLogon", "1", RegistryValueKind.String);
                }

                // En Windows 10/11 recientes, en 2 hace que Windows ignore AutoAdminLogon
                // con cuentas locales.
                using (RegistryKey sinContrasena = equipo.CreateSubKey(RutaPasswordLess))
                {
                    sinContrasena.SetValue("DevicePasswordLessBuildVersion", 0, RegistryValueKind.DWord);
                }
            }

            PedirContrasenaAlDespertar(false);
        }

        public static void QuitarInicioAutomatico()
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey winlogon = equipo.CreateSubKey(RutaWinlogon))
            {
                winlogon.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
                winlogon.DeleteValue("DefaultPassword", false);
                winlogon.DeleteValue("ForceAutoLogon", false);
            }

            PedirContrasenaAlDespertar(true);
        }

        private static void PedirContrasenaAlDespertar(bool pedir)
        {
            string valor = pedir ? "1" : "0";
            Sistema.Ejecutar("powercfg.exe", "/SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK " + valor);
            Sistema.Ejecutar("powercfg.exe", "/SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK " + valor);
            Sistema.Ejecutar("powercfg.exe", "/SETACTIVE SCHEME_CURRENT");
        }

        /// <summary>
        /// Valores de la prueba de desarrollo: escribe y protege en una rama propia de
        /// HKCU (nunca lo del kiosco de verdad), lo comprueba y la borra.
        /// </summary>
        public static void ProbarEnRamaPropia()
        {
            const string Rama = @"Software\DigLabPruebaConfigurador";
            using (WindowsIdentity yo = WindowsIdentity.GetCurrent())
            using (RegistryKey usuario = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                usuario.DeleteSubKeyTree(Rama, false);
                using (RegistryKey raiz = usuario.CreateSubKey(Rama))
                {
                    List<ValorUsuario> valores = ValoresUsuario(@"C:\DigLab\DigLab.exe");
                    EscribirValores(raiz, valores);
                    foreach (ValorUsuario valor in valores)
                    {
                        using (RegistryKey clave = raiz.OpenSubKey(valor.Clave))
                        {
                            object leido = clave.GetValue(valor.Nombre);
                            bool bien = Equals(leido, valor.Valor);
                            Salida.Escribir(bien ? "BIEN" : "MAL", valor.Clave + " -> " + valor.Nombre + " = " + leido);
                        }
                    }

                    BorrarValores(raiz, valores.Where(v => v.Nombre == "NoRun"));
                    using (RegistryKey clave = raiz.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"))
                    {
                        Salida.Escribir(clave.GetValue("NoRun") == null ? "BIEN" : "MAL", "BorrarValores quita NoRun");
                    }

                    // La regla de leer de la cuenta (aqui, la de quien prueba): sin escribir.
                    string antes = PermisoDeMas(raiz, RutaWinlogonUsuario, yo.User);
                    Salida.Escribir(antes != null ? "BIEN" : "MAL", "antes de proteger tiene: " + antes);
                    ProtegerShell(raiz, RutaWinlogonUsuario, yo.User);
                    Salida.Escribir(PermisoDeMas(raiz, RutaWinlogonUsuario, yo.User) == null ? "BIEN" : "MAL", "protegida: solo lectura");
                    bool negado = false;
                    try
                    {
                        using (RegistryKey clave = raiz.OpenSubKey(RutaWinlogonUsuario, true))
                        {
                            clave.SetValue("Shell", "explorer.exe");
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        negado = true;
                    }

                    Salida.Escribir(negado ? "BIEN" : "MAL", "escribir Shell queda negado a la cuenta");

                    // La cuenta original, al desinstalar: vuelve a mandar en su clave.
                    DesprotegerShell(raiz, RutaWinlogonUsuario, yo.User);
                    bool escribe = true;
                    try
                    {
                        using (RegistryKey clave = raiz.OpenSubKey(RutaWinlogonUsuario, true))
                        {
                            clave.SetValue("Shell", "explorer.exe");
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        escribe = false;
                    }

                    Salida.Escribir(escribe ? "BIEN" : "MAL", "al desproteger, la cuenta vuelve a poder escribir su clave");

                    // La cuenta original: la regla para soltar directivas, y que se quita sola.
                    Salida.Escribir(!PuedeSoltarDirectivas(raiz, yo.User) ? "BIEN" : "MAL", "antes de permitir, la cuenta no tiene regla propia para soltar");
                    PermitirSoltarDirectivas(raiz, yo.User);
                    Salida.Escribir(PuedeSoltarDirectivas(raiz, yo.User) ? "BIEN" : "MAL", "con la regla, puede escribir valores en las dos claves");
                    QuitarPermisoDirectivas(raiz, yo.User);
                    Salida.Escribir(!PuedeSoltarDirectivas(raiz, yo.User) ? "BIEN" : "MAL", "al desinstalar, la regla se va");

                    // Para poder borrar la rama: de vuelta el control a quien prueba.
                    using (RegistryKey clave = raiz.OpenSubKey(RutaWinlogonUsuario, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadPermissions))
                    {
                        RegistrySecurity abierta = new RegistrySecurity();
                        abierta.AddAccessRule(new RegistryAccessRule(yo.User, RegistryRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                        clave.SetAccessControl(abierta);
                    }
                }

                usuario.DeleteSubKeyTree(Rama, false);
                using (RegistryKey quedo = usuario.OpenSubKey(Rama))
                {
                    Salida.Escribir(quedo == null ? "BIEN" : "MAL", "la rama de prueba se borro");
                }
            }
        }
    }
}
