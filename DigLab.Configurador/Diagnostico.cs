using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

namespace DigLab.Configurador
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
            string exeDigLab = Path.Combine(destino, "DigLab.exe");
            Linea(File.Exists(exeDigLab), "DigLab está en " + destino);

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
                Linea(false, "Falta la conexión a la base: DigLab no podrá conectarse");
            }
            else
            {
                // Quien abra connections.config no debe ver la contrasena (CadenaCifrada).
                Linea(!Archivos.ConexionEnClaro(destino), "La contraseña de la base va cifrada para este equipo");
                try
                {
                    BaseDatos.Laboratorios(cadena);
                    Linea(true, "La conexión a la base funciona");
                }
                catch (Exception)
                {
                    Salida.Aviso("No se pudo conectar con la base ahora. DigLab se conectará cuando haya red.");
                }
            }

            string[] excluidas = Defender.Excluidas();
            if (excluidas == null)
            {
                Salida.Aviso("Microsoft Defender no responde. Si este equipo usa otro antivirus, excluye DigLab.exe en el suyo.");
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
            string config = Archivos.ConfigDigLab(destino);
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

            // Las memorias de emergencia (db/28): la copia de la base que deja DigLab.exe
            // --memorias-emergencia. Mientras no haya ninguna, vale la de una instalacion anterior.
            int enLaCopia = MemoriasEnLaCopia(Path.Combine(destino, "memorias_emergencia.txt"));
            if (enLaCopia > 0)
            {
                Linea(true, "Memorias de emergencia que abren este equipo sin servidor: " + enLaCopia);
            }
            else if (string.IsNullOrWhiteSpace(Archivos.LeerAjuste(config, "EmergenciaMemoria")))
            {
                Salida.Aviso("Sin memorias de emergencia: regístralas en DigLab (Laboratorios > Memorias de emergencia). Sin servidor y "
                    + "sin bitácora, solo se llega al escritorio con otra cuenta del equipo, si la hay.");
            }

            if (string.IsNullOrWhiteSpace(cuenta) || !Cuenta.Existe(cuenta))
            {
                Linea(false, "La cuenta del laboratorio no existe");
                return;
            }

            SecurityIdentifier sid = Cuenta.Sid(cuenta);
            bool original = Pasos.CuentaEsOriginal();
            bool sueltaDirectivas = original && string.Equals(Archivos.LeerAjuste(config, Pasos.AjusteLiberarDirectivas), "true", StringComparison.OrdinalIgnoreCase);
            Linea(true, "Cuenta del laboratorio: " + cuenta);
            Linea(Cuenta.Perfil(sid) != null, "Su perfil de Windows está creado");
            if (original)
            {
                AvisarDeLaCuentaOriginal(cuenta, config);
            }

            try
            {
                Kiosco.ConRegistroDeLaCuenta(sid, raiz =>
                {
                    foreach (Kiosco.ValorUsuario valor in Kiosco.ValoresUsuario(Path.Combine(destino, "DigLab.exe")))
                    {
                        // Puede estar suelto con razon: al actualizar desde el escritorio liberado.
                        // DigLab lo vuelve a poner al arrancar.
                        if (sueltaDirectivas && Kiosco.QueSeSueltan.Contains(valor.Nombre))
                        {
                            continue;
                        }

                        using (RegistryKey clave = raiz.OpenSubKey(valor.Clave))
                        {
                            object leido = clave == null ? null : clave.GetValue(valor.Nombre);
                            Linea(Equals(Convert.ToString(leido, CultureInfo.InvariantCulture), Convert.ToString(valor.Valor, CultureInfo.InvariantCulture)), valor.Que);
                        }
                    }

                    Linea(Kiosco.PermisoDeMas(raiz, Kiosco.ClaveShell, sid) == null, "El shell de la cuenta está protegido");
                    if (sueltaDirectivas)
                    {
                        if (Kiosco.PuedeSoltarDirectivas(raiz, sid))
                        {
                            Linea(true, "Tras la bitácora, el escritorio queda sin límites");
                        }
                        else
                        {
                            Salida.Aviso("DigLab no podrá quitar los límites del escritorio tras la bitácora: seguirán sin Administrador de tareas, Win+R ni Panel de control.");
                        }
                    }
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
                if (original && winlogon != null && string.IsNullOrEmpty(winlogon.GetValue("DefaultPassword") as string))
                {
                    // La cuenta original puede no tener contrasena, y entra sola igual.
                    Salida.Aviso("La cuenta " + cuenta + " no tiene contraseña: entra sola sin guardar ninguna.");
                }
                else
                {
                    Linea(winlogon != null && !string.IsNullOrEmpty(winlogon.GetValue("DefaultPassword") as string), "Su contraseña está guardada para entrar sola");
                }

                Linea(winlogon != null && (winlogon.GetValue("ForceAutoLogon") as string) == "1", "Vuelve a entrar sola al cerrar la sesión");
            }

            Linea(Servicio.Corriendo(), "El vigilante está corriendo");
            Linea(Servicio.EnModoSeguro(), "El vigilante también arranca en modo seguro");
        }

        /// <summary>Lo que hay que saber de esta decision, para que no sorprenda a quien instala.</summary>
        private static void AvisarDeLaCuentaOriginal(string cuenta, string config)
        {
            try
            {
                if (Cuenta.EsAdministradora(cuenta))
                {
                    Salida.Aviso("«" + cuenta + "» es administradora: tras su bitácora, quien use el equipo podría instalar programas o desactivar el kiosco.");
                }

                if (Cuenta.ContrasenaCaduca(cuenta))
                {
                    Salida.Aviso("La contraseña de «" + cuenta + "» caduca: el día que Windows pida cambiarla, el equipo dejará de entrar solo.");
                }
            }
            catch (Exception ex)
            {
                Salida.Aviso("No se pudo revisar la cuenta «" + cuenta + "»: " + ex.Message);
            }
        }

        /// <summary>Cuantas memorias trae la copia (renglones que no son comentario); 0 si no hay copia.</summary>
        private static int MemoriasEnLaCopia(string ruta)
        {
            try
            {
                return File.Exists(ruta) ? File.ReadAllLines(ruta).Count(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)) : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
