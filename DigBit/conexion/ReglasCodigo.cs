using System;
using System.Collections.Generic;
using System.Linq;

namespace DigBit.conexion
{
    /// <summary>Una franja o sesion extra de hoy en la que el codigo tecleado podria entrar.</summary>
    internal class Candidata
    {
        public int ClaseId;
        public bool EsExtra;
        /// <summary>idhorarios de la franja, o idhorario_excepciones de la extra.</summary>
        public int Id;
        public int LaboratorioId;
        public string Laboratorio;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public bool Cancelada;
    }

    /// <summary>
    /// Decide en que clase entra un codigo, en que laboratorio y a que hora. La
    /// usan igual el servidor (HorariosDatos) y la copia local sin conexion
    /// (CacheHorario), para que las dos den la misma respuesta.
    ///
    /// Desde db/09_codigo_por_profesor.sql el codigo es del profesor y vale para
    /// todas sus clases, asi que las candidatas pueden ser de varias clases. No
    /// hay ambiguedad porque un profesor no da dos clases a la vez en el mismo
    /// laboratorio. El unico cruce es el margen de 15 minutos antes del inicio:
    /// si tiene dos clases seguidas en el mismo laboratorio, en esos 15 minutos
    /// valen las dos, y gana la que esta en curso.
    /// </summary>
    internal static class ReglasCodigo
    {
        /// <param name="sinServidor">Sin servidor solo se conoce el horario de este laboratorio.</param>
        public static Candidata Elegir(IList<Candidata> hoy, int idLaboratorio, TimeSpan ahora, bool sinServidor, out ResultadoCodigo rechazo)
        {
            rechazo = null;
            List<Candidata> aqui = hoy.Where(c => c.LaboratorioId == idLaboratorio).ToList();
            if (aqui.Count == 0)
            {
                if (hoy.Count == 0 || sinServidor)
                {
                    rechazo = Rechazo(MotivoCodigo.HoyNoHayClase, sinServidor
                        ? "Hoy no hay clase con ese código en este laboratorio."
                        : "Hoy no hay clase con ese código.");
                    return null;
                }

                string otros = string.Join(" / ", hoy.Select(c => c.Laboratorio).Distinct());
                rechazo = Rechazo(MotivoCodigo.OtroLaboratorio, "Ese código es de " + otros + ", no de este laboratorio.");
                return null;
            }

            TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            // Primero la que ya empezo (la mas reciente: a las 10:00 en punto gana la
            // de 10 a 12 sobre la de 8 a 10); si ninguna, la que empieza antes.
            List<Candidata> abiertas = aqui.Where(c => !c.Cancelada && ahora >= c.Inicio - margen && ahora <= c.Fin).ToList();
            Candidata vigente = abiertas.Where(c => ahora >= c.Inicio).OrderByDescending(c => c.Inicio).FirstOrDefault()
                ?? abiertas.OrderBy(c => c.Inicio).FirstOrDefault();
            if (vigente != null)
            {
                return vigente;
            }

            Candidata cancelada = aqui.FirstOrDefault(c => c.Cancelada && ahora >= c.Inicio - margen && ahora <= c.Fin);
            if (cancelada != null)
            {
                rechazo = Rechazo(MotivoCodigo.Cancelada, "La clase de hoy (" + HorariosDatos.Hora(cancelada.Inicio) + " a "
                    + HorariosDatos.Hora(cancelada.Fin) + ") está cancelada.");
                return null;
            }

            Candidata siguiente = aqui.Where(c => !c.Cancelada && ahora < c.Inicio - margen).OrderBy(c => c.Inicio).FirstOrDefault();
            if (siguiente != null)
            {
                rechazo = Rechazo(MotivoCodigo.AunNoEmpieza, "Tu clase empieza a las " + HorariosDatos.Hora(siguiente.Inicio)
                    + ". Inténtalo más cerca de la hora.");
                return null;
            }

            Candidata ultima = aqui.Where(c => !c.Cancelada).OrderByDescending(c => c.Fin).FirstOrDefault();
            if (ultima == null)
            {
                // Todas las de hoy en este laboratorio estan canceladas y ya pasaron.
                rechazo = Rechazo(MotivoCodigo.Cancelada, "La clase de hoy está cancelada.");
                return null;
            }

            rechazo = Rechazo(MotivoCodigo.Expirado, "Tu clase terminó a las " + HorariosDatos.Hora(ultima.Fin)
                + (sinServidor ? "." : ". Pide a tu profesor que avise al administrador si necesitas una sesión extra."));
            return null;
        }

        public static ResultadoCodigo Rechazo(MotivoCodigo motivo, string mensaje)
        {
            return new ResultadoCodigo { Motivo = motivo, Mensaje = mensaje };
        }
    }
}
