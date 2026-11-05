using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using DigLab.conexion;
using MySql.Data.MySqlClient;

namespace DigLab.Infraestructura
{
    /// <summary>Como va el envio de la cola, para mostrarlo. Se sustituye entero, nunca se modifica.</summary>
    internal sealed class EstadoCola
    {
        public int Pendientes;
        public int Rechazadas;
        public DateTime? UltimoIntento;
        public DateTime? UltimoEnvio;
        public DateTime? ProximoIntento;
        /// <summary>Sin pila ni texto del driver: se puede ensenar en pantalla.</summary>
        public string UltimoError;
    }

    /// <summary>
    /// Envia en segundo plano las bitacoras que quedaron en este equipo, aunque
    /// no haya internet: lo sigue intentando hasta que llegan. Un hilo propio,
    /// nunca el de la pantalla (antes el reintento iba en el temporizador de la
    /// interfaz y, sin red, la congelaba hasta 5 s cada minuto). Arranca en
    /// Program.Main, antes que nada, asi que tambien corre si DigLab se relanza
    /// con el equipo ya liberado o mientras se ve la pantalla de error.
    ///
    /// Con algo pendiente prueba enseguida y, si falla, cada vez mas espaciado
    /// (15 s, 30 s, 1 min, 2 min y despues cada 5 min), con un poco de azar para
    /// que los equipos de un laboratorio no lleguen todos a la vez cuando vuelve
    /// el servidor. Vuelve a empezar en cuanto se encola algo nuevo o Windows
    /// avisa de un cambio de red. Sin nada pendiente, solo mira cada 5 min.
    /// </summary>
    internal static class EnviadorBitacoras
    {
        private static readonly TimeSpan[] Esperas =
        {
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)
        };

        private static readonly TimeSpan EsperaSinNada = TimeSpan.FromMinutes(5);
        private static readonly AutoResetEvent despertar = new AutoResetEvent(false);
        private static readonly ManualResetEvent pasadaTerminada = new ManualResetEvent(true);
        private static readonly Random azar = new Random();
        private static readonly object candado = new object();

        private static Thread hilo;
        private static volatile bool detener;
        private static volatile EstadoCola estado = new EstadoCola();
        private static int fallosSeguidos;
        private static bool servidorCaido;

        /// <summary>Como va el envio. La pantalla lo consulta cuando lo necesita (franja del login).</summary>
        public static EstadoCola Estado
        {
            get { return estado; }
        }

        /// <summary>Arranca el hilo. Se puede llamar mas de una vez: solo arranca la primera.</summary>
        public static void Iniciar()
        {
            lock (candado)
            {
                if (hilo != null)
                {
                    return;
                }

                detener = false;
                hilo = new Thread(Bucle) { IsBackground = true, Name = "DigLab.EnviadorBitacoras" };
            }

            try
            {
                NetworkChange.NetworkAvailabilityChanged += (s, e) => { if (e.IsAvailable) { Despertar(); } };
                NetworkChange.NetworkAddressChanged += (s, e) => Despertar();
            }
            catch (Exception ex)
            {
                // Sin estos avisos se sigue igual, con las esperas de siempre.
                Log.Aviso("No se pudo escuchar los cambios de red: " + ex.Message);
            }

            hilo.Start();
        }

        public static void Detener()
        {
            detener = true;
            despertar.Set();
        }

        /// <summary>Que intente ya, sin esperar su turno: algo se encolo o volvio la red.</summary>
        public static void Despertar()
        {
            lock (candado)
            {
                fallosSeguidos = 0;
            }

            despertar.Set();
        }

        /// <summary>
        /// Para "Apagar el equipo": intenta enviar ya y espera como mucho ese
        /// tiempo, sin bloquear la pantalla. True si la cola quedo vacia.
        /// </summary>
        public static Task<bool> EnviarAhoraAsync(TimeSpan limite)
        {
            return Task.Run(() =>
            {
                // Dos pasadas como mucho: si al pedirla ya habia una en marcha, su
                // final no cuenta (pudo empezar antes de lo ultimo encolado) y la
                // que vale es la siguiente.
                DateTime hasta = DateTime.UtcNow + limite;
                for (int intento = 0; intento < 2 && ColaBitacoras.Pendientes() > 0; intento++)
                {
                    TimeSpan queda = hasta - DateTime.UtcNow;
                    if (queda <= TimeSpan.Zero)
                    {
                        break;
                    }

                    pasadaTerminada.Reset();
                    Despertar();
                    if (!pasadaTerminada.WaitOne(queda))
                    {
                        break;
                    }
                }

                return ColaBitacoras.Pendientes() == 0;
            });
        }

        private static void Bucle()
        {
            while (!detener)
            {
                TimeSpan espera;
                try
                {
                    espera = Pasada();
                }
                catch (Exception ex)
                {
                    // Pasada no deberia lanzar nunca; si lo hace, que no se pare el envio.
                    Log.Error("Enviador de bitacoras", ex);
                    espera = Esperas[Esperas.Length - 1];
                }
                finally
                {
                    pasadaTerminada.Set();
                }

                despertar.WaitOne(espera);
            }
        }

        private static TimeSpan Pasada()
        {
            int pendientes = ColaBitacoras.Pendientes();
            if (pendientes == 0)
            {
                lock (candado)
                {
                    fallosSeguidos = 0;
                }

                Publicar(0, null, null, DateTime.Now + EsperaSinNada);
                return EsperaSinNada;
            }

            DateTime intento = DateTime.Now;
            try
            {
                ResultadoSincronizacion r = ColaBitacoras.Sincronizar();
                if (servidorCaido)
                {
                    servidorCaido = false;
                    Log.Info("Envio de bitacoras: el servidor vuelve a responder.");
                }

                lock (candado)
                {
                    fallosSeguidos = 0;
                }

                // Lo que no pudo subir por un error suyo se reintenta al minuto, sin prisas.
                TimeSpan siguiente = r.Pendientes > 0 ? TimeSpan.FromMinutes(1) : EsperaSinNada;
                Publicar(r.Pendientes, r.Enviadas + r.Duplicadas > 0 ? intento : (DateTime?)null, null, DateTime.Now + siguiente);
                return siguiente;
            }
            catch (Exception ex)
            {
                // Sin conexion con el servidor (o el archivo de la cola ocupado un
                // momento): ColaBitacoras solo lanza en esos casos.
                if (!servidorCaido)
                {
                    servidorCaido = true;
                    Log.Aviso("Envio de bitacoras: no se pudo (" + pendientes + " pendientes); se reintenta solo. " + ex.GetBaseException().Message);
                }

                // Que el siguiente intento no reutilice una conexion muerta del pool.
                try
                {
                    MySqlConnection.ClearAllPools();
                }
                catch (Exception)
                {
                }

                TimeSpan espera;
                lock (candado)
                {
                    espera = Esperas[Math.Min(fallosSeguidos, Esperas.Length - 1)];
                    fallosSeguidos++;
                }

                // +-20 % al azar.
                double factor;
                lock (azar)
                {
                    factor = 0.8 + azar.NextDouble() * 0.4;
                }

                espera = TimeSpan.FromMilliseconds(espera.TotalMilliseconds * factor);
                Publicar(ColaBitacoras.Pendientes(), null, "Sin conexión con el servidor", DateTime.Now + espera);
                return espera;
            }
        }

        private static void Publicar(int pendientes, DateTime? envio, string error, DateTime proximo)
        {
            EstadoCola anterior = estado;
            estado = new EstadoCola
            {
                Pendientes = pendientes,
                Rechazadas = ColaBitacoras.Rechazadas(),
                UltimoIntento = DateTime.Now,
                UltimoEnvio = envio ?? anterior.UltimoEnvio,
                ProximoIntento = proximo,
                UltimoError = error
            };
        }
    }
}
