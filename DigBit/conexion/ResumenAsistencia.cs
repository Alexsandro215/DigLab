using System;
using System.Collections.Generic;
using System.Linq;

namespace DigBit.conexion
{
    /// <summary>Como le fue al grupo en una de sus clases (una columna de la lista).</summary>
    public class AsistenciaDelDia
    {
        public DiaDeAsistencia Dia;
        /// <summary>Cuantos tienen asistencia: registraron (y les cuenta) o se la dio su profesor (db/22).</summary>
        public int Asistieron;
        /// <summary>De ellos, a cuantos se la dio su profesor sin bitacora (db/22): para los textos.</summary>
        public int Dadas;
        /// <summary>Cuantos registraron sin estar en el salon (se les quito).</summary>
        public int Retiradas;
        /// <summary>Con la clase en curso: cuantos aun no registran (P, pendientes).</summary>
        public int Pendientes;
        /// <summary>Los de la lista: quien entro despues cuenta con falta en las anteriores.</summary>
        public int EnLista;

        /// <summary>De los que tienen asistencia, los que registraron su bitacora (sin las dadas).</summary>
        public int Registraron
        {
            get { return Asistieron - Dadas; }
        }

        public double Porcentaje
        {
            get { return EnLista == 0 ? 0 : 100.0 * Asistieron / EnLista; }
        }
    }

    /// <summary>Las cuentas de un alumno de la lista.</summary>
    public class AlumnoResumido
    {
        public AlumnoDeAsistencia Alumno;
        /// <summary>Clases en que tiene asistencia: registro o se la dio su profesor (tambien la de hoy si ya la tiene).</summary>
        public int Asistencias;
        /// <summary>De ellas, las que se la dio su profesor sin bitacora (db/22): para los textos.</summary>
        public int Dadas;
        /// <summary>Clases en que falto: las que terminaron sin registrar y las que se le quitaron. Lo pendiente de hoy no.</summary>
        public int Faltas;
        /// <summary>Faltas seguidas hasta la ultima clase que ya cuenta (lo pendiente de hoy se salta).</summary>
        public int FaltasSeguidas;
        /// <summary>El primer dia de esas faltas seguidas, o null sin faltas seguidas.</summary>
        public DateTime? InicioDeRacha;
        /// <summary>Veces que registro sin estar en el salon.</summary>
        public int Retiradas;
        /// <summary>Hoy, con la clase en curso: ya tiene asistencia (registro y le cuenta, o se la dio su profesor).</summary>
        public bool RegistroHoy;
        /// <summary>Hoy, con la clase en curso: aun no registra (P).</summary>
        public bool PendienteHoy;

        /// <summary>De 0 a 100 (asistencias entre asistencias y faltas); null si aun no hay nada que cuente.</summary>
        public double? Porcentaje
        {
            get { return Asistencias + Faltas == 0 ? (double?)null : 100.0 * Asistencias / (Asistencias + Faltas); }
        }

        public bool ConDerecho
        {
            get { return !Porcentaje.HasValue || ResumenAsistencia.TieneDerecho(Porcentaje.Value); }
        }
    }

    /// <summary>
    /// Lo que se calcula de la lista de asistencia de una clase para presentarla
    /// (Mis grupos), con las reglas de la hoja: lo pendiente de hoy (P: la clase
    /// sigue y aun no registra) no cuenta ni como asistencia ni como falta; la A
    /// de hoy si cuenta; una asistencia quitada (registro sin estar en el salon)
    /// es falta; una asistencia que dio el profesor (db/22) es asistencia, igual
    /// que una bitacora (ya esta en Presente: Dadas solo sirve para los textos).
    /// Con el minimo de asistencia se tiene derecho a evaluacion.
    /// </summary>
    public class ResumenAsistencia
    {
        /// <summary>Desde aqui, derecho a evaluacion (lo que suele pedir el TecNM).</summary>
        public const int Minimo = 80;

        /// <summary>
        /// El % que se ensena, en la hoja, el tablero y la lista: truncado, para que
        /// nunca se vea "80 %" junto a "Sin derecho" (79,6 % se ensena 79 %).
        /// </summary>
        public static int PorcentajeMostrado(double valor)
        {
            return (int)Math.Floor(valor + 1e-9);
        }

        /// <summary>Con derecho a evaluacion: el valor exacto llega al minimo. La misma regla en todas partes.</summary>
        public static bool TieneDerecho(double valor)
        {
            return valor >= Minimo - 1e-9;
        }

        public AsistenciaDeClase Lista;
        public List<AsistenciaDelDia> PorDia = new List<AsistenciaDelDia>();
        public List<AlumnoResumido> Alumnos = new List<AlumnoResumido>();
        /// <summary>La clase de hoy mientras dura, o null.</summary>
        public AsistenciaDelDia Hoy;

        /// <summary>Asistencia del grupo (0 a 100), o null si aun no hay nada que cuente.</summary>
        public double? Promedio
        {
            get
            {
                int asistencias = Alumnos.Sum(a => a.Asistencias);
                int posibles = asistencias + Alumnos.Sum(a => a.Faltas);
                return posibles == 0 ? (double?)null : 100.0 * asistencias / posibles;
            }
        }

        /// <summary>Sin derecho a evaluacion (bajo el minimo), del que menos al que mas.</summary>
        public List<AlumnoResumido> SinDerecho
        {
            get { return Alumnos.Where(a => !a.ConDerecho).OrderBy(a => a.Porcentaje.Value).ThenByDescending(a => a.FaltasSeguidas).ToList(); }
        }

        /// <summary>Con dos o mas faltas seguidas hasta la ultima clase.</summary>
        public List<AlumnoResumido> ConFaltasSeguidas
        {
            get { return Alumnos.Where(a => a.FaltasSeguidas >= 2).OrderByDescending(a => a.FaltasSeguidas).ToList(); }
        }

        /// <summary>Los que alguna vez registraron sin estar en el salon.</summary>
        public List<AlumnoResumido> SinEstar
        {
            get { return Alumnos.Where(a => a.Retiradas > 0).OrderByDescending(a => a.Retiradas).ToList(); }
        }

        public static ResumenAsistencia De(AsistenciaDeClase lista)
        {
            return De(lista, null);
        }

        /// <summary>
        /// Las cuentas contando solo los dias que pasan el filtro (un periodo, un
        /// laboratorio: Estadisticas). La lista no cambia: quien entro a ella despues
        /// cuenta con falta en los dias del filtro anteriores, como en Mis grupos.
        /// Retiradas y Dadas de cada alumno tambien son solo de esos dias, y la racha
        /// de faltas es la que llega al ultimo de ellos. Con null cuentan todos (De(lista)).
        /// </summary>
        public static ResumenAsistencia De(AsistenciaDeClase lista, Func<DiaDeAsistencia, bool> cuenta)
        {
            ResumenAsistencia resumen = new ResumenAsistencia { Lista = lista };
            List<DiaDeAsistencia> dias = lista.Dias.Where(d => cuenta == null || cuenta(d)).OrderBy(d => d.Fecha).ToList();
            HashSet<DateTime> fechas = cuenta == null ? null : new HashSet<DateTime>(dias.Select(d => d.Fecha));
            foreach (DiaDeAsistencia dia in dias)
            {
                AsistenciaDelDia delDia = new AsistenciaDelDia
                {
                    Dia = dia,
                    EnLista = lista.Alumnos.Count,
                    Asistieron = lista.Alumnos.Count(a => a.Presente.Contains(dia.Fecha)),
                    Dadas = lista.Alumnos.Count(a => a.SinBitacora.Contains(dia.Fecha)),
                    Retiradas = lista.Alumnos.Count(a => a.Retiradas.Contains(dia.Fecha))
                };
                if (dia.EnCurso)
                {
                    delDia.Pendientes = delDia.EnLista - delDia.Asistieron - delDia.Retiradas;
                    resumen.Hoy = delDia;
                }

                resumen.PorDia.Add(delDia);
            }

            foreach (AlumnoDeAsistencia alumno in lista.Alumnos)
            {
                AlumnoResumido resumido = new AlumnoResumido
                {
                    Alumno = alumno,
                    Retiradas = fechas == null ? alumno.Retiradas.Count : alumno.Retiradas.Count(fechas.Contains),
                    Dadas = fechas == null ? alumno.SinBitacora.Count : alumno.SinBitacora.Count(fechas.Contains)
                };
                bool racha = true;
                for (int i = dias.Count - 1; i >= 0; i--)
                {
                    DiaDeAsistencia dia = dias[i];
                    bool vino = alumno.Presente.Contains(dia.Fecha);
                    bool pendiente = dia.EnCurso && !vino && !alumno.Retiradas.Contains(dia.Fecha);
                    if (dia.EnCurso)
                    {
                        resumido.RegistroHoy = vino;
                        resumido.PendienteHoy = pendiente;
                    }

                    if (pendiente)
                    {
                        continue;
                    }

                    if (vino)
                    {
                        resumido.Asistencias++;
                        racha = false;
                    }
                    else
                    {
                        resumido.Faltas++;
                        if (racha)
                        {
                            resumido.FaltasSeguidas++;
                            // Se va hacia atras: la ultima que se anota es la primera de la racha.
                            resumido.InicioDeRacha = dia.Fecha;
                        }
                    }
                }

                resumen.Alumnos.Add(resumido);
            }

            return resumen;
        }
    }
}
