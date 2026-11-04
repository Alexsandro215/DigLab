using System;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Management;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace DigBit.Configurador
{
    /// <summary>La cuenta local estandar del laboratorio.</summary>
    internal static class Cuenta
    {
        public const string VarClave = "DIGBIT_CLAVE_CUENTA";
        private const string Administradores = "S-1-5-32-544";
        private const string Usuarios = "S-1-5-32-545";

        /// <summary>
        /// Por que no vale esa cuenta para el kiosco, o null si vale. La cuenta del kiosco
        /// NO puede ser administrador: tras guardar la bitacora, SesionEquipo lanza
        /// explorer.exe con el token de ESA cuenta y cada alumno se quedaria con un
        /// escritorio de administrador, en silencio, todo el semestre. Tampoco puede ser la
        /// cuenta con la que se instala, o te quedas sin escritorio a media instalacion.
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
                return "No se puede usar «" + cuenta + "»: es la cuenta con la que estás instalando. Usa una cuenta estándar aparte.";
            }

            try
            {
                using (PrincipalContext equipo = new PrincipalContext(ContextType.Machine))
                using (UserPrincipal usuario = UserPrincipal.FindByIdentity(equipo, IdentityType.SamAccountName, cuenta))
                using (GroupPrincipal grupo = GroupPrincipal.FindByIdentity(equipo, IdentityType.Sid, Administradores))
                {
                    if (usuario != null && grupo != null && usuario.IsMemberOf(grupo))
                    {
                        return "La cuenta «" + cuenta + "» es administrador. El kiosco le entrega el escritorio a cada estudiante con esa cuenta, así que todos tendrían permisos de administrador. Usa una cuenta estándar.";
                    }
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
