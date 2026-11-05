using System;
using DigLab.conexion;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// Modo sin conexion (fase 4). Cuando el servidor no responde y hay una copia
    /// local del horario de este laboratorio, DigLab sigue atendiendo alumnos con
    /// esa copia: entran solo con su matricula, el codigo se resuelve con la
    /// copia y el reloj estimado, y la bitacora se guarda en una cola que se sube
    /// cuando vuelve la conexion. Profesores y administradores no pueden entrar
    /// sin servidor.
    /// </summary>
    internal static class SinConexion
    {
        /// <summary>Copia mas vieja que esto no se usa: el horario puede haber cambiado demasiado.</summary>
        public const int EdadMaximaDias = 30;

        public static bool Activo { get; private set; }
        public static DateTime Desde { get; private set; }
        public static string Motivo { get; private set; }
        public static CacheHorario Cache { get; private set; }

        /// <summary>
        /// Intenta entrar en modo sin conexion con la copia local. Devuelve false
        /// (y lo deja en el log) si no hay copia, es de otro laboratorio o es
        /// demasiado vieja.
        /// </summary>
        public static bool IntentarActivar(string motivo)
        {
            CacheHorario cache = CacheHorario.Cargar();
            if (cache == null)
            {
                Log.Aviso("Sin servidor y sin copia local del horario: no se puede trabajar sin conexion.");
                return false;
            }

            string configurado = LaboratorioEquipo.NombreConfigurado;
            if (string.IsNullOrEmpty(configurado) || !string.Equals(configurado, cache.Laboratorio, StringComparison.OrdinalIgnoreCase))
            {
                Log.Aviso("La copia local es de '" + cache.Laboratorio + "' y este equipo esta configurado como '" + (configurado ?? "(sin laboratorio)") + "': no se usa.");
                return false;
            }

            if (cache.Edad > TimeSpan.FromDays(EdadMaximaDias))
            {
                Log.Aviso("La copia local del horario tiene " + (int)cache.Edad.TotalDays + " dias; se considera demasiado vieja.");
                return false;
            }

            Cache = cache;
            Activo = true;
            Desde = DateTime.Now;
            Motivo = motivo;
            LaboratorioEquipo.UsarCopiaLocal(cache.LaboratorioId, cache.Laboratorio);
            Log.Aviso("MODO SIN CONEXION activado con la copia del horario del " + cache.Generado.ToString("dd/MM/yyyy HH:mm")
                + " (" + ColaBitacoras.Pendientes() + " bitacoras pendientes de enviar; acceso " + ModosDeAcceso.Valor(cache.Modo)
                + "). Motivo: " + PrimeraLinea(motivo));
            return true;
        }

        public static void Desactivar()
        {
            if (!Activo)
            {
                return;
            }

            Activo = false;
            Log.Info("Conexion con el servidor recuperada; se sale del modo sin conexion (estuvo activo desde " + Desde.ToString("HH:mm") + ").");
        }

        /// <summary>
        /// Con servidor a la vista: avisa al enviador (la cola la sube el, en su
        /// hilo) y refresca la copia local. Nunca lanza; lo que falle queda en el
        /// log y se reintenta en la siguiente.
        /// </summary>
        public static void AlConectar(int idLaboratorio)
        {
            EnviadorBitacoras.Despertar();
            try
            {
                if (idLaboratorio > 0)
                {
                    Cache = CacheHorario.Actualizar(idLaboratorio);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Actualizar la copia local del horario", ex);
            }
        }

        /// <summary>La copia que acaba de leer la vigilancia de conexion, en segundo plano.</summary>
        public static void UsarCopia(CacheHorario copia)
        {
            if (copia != null)
            {
                Cache = copia;
            }
        }

        /// <summary>
        /// Texto para la franja de la pantalla de inicio: el modo sin conexion y,
        /// tambien con conexion, las bitacoras de este equipo que aun no llegaron
        /// (antes solo se veian sin conexion, y una atascada no la veia nadie).
        /// Vacio si no hay nada que decir. Lee el estado del enviador, que esta en
        /// memoria: no el archivo cada 5 s.
        /// </summary>
        public static string TextoAviso()
        {
            EstadoCola cola = EnviadorBitacoras.Estado;
            string pendientes = cola.Pendientes == 0 ? ""
                : " Bitácoras de este equipo por enviar: " + cola.Pendientes + " (se envían solas).";
            string apartadas = cola.Rechazadas == 0 ? ""
                : " " + cola.Rechazadas + (cola.Rechazadas == 1 ? " no se pudo enviar" : " no se pudieron enviar") + ": avisa a la persona responsable del laboratorio.";

            if (Activo && Cache != null)
            {
                return "SIN CONEXIÓN con el servidor. Horario del " + Cache.Generado.ToString("dd/MM HH:mm")
                    + ". Estudiantes: escribe tu matrícula (la contraseña no se puede comprobar)."
                    + (Cache.Modo == ModoCodigo.Renovable ? " Elige tu clase de la lista: sin conexión el código de la clase no se puede comprobar." : "")
                    + pendientes + apartadas;
            }

            return (pendientes + apartadas).Trim();
        }

        private static string PrimeraLinea(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return "";
            }

            int corte = texto.IndexOf('\n');
            return (corte < 0 ? texto : texto.Substring(0, corte)).Trim();
        }
    }
}
