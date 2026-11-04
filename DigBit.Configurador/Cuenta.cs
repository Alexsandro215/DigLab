using System;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace DigBit.Configurador
{
    /// <summary>
    /// La cuenta del kiosco: una nueva, local y estandar, o la que el equipo ya tenia. Esa la
    /// pidio el usuario (2026-11-04): los programas y los permisos de los alumnos estan en ella
    /// y los equipos se reinstalan cada semestre, asi que acepta que tras la bitacora tengan un
    /// escritorio de administrador. De ella no se cambia nada y nunca se ofrece borrarla.
    /// </summary>
    internal static class Cuenta
    {
        public const string VarClave = "DIGBIT_CLAVE_CUENTA";
        private const string Administradores = "S-1-5-32-544";
        private const string Usuarios = "S-1-5-32-545";

        // Los grupos de un inicio de sesion con cuenta Microsoft.
        private const string PrefijoCuentaMicrosoft = "S-1-11-96-";

        private const int InicioInteractivo = 2;
        private const int ProveedorPorOmision = 0;
        private const int ErrorContrasena = 1326;
        private const int ErrorRestriccion = 1327;
        private const int ErrorSinPermisoDeEntrar = 1385;
        private const int ErrorDeshabilitada = 1331;
        private const int ErrorCaducada = 1330;
        private const int ErrorDebeCambiarla = 1907;
        private const int ErrorBloqueada = 1909;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LogonUserW(string usuario, string dominio, string contrasena, int tipo, int proveedor, out IntPtr token);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// Por que no vale la cuenta original, o null si vale. Sin la contrasena correcta el
        /// inicio automatico falla en cada arranque y el equipo se queda en la pantalla de
        /// Windows, por eso se comprueba contra Windows antes de tocar nada.
        /// </summary>
        public static string PorQueNoValeOriginal(string cuenta, string contrasena)
        {
            if (string.IsNullOrWhiteSpace(cuenta))
            {
                return "Falta el nombre de la cuenta.";
            }

            if (EsDeMicrosoft(cuenta))
            {
                return "«" + cuenta + "» inicia sesión con una cuenta de Microsoft, y DigBit no puede dejarla entrando sola. Elige una cuenta nueva o usa una cuenta local.";
            }

            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal usuario = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
            {
                if (usuario == null)
                {
                    return "«" + cuenta + "» no es una cuenta local de este equipo.";
                }

                if (usuario.Enabled == false)
                {
                    return "La cuenta «" + cuenta + "» está deshabilitada en Windows.";
                }
            }

            return PorQueNoEntra(cuenta, contrasena ?? "");
        }

        /// <summary>Si Windows la deja entrar con esa contrasena; no abre ninguna sesion ni carga su perfil.</summary>
        private static string PorQueNoEntra(string cuenta, string contrasena)
        {
            IntPtr token;
            if (LogonUserW(cuenta, Environment.MachineName, contrasena, InicioInteractivo, ProveedorPorOmision, out token))
            {
                CloseHandle(token);
                return null;
            }

            int codigo = Marshal.GetLastWin32Error();
            switch (codigo)
            {
                case ErrorContrasena:
                    return contrasena.Length == 0
                        ? "La cuenta «" + cuenta + "» sí tiene contraseña: escríbela para que pueda entrar sola."
                        : "Esa no es la contraseña de «" + cuenta + "». Distingue mayúsculas y minúsculas.";
                case ErrorRestriccion:
                    // Windows no deja probar una contrasena vacia por aqui, pero solo responde esto
                    // si de verdad lo es. Entrar solo en la consola con ella si esta permitido.
                    return contrasena.Length == 0 ? null : "Windows no deja entrar a «" + cuenta + "» ahora (restricción de la cuenta).";
                case ErrorSinPermisoDeEntrar:
                    return "La cuenta «" + cuenta + "» no tiene permitido iniciar sesión en este equipo.";
                case ErrorDeshabilitada:
                    return "La cuenta «" + cuenta + "» está deshabilitada en Windows.";
                case ErrorCaducada:
                case ErrorDebeCambiarla:
                    return "La contraseña de «" + cuenta + "» caducó o hay que cambiarla: cámbiala en Windows y vuelve a instalar.";
                case ErrorBloqueada:
                    return "La cuenta «" + cuenta + "» está bloqueada por demasiados intentos.";
                default:
                    return "Windows no dejó comprobar la contraseña de «" + cuenta + "» (código " + codigo + ").";
            }
        }

        /// <summary>Solo se puede saber de la cuenta con la que se instala, que es el caso normal.</summary>
        private static bool EsDeMicrosoft(string cuenta)
        {
            if (!string.Equals(cuenta, Environment.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using (WindowsIdentity yo = WindowsIdentity.GetCurrent())
            {
                return yo.Groups != null && yo.Groups.Any(g => g.Value.StartsWith(PrefijoCuentaMicrosoft, StringComparison.Ordinal));
            }
        }

        public static bool EsAdministradora(string cuenta)
        {
            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal usuario = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
            using (GroupPrincipal grupo = GroupPrincipal.FindByIdentity(equipo, IdentityType.Sid, Administradores))
            {
                return usuario != null && grupo != null && usuario.IsMemberOf(grupo);
            }
        }

        /// <summary>Si Windows le pedira cambiar la contrasena algun dia: ese dia ya no entraria sola.</summary>
        public static bool ContrasenaCaduca(string cuenta)
        {
            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal usuario = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
            {
                return usuario != null && !usuario.PasswordNeverExpires;
            }
        }

        /// <summary>
        /// Por que no vale esa cuenta como cuenta aparte del kiosco, o null si vale. NO puede ser
        /// administrador: tras guardar la bitacora, SesionEquipo lanza explorer.exe con el token de
        /// ESA cuenta y cada alumno se quedaria con un escritorio de administrador, en silencio,
        /// todo el semestre; quien lo quiere asi elige la cuenta original, a sabiendas. Tampoco
        /// puede ser la cuenta con la que se instala, porque este camino le cambia la contrasena.
        /// </summary>
        public static string PorQueNoVale(string cuenta)
        {
            if (string.IsNullOrWhiteSpace(cuenta))
            {
                return "Falta el nombre de la cuenta del laboratorio.";
            }

            if (cuenta.IndexOfAny(new[] { '\\', '/', '@', '"' }) >= 0)
            {
                return "«" + cuenta + "» tiene que ser una cuenta LOCAL, sin dominio.";
            }

            if (string.Equals(cuenta, Environment.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return "«" + cuenta + "» es la cuenta con la que estás instalando. Para usarla, elige «La que ya tiene el equipo».";
            }

            try
            {
                if (EsAdministradora(cuenta))
                {
                    return "La cuenta «" + cuenta + "» es administradora y aquí se le cambiaría la contraseña. Para usarla sin cambiarla, elige «La que ya tiene el equipo» desde esa cuenta, o escribe otro nombre para crear una cuenta nueva.";
                }
            }
            catch (Exception ex)
            {
                // El grupo puede tener SIDs huerfanos y la consulta se queja. No es motivo
                // para bloquear la instalacion, pero si para avisar.
                Salida.Aviso("No se pudo comprobar si «" + cuenta + "» es administrador (" + ex.Message + "). Asegúrate de que NO lo sea.");
            }

            return null;
        }

        public static bool Existe(string cuenta)
        {
            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal usuario = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
            {
                return usuario != null;
            }
        }

        public static SecurityIdentifier Sid(string cuenta)
        {
            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal usuario = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
            {
                if (usuario == null)
                {
                    throw new FalloDeInstalacion("La cuenta «" + cuenta + "» no existe.");
                }

                return usuario.Sid;
            }
        }

        /// <summary>La carpeta de su perfil de Windows, o null si todavia no tiene.</summary>
        public static string Perfil(SecurityIdentifier sid)
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey clave = equipo.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid.Value))
            {
                return clave == null ? null : clave.GetValue("ProfileImagePath") as string;
            }
        }

        private static UserPrincipal NuevaSinGuardar(PrincipalContext equipo, string cuenta)
        {
            return new UserPrincipal(equipo)
            {
                SamAccountName = cuenta,
                Description = "Cuenta de laboratorio DigBit",
                Enabled = true
            };
        }

        /// <summary>Solo desarrollo: arma una cuenta en memoria, sin guardarla, para ver que los ajustes valen.</summary>
        public static void ProbarSinGuardar()
        {
            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal usuario = NuevaSinGuardar(equipo, "digbit_prueba_sin_guardar"))
            {
                usuario.SetPassword("Prueba-Sin-Guardar-2026");
                usuario.PasswordNeverExpires = true;
                Salida.Escribir("BIEN", "la cuenta se arma en memoria; existe de verdad: " + Existe("digbit_prueba_sin_guardar"));
            }

            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (GroupPrincipal usuarios = GroupPrincipal.FindByIdentity(equipo, IdentityType.Sid, Usuarios))
            using (GroupPrincipal admins = GroupPrincipal.FindByIdentity(equipo, IdentityType.Sid, Administradores))
            {
                Salida.Escribir(usuarios != null ? "BIEN" : "MAL", "encuentra el grupo Usuarios por su SID: " + (usuarios == null ? "" : usuarios.Name));
                Salida.Escribir(admins != null ? "BIEN" : "MAL", "encuentra el grupo Administradores por su SID: " + (admins == null ? "" : admins.Name));
            }

            // La comprobacion de la contrasena, con una cuenta que no existe: nunca con la de verdad.
            string respuesta = PorQueNoEntra("digbit_prueba_no_existe", "contrasena-falsa-123");
            Salida.Escribir(respuesta != null && respuesta.StartsWith("Esa no es la contraseña", StringComparison.Ordinal) ? "BIEN" : "MAL", "LogonUser responde: " + respuesta);
            Salida.Escribir("INFO", "quien instala tiene cuenta Microsoft: " + EsDeMicrosoft(Environment.UserName));
            Salida.Escribir(PorQueNoValeOriginal("digbit_prueba_no_existe", "x") != null ? "BIEN" : "MAL", "una cuenta que no existe no vale como original");
            Salida.Escribir(PorQueNoValeOriginal(" ", "x") != null ? "BIEN" : "MAL", "sin nombre no vale");
        }

        /// <summary>La crea si no existe (sin privilegios, contrasena sin caducidad), o le fija la contrasena.</summary>
        public static void CrearOActualizar(string cuenta, string contrasena)
        {
            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal existente = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
            {
                UserPrincipal usuario = existente ?? NuevaSinGuardar(equipo, cuenta);
                try
                {
                    usuario.SetPassword(contrasena);
                    usuario.PasswordNeverExpires = true;
                    usuario.Enabled = true;
                    usuario.Save();
                    // Despues de guardarla: en una cuenta sin guardar este ajuste no siempre vale.
                    usuario.UserCannotChangePassword = true;
                    usuario.Save();
                }
                catch (PasswordException)
                {
                    throw new FalloDeInstalacion("Windows rechazó la contraseña de la cuenta «" + cuenta + "»: no cumple las reglas de este equipo (largo o complejidad). Vuelve a instalar con otra.");
                }

                using (GroupPrincipal grupo = GroupPrincipal.FindByIdentity(equipo, IdentityType.Sid, Usuarios))
                {
                    if (grupo != null && !usuario.IsMemberOf(grupo))
                    {
                        grupo.Members.Add(usuario);
                        grupo.Save();
                    }
                }

                if (existente == null)
                {
                    usuario.Dispose();
                }
            }
        }

        /// <summary>
        /// Crea su perfil de Windows con un inicio de sesion silencioso, si aun no lo
        /// tiene: sin perfil no hay registro de la cuenta donde escribir el shell.
        /// </summary>
        public static void CrearPerfil(string cuenta, string contrasena)
        {
            SecurityIdentifier sid = Sid(cuenta);
            if (Perfil(sid) != null)
            {
                return;
            }

            using (ServiceController inicioSecundario = new ServiceController("seclogon"))
            {
                if (inicioSecundario.Status != ServiceControllerStatus.Running)
                {
                    inicioSecundario.Start();
                    inicioSecundario.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                }
            }

            using (SecureString segura = new SecureString())
            {
                foreach (char c in contrasena)
                {
                    segura.AppendChar(c);
                }

                ProcessStartInfo inicio = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    LoadUserProfile = true,
                    UserName = cuenta,
                    Domain = Environment.MachineName,
                    Password = segura,
                    WorkingDirectory = Environment.SystemDirectory
                };
                using (Process proceso = Process.Start(inicio))
                {
                    proceso.WaitForExit(60000);
                }
            }

            if (Perfil(sid) == null)
            {
                throw new FalloDeInstalacion("No se pudo crear el perfil de «" + cuenta + "». Inicia sesión con esa cuenta una vez y vuelve a instalar.");
            }
        }

        /// <summary>Borra la cuenta y su perfil. Solo si se pide al desinstalar.</summary>
        public static void Borrar(string cuenta)
        {
            SecurityIdentifier sid = Sid(cuenta);
            using (ManagementObjectSearcher buscador = new ManagementObjectSearcher("SELECT * FROM Win32_UserProfile WHERE SID='" + sid.Value + "'"))
            using (ManagementObjectCollection perfiles = buscador.Get())
            {
                foreach (ManagementBaseObject objeto in perfiles)
                {
                    using (ManagementObject perfil = (ManagementObject)objeto)
                    {
                        perfil.Delete();
                    }
                }
            }

            using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal usuario = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
            {
                if (usuario != null)
                {
                    usuario.Delete();
                }
            }
        }
    }
}
