using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigBit.conexion
{
    /// <summary>Que es un renglon de "Antes de aplicar": rojo no deja aplicar, ambar avisa, verde esta bien y gris informa.</summary>
    public enum NivelRevision
    {
        Rojo,
        Ambar,
        Verde,
        Gris
    }

    /// <summary>Un renglon de "Antes de aplicar" (pestana Aplicacion).</summary>
    public class RenglonRevision
    {
        public NivelRevision Nivel;
        public string Texto = "";
        /// <summary>Con que filtro abre la configuracion en Clases el enlace "Verlas" (Ninguno: sin enlace).</summary>
        public FiltroClases Ver;

        /// <summary>No deja aplicar.</summary>
        public bool Bloquea
        {
            get { return Nivel == NivelRevision.Rojo; }
        }
    }

    /// <summary>
    /// Los renglones de "Antes de aplicar" de la pestana Aplicacion
    /// (especificacion v2, 3.6), en memoria: lo que no deja aplicar (en rojo, o
    /// en verde si esta bien), lo que solo avisa (ambar) y lo que informa (gris).
    /// Sale de RevisionCiclo.Revisar (ya en DatosDeAplicacion.Revision) mas lo de
    /// las fechas. Aplicar lo vuelve a pasar dentro de su transaccion: con algo
    /// en rojo no aplica.
    /// </summary>
    internal static class RevisionAplicacion
    {
        private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

        /// <summary>Los renglones para esas fechas y ese nombre en el registro, ordenados rojo, ambar, verde y gris.</summary>
        public static List<RenglonRevision> Revisar(DatosDeAplicacion datos, DateTime desde, DateTime hasta, DateTime fin, string nombre)
        {
            if (datos == null || datos.Leido == null)
            {
                throw new ArgumentNullException("datos");
            }

            desde = desde.Date;
            hasta = hasta.Date;
            fin = fin.Date;
            List<RenglonRevision> renglones = new List<RenglonRevision>();
            CicloLeido leido = datos.Leido;
            RevisionDelCiclo revision = datos.Revision ?? RevisionCiclo.Revisar(leido);
            List<CargaCiclo> vivas = leido.Cargas.Where(c => !c.Quitada).ToList();

            // 1. Docente.
            renglones.Add(revision.SinProfesor == 0
                ? Verde("Todas las clases tienen docente.")
                : Rojo(revision.SinProfesor == 1
                    ? "1 clase sin docente: dásela a alguien o quítala."
                    : revision.SinProfesor + " clases sin docente: dáselas a alguien o quítalas.", FiltroClases.SinDocente));

            // 2. Docentes de baja (db/08).
            if (leido.ConBajas)
            {
                renglones.Add(revision.DeBaja == 0
                    ? Verde("Ninguna persona docente está de baja.")
                    : Rojo(revision.DeBaja == 1
                        ? "1 clase es de una persona docente de baja (" + RevisionCiclo.Enumerar(revision.DocentesDeBaja) + "): pásala a otra persona docente."
                        : revision.DeBaja + " clases son de docentes de baja (" + RevisionCiclo.Enumerar(revision.DocentesDeBaja) + "): pásalas a otra persona docente.",
                        FiltroClases.DeBaja));
            }

            // 3. Laboratorios desactivados (db/08).
            if (leido.ConLaboratoriosDesactivables)
            {
                renglones.Add(revision.EnLaboratorioDesactivado == 0
                    ? Verde("Todos sus laboratorios están activos.")
                    : Rojo((revision.EnLaboratorioDesactivado == 1 ? "1 clase tiene" : revision.EnLaboratorioDesactivado + " clases tienen")
                        + (revision.LaboratoriosDesactivados.Count == 1 ? " horas en un laboratorio desactivado (" : " horas en laboratorios desactivados (")
                        + RevisionCiclo.Enumerar(revision.LaboratoriosDesactivados)
                        + (revision.EnLaboratorioDesactivado == 1 ? "): cámbiale el horario." : "): cámbiales el horario."), FiltroClases.LaboratorioDesactivado));
            }

            // 4. Choques, tambien con las clases que ya estan en Clases en esas fechas.
            int choques = revision.Choques.Count;
            int conProgramadas = revision.Choques.Count(c => c.Programada != null);
            renglones.Add(choques == 0
                ? Verde("No hay choques.")
                : Rojo((choques == 1 ? "1 choque: corrígelo antes de aplicar" : choques + " choques: corrígelos antes de aplicar")
                    + (conProgramadas == 0 ? "."
                        : conProgramadas == choques
                            ? (choques == 1 ? " (es con una clase que ya está en Clases en esas fechas)." : " (son con clases que ya están en Clases en esas fechas).")
                            : " (" + conProgramadas + " con clases que ya están en Clases en esas fechas)."), FiltroClases.Choques));

            // 5. Repetidas: la misma persona docente con la misma materia y grupo dos veces.
            List<CargaCiclo> repetidas = revision.Repetidas
                .GroupBy(c => new { c.ProfesorId, c.GrupoId, c.MateriaId })
                .Select(g => g.First())
                .ToList();
            renglones.Add(repetidas.Count == 0
                ? Verde("No hay clases repetidas.")
                : Rojo("Repetida: " + repetidas[0].ProfesorCorto + " tiene dos veces " + repetidas[0].Nombre + ". Quita una."
                    + Mas(repetidas.Count - 1), FiltroClases.Repetidas));

            // 6. R6: la misma clase ya esta en Clases en esas fechas.
            if (datos.TernasCruzadas.Count == 0)
            {
                renglones.Add(Verde("Ninguna clase se cruza con otra igual que ya está en Clases."));
            }
            else
            {
                TernaCruzada primera = datos.TernasCruzadas[0];
                renglones.Add(Rojo(primera.Materia + " con " + primera.Grupo + " (" + primera.Profesor + ") ya está en Clases "
                    + HistorialCambios.Vigencia(primera.Desde, primera.Hasta) + ": quítala de aquí o termina antes la otra."
                    + Mas(datos.TernasCruzadas.Select(t => t.CargaId).Distinct().Count() - 1)));
            }

            // 7. Que tenga clases.
            renglones.Add(vivas.Count > 0 ? Verde("Tiene clases.") : Rojo("No tiene ninguna clase."));

            // 8. Fechas.
            string errorFechas = ErrorDeFechas(datos.Hoy, desde, hasta, fin);
            renglones.Add(errorFechas == null ? Verde("Las fechas están en orden.") : Rojo(errorFechas));

            // 9. Ninguna clase de ahora termina antes de su ultima sesion.
            SesionPosterior sesion = datos.SesionPosterior;
            renglones.Add(sesion == null
                ? Verde("Las clases de ahora no tienen sesiones después del " + Fecha(fin) + ".")
                : Rojo("El último día del horario de ahora no puede ser antes del " + Fecha(sesion.Fecha) + ": ese día "
                    + (sesion.EsAsistenciaDada ? "se dio asistencia en " : "hubo una sesión de ") + sesion.Clase + "."));

            // 10. El nombre en el registro (solo si falla).
            string errorNombre = ErrorDeNombre(nombre, datos.NombresAplicadas);
            if (errorNombre != null)
            {
                renglones.Add(Rojo(errorNombre));
            }

            // 11. Sin horario: solo avisa.
            if (revision.SinHorario > 0)
            {
                renglones.Add(Ambar(revision.SinHorario == 1
                    ? "1 clase sin horario: se crea sin horas en ningún laboratorio."
                    : revision.SinHorario + " clases sin horario: se crean sin horas en ningún laboratorio.", FiltroClases.SinHorario));
            }

            // 12. La misma materia con el mismo grupo con dos personas docentes (o dos veces sin docente).
            List<List<CargaCiclo>> dobles = vivas
                .GroupBy(c => new { c.GrupoId, c.MateriaId })
                .Where(g => g.Count() > 1 && (g.Select(c => c.ProfesorId).Distinct().Count() > 1 || g.Any(c => !c.ProfesorId.HasValue)))
                .Select(g => g.ToList())
                .ToList();
            if (dobles.Count > 0)
            {
                List<CargaCiclo> doble = dobles[0];
                List<string> quienes = doble.Select(c => c.ProfesorId.HasValue ? c.ProfesorCorto : "sin docente").Distinct().ToList();
                renglones.Add(Ambar(doble.All(c => c.ProfesorId.HasValue)
                    ? doble[0].Nombre + " la tienen " + Cuantas(doble.Count) + " personas docentes (" + RevisionCiclo.Enumerar(quienes) + ")." + Mas(dobles.Count - 1)
                    : doble[0].Nombre + " está " + doble.Count + " veces (" + RevisionCiclo.Enumerar(quienes) + ")." + Mas(dobles.Count - 1),
                    FiltroClases.DosDocentes));
            }

            // 13. Cambios "desde una fecha" (db/20) que ya no llegan a darse.
            if (datos.CambiosQueNoLlegan.Count > 0)
            {
                CambioQueNoLlega primero = datos.CambiosQueNoLlegan[0];
                int n = datos.CambiosQueNoLlegan.Count;
                renglones.Add(Ambar((n == 1
                        ? "1 cambio de horario que pusiste desde una fecha posterior al " + Fecha(fin) + " no llegará a darse: "
                        : n + " cambios de horario que pusiste desde una fecha posterior al " + Fecha(fin) + " no llegarán a darse: ")
                    + primero.Materia + " con " + primero.Grupo + " desde el " + Fecha(primero.Desde)
                    + (n > 1 ? " y " + (n - 1) + " más." : ".")));
            }

            // 14. Sesiones extra despues del ultimo dia.
            if (datos.ExtrasFuera.Count > 0)
            {
                ExtraFuera primera = datos.ExtrasFuera[0];
                int n = datos.ExtrasFuera.Count;
                renglones.Add(Ambar(n == 1
                    ? "1 sesión extra queda fuera: " + primera.Materia + " con " + primera.Grupo + " el " + Fecha(primera.Fecha)
                      + " (su clase termina el " + Fecha(fin) + "). Quítala en Cambios de un día o cambia el último día."
                    : n + " sesiones extra quedan fuera: " + primera.Materia + " con " + primera.Grupo + " el " + Fecha(primera.Fecha)
                      + " y " + (n - 1) + " más (sus clases terminan el " + Fecha(fin) + "). Quítalas en Cambios de un día o cambia el último día."));
            }

            // 15. Un ultimo dia que ya paso.
            if (fin < datos.Hoy.Date && errorFechas == null)
            {
                renglones.Add(Ambar((fin.AddDays(1) >= datos.Hoy.Date
                        ? "Esa fecha ya pasó: desde hoy esas clases dejan de contar en Faltas y en Alertas"
                        : "Esa fecha ya pasó: del " + Fecha(fin.AddDays(1)) + " a hoy esas clases dejan de contar en Faltas y en Alertas")
                    + ", y hasta el " + Fecha(desde) + " sus códigos ya no abren sesión."));
            }

            // 16. Empieza manana.
            if (desde == datos.Hoy.Date.AddDays(1))
            {
                renglones.Add(Ambar("Empieza mañana: aplícalo al menos un día de clases antes, para que cada equipo guarde el horario nuevo en su copia local."));
            }

            // 17 y 19. La casilla de Ajustes: si esta encendida y donde se enciende.
            renglones.Add(datos.ConFecha
                ? Verde("La casilla «Los cambios de horario aplican desde una fecha» de Ajustes está encendida: los cambios a mitad del ciclo aplican desde una fecha y lo que ya pasó no se reescribe.")
                : Ambar("La casilla «Los cambios de horario aplican desde una fecha» está apagada (se enciende en el panel de la administración, Ajustes, apartado Cambios de horario): si a mitad del ciclo cambias una hora, también cambian las semanas que ya pasaron. Enciéndela cuando todos los equipos tengan esta versión."));

            // 18. Las clases que ya estan en Clases en esas fechas (informa).
            List<FranjaInfo> programadas = leido.YaProgramadas.GroupBy(f => f.ClaseId).Select(g => g.First()).ToList();
            if (programadas.Count > 0)
            {
                List<string> nombres = programadas.Take(2).Select(f => f.Materia + " con " + f.Grupo + " (" + f.ProfesorCorto + ")").ToList();
                renglones.Add(Gris((programadas.Count == 1
                        ? "1 clase que ya está en Clases también vale en esas fechas: "
                        : programadas.Count + " clases que ya están en Clases también valen en esas fechas: ")
                    + RevisionCiclo.Enumerar(nombres) + (programadas.Count > 2 ? " y " + (programadas.Count - 2) + " más." : ".")));
            }

            // Rojo, ambar, verde y gris; dentro de cada uno, en el orden de arriba (OrderBy es estable).
            return renglones.OrderBy(r => (int)r.Nivel).ToList();
        }

        /// <summary>
        /// El primer error de las fechas, o null: el primer dia del horario nuevo
        /// despues de hoy, el ultimo no antes del primero y el ultimo dia del
        /// horario de ahora antes del primero del nuevo.
        /// </summary>
        public static string ErrorDeFechas(DateTime hoy, DateTime desde, DateTime hasta, DateTime fin)
        {
            if (desde.Date <= hoy.Date)
            {
                return "El primer día del nuevo horario tiene que ser después de hoy (" + Fecha(hoy) + ").";
            }

            if (hasta.Date < desde.Date)
            {
                return "El último día del nuevo horario no puede ser antes del primero.";
            }

            if (fin.Date >= desde.Date)
            {
                return "El último día del horario de ahora tiene que ser antes del primer día del nuevo (" + Fecha(desde) + ").";
            }

            return null;
        }

        /// <summary>El error del nombre en el registro, o null: vacio, largo o igual que otra aplicada (sin acentos ni mayusculas).</summary>
        private static string ErrorDeNombre(string nombre, IEnumerable<string> otrasAplicadas)
        {
            string limpio = (nombre ?? "").Trim();
            if (limpio.Length == 0)
            {
                return "Escribe el nombre con que queda en el registro.";
            }

            if (limpio.Length > CicloPreliminarDatos.LargoNombre)
            {
                return "El nombre en el registro puede tener hasta " + CicloPreliminarDatos.LargoNombre + " letras.";
            }

            string clave = Clave(limpio);
            string igual = (otrasAplicadas ?? Enumerable.Empty<string>()).FirstOrDefault(o => Clave(o) == clave);
            return igual == null ? null : "Ya hay una plantilla aplicada que se llama «" + igual + "»: cambia el nombre con que se guarda.";
        }

        /// <summary>Como CicloPreliminarDatos compara los nombres: sin acentos, sin mayusculas y con un solo espacio.</summary>
        private static string Clave(string nombre)
        {
            return Regex.Replace(EstiloAdmin.Normalizar((nombre ?? "").Trim()), @"\s+", " ");
        }

        private static string Fecha(DateTime fecha)
        {
            return fecha.ToString("dd/MM/yyyy", Invariante);
        }

        /// <summary>" Hay 2 mas." (con su acento; vacio si no hay mas), detras de una frase que ya acaba en punto.</summary>
        private static string Mas(int cuantas)
        {
            return cuantas <= 0 ? "" : " Hay " + cuantas + " más.";
        }

        private static string Cuantas(int n)
        {
            switch (n)
            {
                case 2:
                    return "dos";
                case 3:
                    return "tres";
                case 4:
                    return "cuatro";
                default:
                    return n.ToString(Invariante);
            }
        }

        private static RenglonRevision Rojo(string texto, FiltroClases ver = FiltroClases.Ninguno)
        {
            return new RenglonRevision { Nivel = NivelRevision.Rojo, Texto = texto, Ver = ver };
        }

        private static RenglonRevision Ambar(string texto, FiltroClases ver = FiltroClases.Ninguno)
        {
            return new RenglonRevision { Nivel = NivelRevision.Ambar, Texto = texto, Ver = ver };
        }

        private static RenglonRevision Verde(string texto)
        {
            return new RenglonRevision { Nivel = NivelRevision.Verde, Texto = texto };
        }

        private static RenglonRevision Gris(string texto)
        {
            return new RenglonRevision { Nivel = NivelRevision.Gris, Texto = texto };
        }
    }
}
