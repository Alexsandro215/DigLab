using System;
using System.Collections.Generic;
using System.Linq;

namespace DigLab.conexion
{
    /// <summary>La lista de una clase tal como la cuenta su tarjeta del panel (ResumenDeClase.Leer).</summary>
    public sealed class ListaDeClase
    {
        /// <summary>Alumnos de la lista: los de Mis grupos (quienes han registrado alguna vez en la clase o recibido de su profesor la asistencia).</summary>
        public int Alumnos;

        /// <summary>
        /// De ellos, los que hoy ya cuentan como presentes (registraron y no se les
        /// quito la asistencia, o se la dio su profesor, db/22).
        /// </summary>
        public int Hoy;

        /// <summary>
        /// De ellos, los que estaban en la lista antes de hoy (registraron o se les dio
        /// la asistencia). Con cero, hoy es el primer dia de la lista (se forma con
        /// quienes registran): todavia no dice cuantos faltan.
        /// </summary>
        public int AntesDeHoy;

        /// <summary>De los de Hoy, a cuantos se la dio su profesor sin bitacora (db/22).</summary>
        public int DadasHoy;
    }

    /// <summary>
    /// "16 de 24 alumnos" y "8 aun no" en las cifras de la clase del panel del
    /// profesor. La lista es la misma de Mis grupos y se cuenta igual
    /// (AsistenciaDatos.DeLaClase): con el codigo unico y dos clases seguidas
    /// del mismo profesor en el mismo laboratorio, los que llegan antes quedan
    /// en la sesion de la anterior y la asistencia los pasa a la suya. Contar
    /// solo quien registro en las sesiones de la clase los sumaba a la lista de
    /// la anterior (y en el horario de la escuela hay clases seguidas asi). Como
    /// esa cuenta pide varias consultas, solo se hace para las clases abiertas
    /// (o a punto de abrirse), las unicas cuyas cifras se ensenan, y solo
    /// cuando pudo cambiar: mientras las bitacoras de hoy del profesor sean las
    /// mismas (Huella) se repite la ultima, como mucho Vigencia. Solo lee.
    /// </summary>
    internal static class ResumenDeClase
    {
        // La lectura del panel es cada 30 s: una clase que se abre en el siguiente minuto ya se cuenta.
        private static readonly TimeSpan Adelanto = TimeSpan.FromMinutes(1);

        // Lo que cambia sin una bitacora nueva (una asistencia que el profesor
        // quita o da en Mis grupos, el grupo de un alumno) se ve a mas tardar en
        // este tiempo; al cerrar Mis grupos o al cambiar una asistencia, enseguida
        // (Olvidar).
        private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(3);

        // La ultima cuenta de cada clase (por idclases). La lectura va en segundo plano: con candado.
        private static readonly Dictionary<int, Cuenta> cuentas = new Dictionary<int, Cuenta>();
        // Sube con cada Olvidar: una lectura que empezo antes no guarda lo que conto.
        private static int generacion;

        private sealed class Cuenta
        {
            public DateTime Dia;
            public string Huella;
            public DateTime HechaUtc;
            public ListaDeClase Lista;
        }

        /// <summary>
        /// La lista de cada clase de hoy que esta abierta (desde 15 minutos antes
        /// hasta que termina), o lo estara en un minuto, a la hora del servidor de
        /// esa lectura. Lanza si no se puede leer.
        /// </summary>
        public static Dictionary<int, ListaDeClase> Leer(PanoramaProfesor panorama)
        {
            Dictionary<int, ListaDeClase> listas = new Dictionary<int, ListaDeClase>();
            if (panorama == null)
            {
                return listas;
            }

            DateTime hoy = panorama.AhoraServidor.Date;
            TimeSpan hora = panorama.AhoraServidor.TimeOfDay;
            string huella = Huella(panorama);
            int suya;
            lock (cuentas)
            {
                suya = generacion;
            }

            foreach (IGrouping<int, SesionAgenda> clase in panorama.Hoy.Where(s => !s.Cancelada && s.ClaseId > 0).GroupBy(s => s.ClaseId))
            {
                bool abierta = clase.Any(s => ReglasCodigo.Abierta(s.Inicio, s.Fin, false, hora) || ReglasCodigo.Abierta(s.Inicio, s.Fin, false, hora + Adelanto));
                if (!abierta)
                {
                    continue;
                }

                Cuenta anterior;
                lock (cuentas)
                {
                    cuentas.TryGetValue(clase.Key, out anterior);
                }

                if (anterior != null && anterior.Dia == hoy && anterior.Huella == huella && DateTime.UtcNow - anterior.HechaUtc < Vigencia)
                {
                    listas[clase.Key] = anterior.Lista;
                    continue;
                }

                // Sus bloques de hoy: su columna de hoy cuenta mientras dura (como en Mis grupos).
                AsistenciaDeClase asistencia = new AsistenciaDatos().DeLaClase(clase.Key, clase.Min(s => s.Inicio), clase.Max(s => s.Fin));
                ListaDeClase lista = new ListaDeClase
                {
                    Alumnos = asistencia.Alumnos.Count,
                    Hoy = asistencia.Alumnos.Count(a => a.Presente.Contains(hoy)),
                    AntesDeHoy = asistencia.Alumnos.Count(a => a.DiasEnLista.Any(d => d < hoy)),
                    DadasHoy = asistencia.Alumnos.Count(a => a.SinBitacora.Contains(hoy))
                };
                listas[clase.Key] = lista;
                lock (cuentas)
                {
                    // Si mientras contaba se cerro Mis grupos (pudo quitar una asistencia), no vale para la siguiente.
                    if (generacion == suya)
                    {
                        cuentas[clase.Key] = new Cuenta { Dia = hoy, Huella = huella, HechaUtc = DateTime.UtcNow, Lista = lista };
                    }
                }
            }

            return listas;
        }

        /// <summary>Que la siguiente lectura cuente otra vez (al cerrar Mis grupos, donde se quitan asistencias, o la sesion).</summary>
        public static void Olvidar()
        {
            lock (cuentas)
            {
                cuentas.Clear();
                generacion++;
            }
        }

        /// <summary>
        /// Las bitacoras de hoy del profesor, sesion por sesion ("346:16;347:3"):
        /// con una nueva (tambien en la clase de antes, donde quedan los que llegan
        /// antes) la huella cambia y la lista se vuelve a contar.
        /// </summary>
        private static string Huella(PanoramaProfesor panorama)
        {
            return string.Join(";", panorama.Hoy
                .Where(s => s.SesionId.HasValue)
                .Select(s => s.SesionId.Value + ":" + s.Alumnos)
                .Distinct()
                .OrderBy(t => t, StringComparer.Ordinal));
        }
    }
}
