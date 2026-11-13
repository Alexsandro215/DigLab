using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DigLab.conexion;
using MySql.Data.MySqlClient;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// Actualizar los equipos desde la base (db/29; pedido del 2026-11-13: "avisa que hay
    /// una nueva actualizacion y se actualiza sola al aceptar"):
    ///   - En los equipos de administracion y de docentes, al abrir DigLab (con servidor)
    ///     se ofrece la version nueva; si se acepta, se descarga, se comprueba su huella y
    ///     se corre el instalador en modo silencioso (Windows pide permiso de
    ///     administrador); DigLab se cierra y el instalador lo vuelve a abrir (/ABRIRDIGLAB).
    ///   - En los del laboratorio (kiosco) no pregunta nadie: el servicio DigLab.Vigilante
    ///     lanza "DigLab.exe --buscar-actualizacion" como SYSTEM, que la descarga a
    ///     C:\DigLab\Actualizacion (ahi un estudiante no puede escribir), y la instala el
    ///     vigilante cuando nadie usa el equipo.
    /// </summary>
    internal static class Actualizador
    {
        /// <summary>DigLab.exe --buscar-actualizacion: ver <see cref="BuscarParaElEquipo"/>.</summary>
        public const string ArgumentoBuscar = "--buscar-actualizacion";

        /// <summary>Para el instalador: al terminar en silencio, volver a abrir DigLab (instalador/DigLab.iss).</summary>
        public const string ArgumentoAbrir = "/ABRIRDIGLAB";

        /// <summary>Codigo de salida de --buscar-actualizacion cuando hay una version descargada y lista.</summary>
        public const int CodigoLista = 10;

        /// <summary>
        /// El aviso del kiosco al vigilante ("Actualizar a la ..." del inicio de sesion): un
        /// archivo en %ProgramData%\DigLab, donde la cuenta del kiosco puede escribir. El
        /// vigilante lo mira cada 10 segundos y busca e instala la version en ese momento.
        /// Crearlo no le da nada a nadie: solo adelanta la que publico la administracion.
        /// </summary>
        public const string ArchivoPedido = "actualizar_ahora.txt";

        /// <summary>
        /// Si la administracion mando "actualizar ya" a los equipos del laboratorio de este
        /// kiosco (ordenes_equipos, db/29). Lanza si no hay servidor.
        /// </summary>
        public static bool HayOrdenParaEsteEquipo()
        {
            if (!LaboratorioEquipo.Resuelto)
            {
                return false;
            }

            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                return ActualizacionesDatos.HayOrdenDeActualizar(conexion, LaboratorioEquipo.Id);
            }
        }

        /// <summary>Pide al vigilante que instale ya la version nueva. False con el motivo si no se pudo.</summary>
        public static bool PedirInstalarAhora(VersionPublicada version, out string error)
        {
            try
            {
                Directory.CreateDirectory(SesionActiva.Carpeta);
                File.WriteAllText(Path.Combine(SesionActiva.Carpeta, ArchivoPedido),
                    version.Version + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                Log.Info("Desde el kiosco se pidio instalar ya DigLab " + version.Version + ".");
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Pedir instalar DigLab " + version.Version, ex);
                error = ex.Message;
                return false;
            }
        }

        /// <summary>La version publicada mas nueva que la instalada, o null. Lanza si no hay servidor.</summary>
        public static VersionPublicada BuscarNueva()
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                VersionPublicada ultima = ActualizacionesDatos.UltimaVigente(conexion);
                return ultima != null && ultima.Numero > ActualizacionesDatos.VersionInstalada ? ultima : null;
            }
        }

        /// <summary>
        /// Al arrancar, con servidor, fuera del kiosco: si hay version nueva, la ofrece. True si
        /// se lanzo el instalador: DigLab tiene que cerrarse para que pueda reemplazarlo.
        /// </summary>
        public static bool OfrecerSiHay()
        {
            if (Kiosco.Activo)
            {
                return false;
            }

            VersionPublicada nueva;
            try
            {
                nueva = BuscarNueva();
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo buscar una version nueva de DigLab: " + ex.Message);
                return false;
            }

            if (nueva == null)
            {
                return false;
            }

            Log.Info("Hay una version nueva de DigLab: " + nueva.Version + " (este equipo tiene " + ActualizacionesDatos.VersionInstalada + ").");
            string ruta;
            using (DialogoVersionNueva dialogo = new DialogoVersionNueva(nueva, Path.Combine(Path.GetTempPath(), "DigLab-Setup-" + nueva.Version + ".exe")))
            {
                if (dialogo.ShowDialog() != DialogResult.OK)
                {
                    Log.Info("La version " + nueva.Version + " se deja para despues.");
                    return false;
                }

                ruta = dialogo.Ruta;
            }

            string registro = Path.Combine(Path.GetTempPath(), "DigLab-actualizacion-" + nueva.Version + ".log");
            try
            {
                // Sin "runas": el instalador pide el permiso el mismo y asi sabe quien lo abrio, para
                // volver a abrir DigLab como esa persona y no como administrador.
                Process.Start(new ProcessStartInfo(ruta,
                    "/SILENT /SUPPRESSMSGBOXES /NORESTART " + ArgumentoAbrir + " /LOG=\"" + registro + "\"")
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(ruta) ?? ""
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Aviso("Actualizar DigLab: no se dio el permiso de administrador.");
                MessageBox.Show("No se actualizó: Windows necesita el permiso de administrador. Vuelve a abrir DigLab para intentarlo otra vez.",
                    "DigLab", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Lanzar el instalador de DigLab " + nueva.Version, ex);
                MessageBox.Show("No se pudo abrir el instalador de la versión nueva. DigLab sigue con la que tiene.",
                    "DigLab", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Log.Info("Instalador de DigLab " + nueva.Version + " lanzado; DigLab se cierra para que pueda actualizarse.");
            return true;
        }

        /// <summary>
        /// DigLab.exe --buscar-actualizacion (lo lanza el servicio DigLab.Vigilante como
        /// SYSTEM): si hay una version nueva, la deja descargada y comprobada en
        /// C:\DigLab\Actualizacion. Codigo de salida: 0 al dia; 10 lista (detalle = "LISTA
        /// version huella ruta"); 1 sin servidor; 2 no se pudo descargar; 3 la base no las
        /// tiene (db/29). Instalarla le toca al vigilante, que sabe si alguien usa el equipo.
        /// </summary>
        public static int BuscarParaElEquipo(out string detalle)
        {
            VersionPublicada nueva;
            try
            {
                using (MySqlConnection conexion = new Conexion().GetConexion())
                {
                    if (!Esquema.Actualizaciones(conexion))
                    {
                        detalle = "La base no tiene las versiones de DigLab (falta db/29, o esta cuenta no puede leerlas).";
                        return 3;
                    }

                    VersionPublicada ultima = ActualizacionesDatos.UltimaVigente(conexion);
                    if (ultima == null || ultima.Numero <= ActualizacionesDatos.VersionInstalada)
                    {
                        // Sin acentos: va al log del vigilante, que es ASCII.
                        detalle = "Al dia con " + ActualizacionesDatos.Mostrar(ActualizacionesDatos.VersionInstalada)
                            + (ultima == null ? " (no hay ninguna publicada)." : ".");
                        return 0;
                    }

                    nueva = ultima;
                }
            }
            catch (Exception ex)
            {
                detalle = "No se pudo leer la base: " + ex.Message;
                return 1;
            }

            // Junto a DigLab.exe: C:\DigLab hereda permisos en los que un estudiante solo lee.
            string carpeta = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Actualizacion");
            string ruta = Path.Combine(carpeta, "DigLab-Setup-" + nueva.Version + ".exe");
            try
            {
                Directory.CreateDirectory(carpeta);
                bool yaEsta = File.Exists(ruta) && new FileInfo(ruta).Length == nueva.Tamano
                    && ActualizacionesDatos.HuellaDe(ruta) == nueva.Huella;
                if (!yaEsta)
                {
                    foreach (string viejo in Directory.GetFiles(carpeta))
                    {
                        File.Delete(viejo);
                    }

                    ActualizacionesDatos.Descargar(nueva, ruta, null);
                }
            }
            catch (Exception ex)
            {
                detalle = "No se pudo descargar DigLab " + ActualizacionesDatos.Mostrar(nueva.Numero) + ": " + ex.Message;
                return 2;
            }

            detalle = "LISTA " + nueva.Version + " " + nueva.Huella + " " + ruta;
            return CodigoLista;
        }
    }
}
