using System;
using System.Collections.Generic;
using System.Linq;

namespace DigLab.conexion
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
        /// <summary>La sesion de hoy, cuando la candidata sale de una (generacion de codigos).</summary>
        public int? SesionId;
    }

    /// <summary>
    /// Un tramo de un dia sin ninguna clase abierta en un laboratorio: el del uso
    /// libre (sin codigos y sin clase, db/14).
    /// </summary>
    public class TramoLibre
    {
        public TimeSpan Inicio;
        public TimeSpan Fin;
    }

    /// <summary>
    /// Decide en que clase entra un alumno, en que laboratorio y a que hora. La
    /// usan igual el servidor (HorariosDatos) y la copia local sin conexion
    /// (CacheHorario), para que las dos den la misma respuesta.
    ///
    /// Con el codigo fijo (db/09) el codigo es del profesor y vale para todas
    /// sus clases, asi que las candidatas pueden ser de varias clases. No hay
    /// ambiguedad porque un profesor no da dos clases a la vez en el mismo
    /// laboratorio. El unico cruce es el margen de 15 minutos antes del inicio:
    /// si tiene dos clases seguidas en el mismo laboratorio, en esos 15 minutos
    /// valen las dos, y gana la que esta en curso. Sin codigo (db/11) el cruce
    /// es entre profesores, y por eso el alumno elige (Abiertas).
    /// </summary>
    internal static class ReglasCodigo
    {
        /// <summary>Se puede registrar en ella: no esta cancelada y es entre 15 minutos antes del inicio y el fin.</summary>
        public static bool Abierta(TimeSpan inicio, TimeSpan fin, bool cancelada, TimeSpan ahora)
        {
            return !cancelada && ahora >= inicio - TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos) && ahora <= fin;
        }

        /// <summary>Las clases de la agenda en las que se puede registrar ahora, en orden de inicio.</summary>
        public static List<SesionAgenda> Abiertas(IEnumerable<SesionAgenda> agenda, TimeSpan ahora)
        {
            return agenda.Where(s => Abierta(s.Inicio, s.Fin, s.Cancelada, ahora)).OrderBy(s => s.Inicio).ToList();
        }

        /// <summary>Donde acaba el dia para el uso libre.</summary>
        public static readonly TimeSpan FinDelDia = new TimeSpan(23, 59, 59);

        /// <summary>
        /// Lo que se le dice al alumno si su codigo no existe (con el servidor y con la
        /// copia local): que revisar y a quien pedirselo. Antes, "Código no válido.
        /// Revisa que esté bien escrito.", sin decir que se suele confundir.
        /// </summary>
        public static string NoExisteElCodigo(string codigo)
        {
            return "No existe el código «" + (codigo ?? "").Trim() + "». Revísalo letra por letra (no confundas la O con el 0 ni la I con el 1) "
                + "o pide a tu docente que lo repita.";
        }

        /// <summary>
        /// Sin codigos y sin clase (uso libre, db/14): el tramo sin clase en que cae
        /// ahora, desde el fin de la ultima clase que ya termino (o las 00:00) hasta
        /// que se abre la siguiente, 15 minutos antes de su inicio (o el fin del
        /// dia): entonces el laboratorio es de esa clase. Las canceladas no cuentan.
        /// Null si ahora hay una clase abierta: el alumno se registra en ella.
        /// </summary>
        public static TramoLibre TramoLibreEn(IEnumerable<SesionAgenda> agenda, TimeSpan ahora)
        {
            TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            List<SesionAgenda> clases = agenda.Where(s => !s.Cancelada).ToList();
            if (clases.Any(s => Abierta(s.Inicio, s.Fin, false, ahora)))
            {
                return null;
            }

            return new TramoLibre
            {
                Inicio = clases.Where(s => s.Fin < ahora).Select(s => s.Fin).DefaultIfEmpty(TimeSpan.Zero).Max(),
                Fin = clases.Where(s => s.Inicio - margen > ahora).Select(s => s.Inicio - margen).DefaultIfEmpty(FinDelDia).Min()
            };
        }

        /// <param name="sinServidor">Sin servidor solo se conoce el horario de este laboratorio.</param>
        /// <param name="porCodigo">False sin codigo: los rechazos no hablan de codigo.</param>
        public static Candidata Elegir(IList<Candidata> hoy, int idLaboratorio, TimeSpan ahora, bool sinServidor, out ResultadoCodigo rechazo, bool porCodigo = true)
        {
            rechazo = null;
            List<Candidata> aqui = hoy.Where(c => c.LaboratorioId == idLaboratorio).ToList();
            if (aqui.Count == 0)
            {
                if (!porCodigo)
                {
                    rechazo = Rechazo(MotivoCodigo.HoyNoHayClase, "Hoy no hay clases en este laboratorio.");
                    return null;
                }

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
            List<Candidata> abiertas = aqui.Where(c => Abierta(c.Inicio, c.Fin, c.Cancelada, ahora)).ToList();
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
                rechazo = Rechazo(MotivoCodigo.AunNoEmpieza, porCodigo
                    ? "Tu clase empieza a las " + HorariosDatos.Hora(siguiente.Inicio) + ". Inténtalo más cerca de la hora."
                    : "La siguiente clase empieza a las " + HorariosDatos.Hora(siguiente.Inicio) + ": podrás registrar desde las "
                      + HorariosDatos.Hora(siguiente.Inicio - margen) + ".");
                return null;
            }

            Candidata ultima = aqui.Where(c => !c.Cancelada).OrderByDescending(c => c.Fin).FirstOrDefault();
            if (ultima == null)
            {
                // Todas las de hoy en este laboratorio estan canceladas y ya pasaron.
                rechazo = Rechazo(MotivoCodigo.Cancelada, "La clase de hoy está cancelada.");
                return null;
            }

            rechazo = Rechazo(MotivoCodigo.Expirado, porCodigo
                ? "Tu clase terminó a las " + HorariosDatos.Hora(ultima.Fin)
                  + (sinServidor ? "." : ". Pide a tu docente que avise a la administración de DigLab si necesitas una sesión extra.")
                : "La última clase de hoy en este laboratorio terminó a las " + HorariosDatos.Hora(ultima.Fin) + ".");
            return null;
        }

        /// <summary>La agenda como candidatas (todas de este laboratorio).</summary>
        public static List<Candidata> DesdeAgenda(IEnumerable<SesionAgenda> agenda, int idLaboratorio, string laboratorio)
        {
            return agenda.Select(s => new Candidata
            {
                ClaseId = s.ClaseId,
                EsExtra = s.EsExtra,
                Id = s.EsExtra ? s.ExcepcionId.GetValueOrDefault() : s.FranjaId.GetValueOrDefault(),
                LaboratorioId = idLaboratorio,
                Laboratorio = laboratorio,
                Inicio = s.Inicio,
                Fin = s.Fin,
                Cancelada = s.Cancelada,
                SesionId = s.SesionId
            }).ToList();
        }

        public static ResultadoCodigo Rechazo(MotivoCodigo motivo, string mensaje)
        {
            return new ResultadoCodigo { Motivo = motivo, Mensaje = mensaje };
        }
    }
}
