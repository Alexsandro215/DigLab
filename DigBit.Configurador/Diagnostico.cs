using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

namespace DigBit.Configurador
{
    /// <summary>
    /// Al terminar: si el equipo quedo bien, en palabras llanas. Solo lee. Cada renglon es
    /// BIEN, MAL o AVISO y el Setup los ensena; con algun MAL, dice que hay que revisarlo.
    /// </summary>
    internal static class Diagnostico
    {
        private static int mal;

        private static void Linea(bool bien, string texto)
        {
            Salida.Escribir(bien ? "BIEN" : "MAL", texto);
            if (!bien)
            {
                mal++;
            }
        }

        /// <summary>Devuelve cuantas cosas salieron mal.</summary>
        public static int Revisar(string destino, string modo, string cuenta)
        {
            mal = 0;
            bool kiosco = Pasos.EsKiosco(modo);
            string exeDigBit = Path.Combine(destino, "DigBit.exe");
            Linea(File.Exists(exeDigBit), "DigBit está en " + destino);

            string cadena = null;
            try
            {
                cadena = Archivos.LeerConexion(destino);
            }
            catch (Exception)
            {
                Linea(false, "El archivo de conexión no se puede leer");
            }

            if (cadena == null)
            {
                Linea(false, "Falta la conexión a la base: DigBit no podrá conectarse");
            }
            else
            {
                try
                {
                    BaseDatos.Laboratorios(cadena);
                    Linea(true, "La conexión a la base funciona");
                }
                catch (Exception)
                {
                    Salida.Aviso("No se pudo conectar con la base ahora. DigBit se conectará cuando haya red.");
                }
            }

            string[] excluidas = Defender.Excluidas();
            if (excluidas == null)
            {
                Salida.Aviso("Microsoft Defender no responde. Si este equipo usa otro antivirus, excluye DigBit.exe en el suyo.");
            }
            else
            {
                foreach (string archivo in Defender.Archivos(destino, kiosco))
                {
                    Linea(excluidas.Contains(archivo, StringComparer.OrdinalIgnoreCase), Path.GetFileName(archivo) + " excluido de Defender");
                }
            }

            if (kiosco)
            {
                RevisarKiosco(destino, cuenta, cadena);
            }

            return mal;
        }

        private static void RevisarKiosco(string destino, string cuenta, string cadena)
        {
            string config = Archivos.ConfigDigBit(destino);
            Linea(string.Equals(Archivos.LeerAjuste(config, "ModoKiosco"), "true", StringComparison.OrdinalIgnoreCase), "Modo kiosco encendido");
            string laboratorio = Archivos.LeerAjuste(config, "Laboratorio");
            Linea(!string.IsNullOrWhiteSpace(laboratorio), "Laboratorio del equipo: " + laboratorio);
            if (cadena != null && !string.IsNullOrWhiteSpace(laboratorio))
            {
                try
                {
                    Linea(BaseDatos.Laboratorios(cadena).Any(n => string.Equals(n.Trim(), laboratorio.Trim(), StringComparison.OrdinalIgnoreCase)), "Ese laboratorio existe en la base");
                }
                catch (Exception)
                {
                    // Ya se aviso arriba de que no hay base.
                }
            }

            string maquina = Archivos.LeerAjuste(config, "NumeroMaquina");
            if (string.IsNullOrWhiteSpace(maquina))
            {
                Salida.Aviso("Sin número de máquina: en las bitácoras saldrá el nombre de Windows (" + Environment.MachineName + ").");
            }

            if (string.IsNullOrWhiteSpace(cuenta) || !Cuenta.Existe(cuenta))
            {
                Linea(false, "La cuenta del laboratorio no existe");
                return;
            }

            SecurityIdentifier sid = Cuenta.Sid(cuenta);
            Linea(true, "Cuenta del laboratorio: " + cuenta);
            Linea(Cuenta.Perfil(sid) != null, "Su perfil de Windows está creado");
            try
            {
                Kiosco.ConRegistroDeLaCuenta(sid, raiz =>
                {
                    foreach (Kiosco.ValorUsuario valor in Kiosco.ValoresUsuario(Path.Combine(destino, "DigBit.exe")))
                    {
                        using (RegistryKey clave = raiz.OpenSubKey(valor.Clave))
                        {
                            object leido = clave == null ? null : clave.GetValue(valor.Nombre);
                            Linea(Equals(Convert.ToString(leido, CultureInfo.InvariantCulture), Convert.ToString(valor.Valor, CultureInfo.InvariantCulture)), valor.Que);
                        }
                    }

                    Linea(Kiosco.PermisoDeMas(raiz, @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", sid) == null, "La cuenta no puede cambiar su propio shell");
                });
            }
            catch (Exception ex)
            {
                Salida.Aviso("No se pudo leer el registro de la cuenta: " + ex.Message);
            }

            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey winlogon = equipo.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
            {
                Linea(winlogon != null && (winlogon.GetValue("AutoAdminLogon") as string) == "1", "Inicio de sesión automático encendido");
                Linea(winlogon != null && string.Equals(winlogon.GetValue("DefaultUserName") as string, cuenta, StringComparison.OrdinalIgnoreCase), "Entra sola la cuenta " + cuenta);
                Linea(winlogon != null && !string.IsNullOrEmpty(winlogon.GetValue("DefaultPassword") as string), "Su contraseña está guardada para entrar sola");
                Linea(winlogon != null && (winlogon.GetValue("ForceAutoLogon") as string) == "1", "Vuelve a entrar sola al cerrar la sesión");
            }

            Linea(Servicio.Corriendo(), "El vigilante está corriendo");
            Linea(Servicio.EnModoSeguro(), "El vigilante también arranca en modo seguro");
        }
    }
}
