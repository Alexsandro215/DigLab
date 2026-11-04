using System;
using System.Configuration;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace DigBit.Infraestructura
{
    /// <summary>
    /// Kiosco en la cuenta que el equipo ya tenia (ajuste LiberarDirectivas, lo escribe el
    /// instalador): esa cuenta es la de todos los dias, asi que quien ya registro su bitacora
    /// recibe su escritorio normal. Las directivas viven en el registro de la cuenta y valen
    /// para toda la sesion, por eso se sueltan justo antes de lanzar el escritorio y se vuelven
    /// a poner en cuanto DigBit arranca, que es lo primero que corre al entrar la cuenta (es su
    /// shell). Si un corte de luz las dejo sueltas, el siguiente arranque las pone.
    ///
    /// Solo estas tres, las que estorban para trabajar. El resto se queda siempre: sin bloquear
    /// el equipo ni cambiar la contrasena, porque cualquiera de las dos dejaria al equipo sin
    /// poder entrar solo. Deben ser las mismas que Kiosco.QueSeSueltan del configurador, que es
    /// quien le da a la cuenta el permiso de escribir estos valores.
    ///
    /// En un kiosco con cuenta aparte el ajuste no esta: aquello sigue igual, limitado.
    /// </summary>
    internal static class DirectivasKiosco
    {
        private const string ClaveAjuste = "LiberarDirectivas";
        private const string RamaSistema = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string RamaExplorador = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        private static bool Aplica
        {
            get
            {
                try
                {
                    return Kiosco.Activo && Kiosco.EsVerdadero(ConfigurationManager.AppSettings[ClaveAjuste]);
                }
                catch (ConfigurationErrorsException)
                {
                    return false;
                }
            }
        }

        /// <summary>Al arrancar: la pantalla de DigBit no se puede saltar.</summary>
        public static void Bloquear()
        {
            Cambiar(true);
        }

        /// <summary>Al entregar el escritorio: antes de lanzarlo, que lee las suyas al arrancar.</summary>
        public static void Soltar()
        {
            Cambiar(false);
        }

        private static void Cambiar(bool bloquear)
        {
            if (!Aplica)
            {
                return;
            }

            try
            {
                using (RegistryKey usuario = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    Poner(usuario, bloquear);
                }

                Log.Info(bloquear ? "Directivas del kiosco puestas." : "Directivas del kiosco sueltas: el escritorio queda sin limites.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException || ex is IOException)
            {
                // Sin soltar, el escritorio sale limitado; sin poner, quedan como estaban. Ninguna
                // de las dos es motivo para que el laboratorio se quede sin DigBit.
                Log.Error(bloquear ? "No se pudieron poner las directivas del kiosco" : "No se pudieron soltar las directivas del kiosco", ex);
            }
        }

        /// <summary>Sobre la raiz que le den, para poder probarlo sin tocar el registro de nadie.</summary>
        internal static void Poner(RegistryKey raiz, bool bloquear)
        {
            Valor(raiz, RamaSistema, "DisableTaskMgr", bloquear);
            Valor(raiz, RamaExplorador, "NoRun", bloquear);
            Valor(raiz, RamaExplorador, "NoControlPanel", bloquear);
        }

        // Pidiendo solo lo que el instalador le dio: abrirla "para escribir" pide tambien crear
        // subclaves, y eso la cuenta no lo tiene.
        private static void Valor(RegistryKey raiz, string rama, string nombre, bool bloquear)
        {
            using (RegistryKey clave = raiz.OpenSubKey(rama, RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.QueryValues | System.Security.AccessControl.RegistryRights.SetValue))
            {
                if (clave == null)
                {
                    throw new IOException("No existe la clave " + rama + ".");
                }

                if (bloquear)
                {
                    clave.SetValue(nombre, 1, RegistryValueKind.DWord);
                }
                else
                {
                    clave.DeleteValue(nombre, false);
                }
            }
        }
    }
}
