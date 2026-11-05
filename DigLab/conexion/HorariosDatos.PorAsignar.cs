using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// Una franja de otra clase del mismo laboratorio que un reemplazo recorta
    /// (PlanDeReemplazo): sus horas y lo que se le quita, que queda por asignar.
    /// </summary>
    public class FranjaReemplazada
    {
        public int FranjaId;
        public int ClaseId;
        public string Materia = "";
        public string Grupo = "";
        public string Profesor = "";
        public int LaboratorioId;
        public string Laboratorio = "";
        public int DiaSemana;
        /// <summary>Sus horas de ahora.</summary>
        public TimeSpan Inicio;
        public TimeSpan Fin;
        /// <summary>Lo que se le quita (el cruce con la clase que entra).</summary>
        public TimeSpan QuitaInicio;
        public TimeSpan QuitaFin;
        /// <summary>
        /// Sin "Los cambios de horario aplican desde una fecha" y con historia: el
        /// recorte cambia tambien sus semanas pasadas (como "Cambiar horas" hoy).
        /// </summary>
        public bool CambiaElPasado;

        public int Minutos
        {
            get { return HorariosDatos.MinutosEntre(QuitaInicio, QuitaFin); }
        }

        /// <summary>"Redes, 5A (Ana Ruiz) del lunes 08:00–10:00 (2 h)", como lo dice la confirmacion.</summary>
        public string Describir()
        {
            return Materia + ", " + Grupo + " (" + Profesor + ") del " + HorariosDatos.NombresDia[DiaSemana].ToLowerInvariant() + " "
                + HorariosDatos.Hora(QuitaInicio) + "–" + HorariosDatos.Hora(QuitaFin) + " (" + HorariosDatos.Duracion(Minutos) + ")";
        }
    }

    /// <summary>Lo que haria poner unas horas encima de otras del mismo laboratorio (HorariosDatos.PlanDeReemplazo).</summary>
    public class PlanReemplazo
    {
        /// <summary>Las de otras clases del mismo laboratorio a las que se les quitaria lo que se cruza.</summary>
        public List<FranjaReemplazada> Quitadas = new List<FranjaReemplazada>();
        /// <summary>
        /// Lo que no deja ponerlas aunque se reemplace (la misma persona docente o
        /// el mismo grupo a esa hora en otro laboratorio, o unas horas que no
        /// valen), como lo dice ValidarFranja, o lo que no deja quitarle esas horas
        /// a una de las Quitadas (lo que le quedaria choca, un recorte...); null si nada.
        /// </summary>
        public string Bloqueo;
    }

    /// <summary>Lo que hizo un reemplazo (PonerClaseReemplazando, CambiarFranjaReemplazando).</summary>
    public class ResultadoReemplazo
    {
        /// <summary>La franja que quedo con las horas pedidas.</summary>
        public int FranjaId;
        public List<FranjaReemplazada> Quitadas = new List<FranjaReemplazada>();
        /// <summary>Los minutos que bajaron de lo que le faltaba a la clase (lo mas antiguo primero).</summary>
        public int MinutosAsignados;
        /// <summary>Lo que le sigue faltando a la clase, en minutos.</summary>
        public int MinutosQueFaltan;
    }

    /// <summary>Unas horas que le faltan a una clase en el horario en uso (horas_por_asignar, db/25).</summary>
    public class HoraPorAsignar
    {
        public int Id;
        public int ClaseId;
        public string Materia = "";
        public string Grupo = "";
        public string Profesor = "";
        public int ProfesorId;
        public int Minutos;
        public int MinutosOriginales;
        public int? LaboratorioId;
        public string Laboratorio = "";
        public int DiaSemana;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public DateTime Desde;
        /// <summary>HorariosDatos.MotivoDesplazada o MotivoQuitada.</summary>
        public string Motivo = "";
        public DateTime CreadaEn;

        /// <summary>"Redes - 5A".</summary>
        public string Clase
        {
            get { return Materia + " - " + Grupo; }
        }

        /// <summary>"Lab 1, lunes 08:00–10:00".</summary>
        public string Origen
        {
            get
            {
                return (string.IsNullOrEmpty(Laboratorio) ? "" : Laboratorio + ", ")
                    + (DiaSemana >= 1 && DiaSemana <= 7 ? HorariosDatos.NombresDia[DiaSemana].ToLowerInvariant() + " " : "")
                    + HorariosDatos.Hora(Inicio) + "–" + HorariosDatos.Hora(Fin);
            }
        }
    }

    /// <summary>
    /// Horario semanal con db/25 (horas_por_asignar): poner o cambiar horas
    /// encima de otra clase del mismo laboratorio la reemplaza en vez de chocar
    /// (a la otra se le quita solo lo que se cruza, y eso queda por asignar), y
    /// "Quitar horas" puede dejar las suyas por asignar. Lo que falta baja solo al
    /// ponerle horas a la clase (lo mas antiguo primero). Todo en una
    /// transaccion, con su renglon en el historial (db/21). Con los cambios desde
    /// una fecha (db/20, ConFecha), el reemplazo vale desde esa fecha: las franjas
    /// con historia se terminan el dia antes, no se reescriben. Un choque con la
    /// misma persona docente o el mismo grupo en otro laboratorio sigue sin dejar
    /// guardar. Sin db/25 nada de esto se usa (HorasPorAsignarDisponible).
    /// </summary>
    internal partial class HorariosDatos
    {
        public const string MotivoDesplazada = "desplazada";
        public const string MotivoQuitada = "quitada";
        private const string EstadoPendiente = "pendiente";
        private const string EstadoAsignada = "asignada";
        private const string EstadoNoHaceFalta = "no_hace_falta";

        /// <summary>Minutos enteros de inicio a fin (0 si fin no es posterior).</summary>
        internal static int MinutosEntre(TimeSpan inicio, TimeSpan fin)
        {
            return fin > inicio ? (int)Math.Round((fin - inicio).TotalMinutes) : 0;
        }

        /// <summary>"2 h", "1 h 30 min", "45 min".</summary>
        public static string Duracion(int minutos)
        {
            if (minutos <= 0)
            {
                return "0 h";
            }

            int horas = minutos / 60;
            int resto = minutos % 60;
            return horas == 0 ? resto + " min" : resto == 0 ? horas + " h" : horas + " h " + resto + " min";
        }

        /// <summary>True si la base tiene horas_por_asignar (db/25) y esta cuenta la ve.</summary>
        public bool HorasPorAsignarDisponible()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return Esquema.HorasPorAsignar(conexion);
            }
        }

        /// <summary>
        /// Lo que falta por asignar en todos los laboratorios, de lo mas antiguo a
        /// lo mas nuevo: solo de clases activas que no han terminado. Null sin db/25.
        /// </summary>
        public List<HoraPorAsignar> LeerHorasPorAsignar()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.HorasPorAsignar(conexion))
                {
                    return null;
                }

                List<HoraPorAsignar> lista = new List<HoraPorAsignar>();
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT p.idhoras_por_asignar, p.clases_idclases, p.minutos, p.minutos_originales, p.laboratorio_id, l.nombre_laboratorio,
                           p.dia_semana, p.hora_inicio, p.hora_fin, p.desde, p.motivo, p.creada_en,
                           m.nombre_materia, g.nombre_grupo, c.usuarios_idusuarios,
                           CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor
                    FROM horas_por_asignar p
                    INNER JOIN clases c       ON c.idclases = p.clases_idclases
                    INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                    INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                    LEFT JOIN usuarios u      ON u.idusuarios = c.usuarios_idusuarios
                    LEFT JOIN laboratorios l  ON l.idlaboratorios = p.laboratorio_id
                    WHERE p.estado = @Pendiente AND p.minutos > 0 AND c.activa = 1 AND c.vigente_hasta >= CURDATE()
                    ORDER BY p.creada_en, p.idhoras_por_asignar", conexion))
                {
                    comando.Parameters.AddWithValue("@Pendiente", EstadoPendiente);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new HoraPorAsignar
                            {
                                Id = Convert.ToInt32(reader["idhoras_por_asignar"], CultureInfo.InvariantCulture),
                                ClaseId = Convert.ToInt32(reader["clases_idclases"], CultureInfo.InvariantCulture),
                                Minutos = Convert.ToInt32(reader["minutos"], CultureInfo.InvariantCulture),
                                MinutosOriginales = Convert.ToInt32(reader["minutos_originales"], CultureInfo.InvariantCulture),
                                LaboratorioId = reader["laboratorio_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["laboratorio_id"], CultureInfo.InvariantCulture),
                                Laboratorio = Texto(reader, "nombre_laboratorio"),
                                DiaSemana = Convert.ToInt32(reader["dia_semana"], CultureInfo.InvariantCulture),
                                Inicio = LeerHora(reader["hora_inicio"]) ?? TimeSpan.Zero,
                                Fin = LeerHora(reader["hora_fin"]) ?? TimeSpan.Zero,
                                Desde = Convert.ToDateTime(reader["desde"], CultureInfo.InvariantCulture).Date,
                                Motivo = Texto(reader, "motivo"),
                                CreadaEn = Convert.ToDateTime(reader["creada_en"], CultureInfo.InvariantCulture),
                                Materia = Texto(reader, "nombre_materia"),
                                Grupo = Texto(reader, "nombre_grupo"),
                                ProfesorId = Convert.ToInt32(reader["usuarios_idusuarios"], CultureInfo.InvariantCulture),
                                Profesor = Texto(reader, "profesor").Trim()
                            });
                        }
                    }
                }

                return lista;
            }
        }

        // ---------------------------------------------------------------------
        // Reemplazar en vez de chocar
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lo que pasaria al poner (franjaQueCambia null) o cambiar (su id) unas
        /// horas de una clase encima de otras del mismo laboratorio: a que
        /// franjas de OTRAS clases se les quitaria lo que se cruza, y si aun asi
        /// algo no deja guardarlas (Bloqueo): tambien lo que le quedaria a una de
        /// ellas (ProblemaAlRecortar), para no pedir confirmacion de algo que
        /// luego no se puede hacer. No escribe nada. desde: con los cambios desde
        /// una fecha, desde cuando valdria (como en CrearFranja y
        /// CambiarFranjaDesde); sin ellos, null.
        /// </summary>
        public PlanReemplazo PlanDeReemplazo(int? franjaQueCambia, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime? desde)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                VentanaDeReemplazo ventana = Ventana(conexion, null, franjaQueCambia, idClase, desde);
                PlanReemplazo plan = new PlanReemplazo();
                if (ventana.Problema != null)
                {
                    plan.Bloqueo = ventana.Problema;
                    return plan;
                }

                plan.Quitadas = QueSeReemplaza(conexion, null, franjaQueCambia, ventana, idLaboratorio, diaSemana, inicio, fin);
                plan.Bloqueo = ValidarFranjaEn(conexion, null, franjaQueCambia, idClase, idLaboratorio, diaSemana, inicio, fin,
                    ventana.PropiaDesde, ventana.PropiaHasta, plan.Quitadas.Select(q => q.FranjaId))
                    ?? ProblemaAlRecortar(conexion, null, plan.Quitadas, ventana, idLaboratorio, inicio, fin);
                return plan;
            }
        }

        /// <summary>
        /// "Poner clase..." con db/25: crea la franja como CrearFranja, pero lo que
        /// se cruza con OTRAS clases del mismo laboratorio se les quita (solo lo
        /// que se cruza) y queda por asignar, en vez de chocar. Lo que le faltaba a
        /// esta clase baja con sus horas nuevas. Todo o nada:
        /// InvalidOperationException si choca con la misma persona docente o el
        /// mismo grupo en otro laboratorio (o lo que diga ValidarFranja).
        /// </summary>
        public ResultadoReemplazo PonerClaseReemplazando(int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime? desde)
        {
            Permisos.Exigir("Crear una franja del horario (reemplazando)");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ExigirPorAsignar(conexion, transaccion);
                VentanaDeReemplazo ventana = Ventana(conexion, transaccion, null, idClase, desde);
                if (ventana.Problema != null)
                {
                    throw new InvalidOperationException(ventana.Problema);
                }

                List<FranjaReemplazada> quitadas = QueSeReemplaza(conexion, transaccion, null, ventana, idLaboratorio, diaSemana, inicio, fin);
                // Antes de escribir nada: lo que no se arregla reemplazando (la misma
                // persona docente o el mismo grupo en otro laboratorio).
                string conflicto = ValidarFranjaEn(conexion, transaccion, null, idClase, idLaboratorio, diaSemana, inicio, fin,
                    ventana.PropiaDesde, null, quitadas.Select(q => q.FranjaId));
                if (conflicto != null)
                {
                    throw new InvalidOperationException(conflicto);
                }

                string quienEntra = TextoDeClase(ventana.Clase.Materia, ventana.Clase.Grupo, ventana.Clase.Profesor);
                int? quien = IdDeLaSesion(conexion, transaccion);
                Recortar(conexion, transaccion, quitadas, ventana, idLaboratorio, inicio, fin, quienEntra, quien);

                // Ya sin lo que se cruzaba: como CrearFranja.
                conflicto = ValidarFranjaEn(conexion, transaccion, null, idClase, idLaboratorio, diaSemana, inicio, fin, ventana.PropiaDesde, null);
                if (conflicto != null)
                {
                    throw new InvalidOperationException(conflicto);
                }

                int id = InsertarFranja(conexion, transaccion, idClase, idLaboratorio, diaSemana, inicio, fin, ventana.PropiaDesde, null);
                if (HistorialCambios.Activo(conexion, transaccion))
                {
                    FotoFranja nueva = FotoDeFranja(conexion, transaccion, id);
                    AnotarFranja(conexion, transaccion, AccionDeCambio.Alta, id, null, nueva, ventana.PropiaDesde, nueva == null ? null
                        : nueva.Clase + ": " + nueva.Donde + (ventana.PropiaDesde.HasValue ? ", desde el " + HistorialCambios.Fecha(ventana.PropiaDesde.Value) : "") + "."
                          + EnLugarDe(quitadas));
                }

                ResultadoReemplazo resultado = new ResultadoReemplazo { FranjaId = id, Quitadas = quitadas };
                resultado.MinutosAsignados = Asignar(conexion, transaccion, ventana.Clase, MinutosEntre(inicio, fin),
                    NombreDia(diaSemana).ToLowerInvariant() + " de " + HistorialCambios.Horas(inicio, fin) + " en " + NombreDeLaboratorio(conexion, transaccion, idLaboratorio),
                    quien, out resultado.MinutosQueFaltan);
                transaccion.Commit();
                Log.Info("Franja " + id + " puesta" + (quitadas.Count > 0 ? " en lugar de " + quitadas.Count + " (" + string.Join(", ", quitadas.Select(q => q.FranjaId)) + ")" : "")
                    + (resultado.MinutosAsignados > 0 ? "; bajan " + resultado.MinutosAsignados + " min por asignar de la clase " + idClase : "") + ".");
                return resultado;
            }
        }

        /// <summary>
        /// "Cambiar horas..." con db/25: como CambiarFranjaDesde (desde una fecha
        /// si tiene historia y ConFecha; si no, en su sitio), pero lo que se cruza
        /// con OTRAS clases del mismo laboratorio se les quita y queda por asignar.
        /// Si la misma clase gana tiempo, lo que le faltaba baja. Todo o nada.
        /// </summary>
        public ResultadoReemplazo CambiarFranjaReemplazando(int idFranja, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime desde)
        {
            Permisos.Exigir("Editar una franja del horario (reemplazando)");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ExigirPorAsignar(conexion, transaccion);
                FranjaGuardada vieja = LeerFranjaGuardada(conexion, transaccion, idFranja);
                if (vieja == null)
                {
                    throw new InvalidOperationException("Esa franja ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.");
                }

                VentanaDeReemplazo ventana = Ventana(conexion, transaccion, idFranja, idClase, desde);
                if (ventana.Problema != null)
                {
                    throw new InvalidOperationException(ventana.Problema);
                }

                List<FranjaReemplazada> quitadas = QueSeReemplaza(conexion, transaccion, idFranja, ventana, idLaboratorio, diaSemana, inicio, fin);
                string conflicto = ValidarFranjaEn(conexion, transaccion, idFranja, idClase, idLaboratorio, diaSemana, inicio, fin,
                    ventana.PropiaDesde, ventana.PropiaHasta, quitadas.Select(q => q.FranjaId));
                if (conflicto != null)
                {
                    throw new InvalidOperationException(conflicto);
                }

                string quienEntra = TextoDeClase(ventana.Clase.Materia, ventana.Clase.Grupo, ventana.Clase.Profesor);
                int? quien = IdDeLaSesion(conexion, transaccion);
                Recortar(conexion, transaccion, quitadas, ventana, idLaboratorio, inicio, fin, quienEntra, quien);
                int nueva = CambiarFranjaDesdeEn(conexion, transaccion, idFranja, idClase, idLaboratorio, diaSemana, inicio, fin, desde,
                    quitadas.Count == 0 ? null : EnLugarDe(quitadas).Trim());

                // Lo que gana la clase (si cambia de clase, la nueva gana todas sus horas).
                int ganados = MinutosEntre(inicio, fin) - (vieja.ClaseId == idClase ? MinutosEntre(vieja.Inicio, vieja.Fin) : 0);
                ResultadoReemplazo resultado = new ResultadoReemplazo { FranjaId = nueva, Quitadas = quitadas };
                if (ganados > 0)
                {
                    resultado.MinutosAsignados = Asignar(conexion, transaccion, ventana.Clase, ganados,
                        NombreDia(diaSemana).ToLowerInvariant() + " de " + HistorialCambios.Horas(inicio, fin) + " en " + NombreDeLaboratorio(conexion, transaccion, idLaboratorio),
                        quien, out resultado.MinutosQueFaltan);
                }

                transaccion.Commit();
                Log.Info("Franja " + idFranja + " cambiada" + (quitadas.Count > 0 ? " en lugar de " + quitadas.Count + " (" + string.Join(", ", quitadas.Select(q => q.FranjaId)) + ")" : "")
                    + (resultado.MinutosAsignados > 0 ? "; bajan " + resultado.MinutosAsignados + " min por asignar de la clase " + idClase : "") + ".");
                return resultado;
            }
        }

        /// <summary>
        /// "Quitar horas..." con lo que queda por asignar (db/25): como
        /// QuitarFranjaDesde; con porAsignar, sus horas quedan en "Falta asignar"
        /// desde esa fecha. Sin db/25, porAsignar no hace nada (como antes).
        /// </summary>
        public void QuitarFranjaDesde(int idFranja, DateTime desde, bool porAsignar)
        {
            Permisos.Exigir("Borrar una franja del horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                FranjaGuardada franja = LeerFranjaGuardada(conexion, transaccion, idFranja);
                QuitarFranjaDesdeEn(conexion, transaccion, idFranja, desde, null);
                if (franja != null && porAsignar && Esquema.HorasPorAsignar(conexion, transaccion))
                {
                    DateTime hoy = Hoy(conexion, transaccion);
                    DateTime dia = VigenciaFranjas.ConFecha(conexion, transaccion) && desde.Date > hoy ? desde.Date : hoy;
                    PendienteDeQuitar(conexion, transaccion, franja, dia);
                }

                transaccion.Commit();
            }
        }

        /// <summary>BorrarFranja (como siempre) y, con porAsignar y db/25, sus horas en "Falta asignar" desde hoy.</summary>
        public void BorrarFranja(int idFranja, bool porAsignar)
        {
            Permisos.Exigir("Borrar una franja del horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                FranjaGuardada franja = LeerFranjaGuardada(conexion, transaccion, idFranja);
                bool conHistoria = false;
                if (franja != null)
                {
                    DateTime hoy;
                    HistoriaFranja historia = LeerHistoria(conexion, transaccion, franja, out hoy);
                    if (historia.ConFecha && historia.TieneHistoria)
                    {
                        throw new InvalidOperationException("Esta franja ya se dio antes: quitarla tiene que aplicar desde una fecha. "
                            + "Vuelve a quitarla y elige «Quitar desde».");
                    }

                    conHistoria = historia.TieneHistoria;
                }

                BorrarFranjaConHistorial(conexion, transaccion, idFranja, conHistoria);
                if (franja != null && porAsignar && Esquema.HorasPorAsignar(conexion, transaccion))
                {
                    PendienteDeQuitar(conexion, transaccion, franja, Hoy(conexion, transaccion));
                }

                transaccion.Commit();
            }
        }

        /// <summary>
        /// "Ya no hace falta": unas horas por asignar se dan por resueltas, con su
        /// renglon en el historial. InvalidOperationException si ya no estan pendientes.
        /// </summary>
        public void YaNoHaceFalta(int idPorAsignar)
        {
            Permisos.Exigir("Dar por resueltas unas horas por asignar");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ExigirPorAsignar(conexion, transaccion);
                Pendiente pendiente = LeerPendientes(conexion, transaccion, "p.idhoras_por_asignar = @Id",
                    cmd => cmd.Parameters.AddWithValue("@Id", idPorAsignar)).FirstOrDefault();
                if (pendiente == null)
                {
                    throw new InvalidOperationException("Esas horas ya no están por asignar (quizá se asignaron mientras tanto). Vuelve a abrir la lista.");
                }

                int? quien = IdDeLaSesion(conexion, transaccion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE horas_por_asignar SET estado = @Estado, resuelta_en = NOW(), resuelta_por = @Quien
                     WHERE idhoras_por_asignar = @Id AND estado = @Pendiente", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Estado", EstadoNoHaceFalta);
                    comando.Parameters.AddWithValue("@Quien", quien.HasValue ? (object)quien.Value : DBNull.Value);
                    comando.Parameters.AddWithValue("@Id", idPorAsignar);
                    comando.Parameters.AddWithValue("@Pendiente", EstadoPendiente);
                    comando.ExecuteNonQuery();
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.PorAsignar,
                    Accion = AccionDeCambio.NoHaceFalta,
                    Tabla = "horas_por_asignar",
                    RegistroId = idPorAsignar,
                    LaboratorioId = pendiente.LaboratorioId,
                    Fecha = pendiente.Desde,
                    Descripcion = pendiente.Clase + ": ya no hace falta asignar " + Duracion(pendiente.Minutos) + " (salieron del " + pendiente.Donde + ").",
                    Antes = HistorialCambios.Datos("Faltan", Duracion(pendiente.Minutos), "Estado", "Por asignar"),
                    Despues = HistorialCambios.Datos("Faltan", Duracion(0), "Estado", "Ya no hace falta")
                });
                transaccion.Commit();
                Log.Info("Horas por asignar " + idPorAsignar + " (clase " + pendiente.ClaseId + ", " + pendiente.Minutos + " min): ya no hacen falta.");
            }
        }

        // ---------------------------------------------------------------------
        // Por dentro
        // ---------------------------------------------------------------------

        /// <summary>Desde y hasta cuando valdrian las horas que se ponen, y desde cuando se recorta lo de las demas.</summary>
        private sealed class VentanaDeReemplazo
        {
            public ClaseInfo Clase;
            public bool ConVigencia;
            public bool ConFecha;
            public DateTime Hoy;
            /// <summary>Los dias en que se darian: la vigencia de su clase recortada a la propia (como ValidarFranjaEn).</summary>
            public DateTime Desde;
            public DateTime Hasta;
            /// <summary>La vigencia propia de la franja (null = la de su clase), como CrearFranja o la que se cambia.</summary>
            public DateTime? PropiaDesde;
            public DateTime? PropiaHasta;
            /// <summary>Desde cuando se recorta a las demas: la fecha elegida con ConFecha; si no, hoy (en su sitio).</summary>
            public DateTime Corte;
            /// <summary>Lo que no deja seguir (clase que no existe, fecha pasada...).</summary>
            public string Problema;
        }

        private static VentanaDeReemplazo Ventana(MySqlConnection conexion, MySqlTransaction transaccion, int? franjaQueCambia, int idClase, DateTime? desde)
        {
            VentanaDeReemplazo v = new VentanaDeReemplazo();
            v.Hoy = Hoy(conexion, transaccion);
            v.ConVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            v.ConFecha = VigenciaFranjas.ConFecha(conexion, transaccion);
            v.Clase = LeerClaseEn(conexion, transaccion, idClase);
            if (v.Clase == null)
            {
                v.Problema = "Esa clase ya no existe (quizá se quitó mientras tanto). Actualiza Horarios y vuelve a intentarlo.";
                return v;
            }

            if (franjaQueCambia == null && desde.HasValue && desde.Value.Date < v.Hoy)
            {
                v.Problema = "La franja no puede empezar el " + desde.Value.ToString("dd/MM/yyyy") + ": ese día ya pasó. Elige hoy ("
                    + v.Hoy.ToString("dd/MM/yyyy") + ") o una fecha posterior.";
                return v;
            }

            if (franjaQueCambia.HasValue)
            {
                // La que se cambia: con ConFecha, desde la fecha elegida hasta donde llegaba; si no, con la vigencia que ya tiene.
                FranjaGuardada vieja = LeerFranjaGuardada(conexion, transaccion, franjaQueCambia.Value);
                v.PropiaDesde = v.ConFecha && desde.HasValue ? desde.Value.Date : vieja == null ? (DateTime?)null : vieja.FranjaDesde;
                if (v.ConFecha && vieja != null && vieja.FranjaDesde.HasValue && v.PropiaDesde.HasValue && vieja.FranjaDesde.Value > v.PropiaDesde.Value)
                {
                    v.PropiaDesde = vieja.FranjaDesde;
                }

                v.PropiaHasta = vieja == null ? (DateTime?)null : vieja.FranjaHasta;
            }
            else if (v.ConFecha)
            {
                // Como CrearFranja: desde la fecha elegida (sin ella, hoy) si la clase ya empezo o empieza antes.
                DateTime? dia = desde.HasValue ? desde.Value.Date : (v.Clase.VigenteDesde.Date < v.Hoy ? v.Hoy : (DateTime?)null);
                if (dia.HasValue && dia.Value > v.Clase.VigenteDesde.Date)
                {
                    v.PropiaDesde = dia;
                }
            }

            v.Desde = v.Clase.VigenteDesde.Date;
            v.Hasta = v.Clase.VigenteHasta.Date;
            if (v.ConVigencia && v.PropiaDesde.HasValue && v.PropiaDesde.Value > v.Desde)
            {
                v.Desde = v.PropiaDesde.Value;
            }

            if (v.ConVigencia && v.PropiaHasta.HasValue && v.PropiaHasta.Value < v.Hasta)
            {
                v.Hasta = v.PropiaHasta.Value;
            }

            v.Corte = v.ConFecha && desde.HasValue && desde.Value.Date > v.Hoy ? desde.Value.Date : v.Hoy;
            return v;
        }

        /// <summary>
        /// Las franjas de OTRAS clases activas del mismo laboratorio que se cruzan
        /// con esas horas algun dia en que se darian las dos (como ValidarFranjaEn)
        /// y que aun se dan desde el corte: las que ya terminaron no se tocan (si
        /// chocan, siguen chocando, como antes).
        /// </summary>
        private static List<FranjaReemplazada> QueSeReemplaza(MySqlConnection conexion, MySqlTransaction transaccion, int? excluir, VentanaDeReemplazo v,
            int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            List<FranjaReemplazada> lista = new List<FranjaReemplazada>();
            if (v.Clase == null || fin <= inicio || v.Desde > v.Hasta)
            {
                return lista;
            }

            string cruce = "c.vigente_desde <= @Hasta AND c.vigente_hasta >= @Desde";
            if (v.ConVigencia)
            {
                string desdeComun = "GREATEST(DATE(@Desde), " + VigenciaFranjas.Desde(true) + ")";
                string hastaComun = "LEAST(DATE(@Hasta), " + VigenciaFranjas.Hasta(true) + ")";
                cruce = "DATE(DATE_ADD(" + desdeComun + ", INTERVAL MOD(@Dia + 6 - WEEKDAY(" + desdeComun + "), 7) DAY)) <= DATE(" + hastaComun + ")";
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT h.idhorarios, h.clases_idclases, h.hora_inicio, h.hora_fin, l.nombre_laboratorio, m.nombre_materia, g.nombre_grupo,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor
                FROM horarios h
                INNER JOIN clases c       ON c.idclases = h.clases_idclases
                INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                LEFT JOIN usuarios u      ON u.idusuarios = c.usuarios_idusuarios
                WHERE h.laboratorios_idlaboratorios = @Lab AND h.dia_semana = @Dia
                  AND h.idhorarios <> @Excluir AND c.idclases <> @Clase AND c.activa = 1
                  AND h.hora_inicio < @Fin AND h.hora_fin > @Inicio
                  AND " + cruce + @"
                  AND " + VigenciaFranjas.Hasta(v.ConVigencia) + @" >= @Corte
                ORDER BY h.hora_inicio, h.idhorarios", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Excluir", excluir ?? 0);
                comando.Parameters.AddWithValue("@Clase", v.Clase.Id);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Desde", v.Desde);
                comando.Parameters.AddWithValue("@Hasta", v.Hasta);
                comando.Parameters.AddWithValue("@Corte", v.Corte);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        TimeSpan suInicio = LeerHora(reader["hora_inicio"]) ?? TimeSpan.Zero;
                        TimeSpan suFin = LeerHora(reader["hora_fin"]) ?? TimeSpan.Zero;
                        TramoHorario cruza = Cruce(suInicio, suFin, inicio, fin);
                        lista.Add(new FranjaReemplazada
                        {
                            FranjaId = Convert.ToInt32(reader["idhorarios"], CultureInfo.InvariantCulture),
                            ClaseId = Convert.ToInt32(reader["clases_idclases"], CultureInfo.InvariantCulture),
                            Materia = Texto(reader, "nombre_materia"),
                            Grupo = Texto(reader, "nombre_grupo"),
                            Profesor = Texto(reader, "profesor").Trim(),
                            LaboratorioId = idLaboratorio,
                            Laboratorio = Texto(reader, "nombre_laboratorio"),
                            DiaSemana = diaSemana,
                            Inicio = suInicio,
                            Fin = suFin,
                            QuitaInicio = cruza.Inicio,
                            QuitaFin = cruza.Fin
                        });
                    }
                }
            }

            // Sin ConFecha, el recorte se hace en su sitio: con historia, cambia tambien lo pasado.
            if (!v.ConFecha)
            {
                foreach (FranjaReemplazada q in lista)
                {
                    FranjaGuardada guardada = LeerFranjaGuardada(conexion, transaccion, q.FranjaId);
                    DateTime hoy;
                    q.CambiaElPasado = guardada != null && LeerHistoria(conexion, transaccion, guardada, out hoy).TieneHistoria;
                }
            }

            return lista;
        }

        /// <summary>
        /// Lo que no deja quitarle a alguna de las quitadas lo que se cruza
        /// (Recortar), sin escribir nada, con lo que Recortar comprueba y en su
        /// orden: el primer dia en que puede aplicar (desde una fecha), sus
        /// recortes que ya no caben y lo que le quedaria (uno o dos trozos), que
        /// se valida como en ValidarFranja: un choque que ese trozo ya tenia
        /// tambien impide partirla, porque se guarda como franja. Null si se
        /// puede. La vista previa (PlanDeReemplazo) lo dice antes de pedir
        /// confirmacion, y Recortar con el mismo texto antes de escribir.
        /// </summary>
        private static string ProblemaAlRecortar(MySqlConnection conexion, MySqlTransaction transaccion, List<FranjaReemplazada> quitadas, VentanaDeReemplazo v,
            int idLaboratorio, TimeSpan inicio, TimeSpan fin)
        {
            foreach (FranjaReemplazada q in quitadas)
            {
                FranjaGuardada franja = LeerFranjaGuardada(conexion, transaccion, q.FranjaId);
                if (franja == null)
                {
                    continue;
                }

                DateTime hoy;
                HistoriaFranja historia = LeerHistoria(conexion, transaccion, franja, out hoy);
                bool enSuSitio = EnSuSitio(historia, franja, hoy, v.Corte);
                List<TramoHorario> restos = Restos(q.Inicio, q.Fin, inicio, fin);
                string cual = (restos.Count == 0 ? "No se puede reemplazar " : "No se puede reemplazar solo una parte de ") + q.Materia + " - " + q.Grupo + ": ";
                string problema = enSuSitio ? null : ProblemaDePrimerDia(historia, hoy, v.Corte, restos.Count == 0 ? "se puede quitar" : "el cambio puede aplicar");
                if (problema != null)
                {
                    return cual + Minuscula(problema);
                }

                if (restos.Count == 0)
                {
                    // Entera (QuitarFranjaDesdeEn): desde una fecha, un resto de sus
                    // recortes con registros no deja; en su sitio, ese se queda.
                    if (!enSuSitio)
                    {
                        List<CanceladaDeFranja> suyas = CanceladasDesde(conexion, transaccion, q.FranjaId, v.Corte);
                        problema = ProblemaDeRestosUsados(suyas, suyas.ToDictionary(x => x.Id, x => LeerRestos(conexion, transaccion, x.Id)), v.Corte);
                    }

                    if (problema != null)
                    {
                        return cual + Minuscula(problema);
                    }

                    continue;
                }

                // Partida (CambiarFranjaDesdeEn con el primer trozo): sus recortes le
                // tienen que caber al trozo de antes, que se queda con sus cancelaciones.
                List<DateTime> recortes = RecortesQueNoCaben(conexion, transaccion, CanceladasDesde(conexion, transaccion, q.FranjaId, enSuSitio ? hoy : v.Corte),
                    franja, q.ClaseId, idLaboratorio, q.DiaSemana, restos[0].Inicio, restos[0].Fin, !enSuSitio);
                if (recortes.Count > 0)
                {
                    return cual + Minuscula(TextoDeRecortesQueNoCaben(recortes, !enSuSitio));
                }

                // Cada trozo, con la vigencia que tendria (como la nueva de
                // CambiarFranjaDesdeEn) y sin la franja de ahora, que ya no estara.
                DateTime? suDesde = enSuSitio ? franja.FranjaDesde
                    : franja.FranjaDesde.HasValue && franja.FranjaDesde.Value > v.Corte ? franja.FranjaDesde : v.Corte;
                foreach (TramoHorario trozo in restos)
                {
                    string choque = ValidarFranjaEn(conexion, transaccion, q.FranjaId, q.ClaseId, idLaboratorio, q.DiaSemana, trozo.Inicio, trozo.Fin,
                        suDesde, franja.FranjaHasta);
                    if (choque != null)
                    {
                        return TextoDeTrozo(q, trozo, choque);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// "No se puede reemplazar solo una parte de Redes - 5A: lo que le
        /// quedaría (13:00–14:00) se solapa con ... Elige sus horas completas
        /// (12:00–14:00) para reemplazarla entera, o resuelve antes ese choque."
        /// </summary>
        private static string TextoDeTrozo(FranjaReemplazada q, TramoHorario trozo, string choque)
        {
            return "No se puede reemplazar solo una parte de " + q.Materia + " - " + q.Grupo + ": lo que le quedaría ("
                + Hora(trozo.Inicio) + "–" + Hora(trozo.Fin) + ") " + Minuscula(choque)
                + (choque.StartsWith("Se solapa con", StringComparison.Ordinal)
                    ? " Elige sus horas completas (" + Hora(q.Inicio) + "–" + Hora(q.Fin) + ") para reemplazarla entera, o resuelve antes ese choque."
                    : "");
        }

        /// <summary>El texto con su primera letra en minuscula, para seguir una frase ("...: se solapa con...").</summary>
        private static string Minuscula(string texto)
        {
            return string.IsNullOrEmpty(texto) ? "" : char.ToLowerInvariant(texto[0]) + texto.Substring(1);
        }

        /// <summary>
        /// Al partir una franja en dos (Recortar), las cancelaciones del trozo de
        /// antes de desde en adelante (las de la franja, que CambiarFranjaDesdeEn
        /// le dejo o le paso) valen tambien para el de despues: ese dia no se da
        /// ninguna de las dos partes. Si no, la de despues saldria ese dia como
        /// clase (y en Faltas en rojo). Con el mismo motivo, quien y cuando, como
        /// el segundo trozo de una extra que se parte; un "No tomar en cuenta" sigue
        /// siendo un solo lote. Sus restos caben en el de antes (ProblemaAlRecortar)
        /// y se quedan con el. Devuelve sus fechas.
        /// </summary>
        private static List<DateTime> CopiarCanceladas(MySqlConnection conexion, MySqlTransaction transaccion, int deFranja, int aFranja, DateTime desde)
        {
            List<DateTime> fechas = CanceladasDesde(conexion, transaccion, deFranja, desde).Select(x => x.Fecha).ToList();
            if (fechas.Count == 0)
            {
                return fechas;
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO horario_excepciones (tipo, fecha, horarios_idhorarios, motivo, creada_por, creada_en)
                SELECT 'cancelada', x.fecha, @A, x.motivo, x.creada_por, x.creada_en
                FROM horario_excepciones x
                WHERE x.tipo = 'cancelada' AND x.horarios_idhorarios = @De AND x.fecha >= @Desde", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@A", aFranja);
                comando.Parameters.AddWithValue("@De", deFranja);
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.ExecuteNonQuery();
            }

            Log.Info("Franja " + aFranja + " (lo que le queda a la " + deFranja + "): con sus " + fechas.Count + " cancelaciones de "
                + desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " en adelante.");
            return fechas;
        }

        /// <summary>
        /// A cada una le quita lo que se cruza desde el corte (QuitarFranjaDesdeEn
        /// o CambiarFranjaDesdeEn: con ConFecha y historia, sin reescribir lo
        /// pasado; si no, en su sitio), y eso queda por asignar ('desplazada').
        /// Antes de escribir nada, lo que no lo dejaria (ProblemaAlRecortar, lo
        /// mismo que dijo la vista previa). Si se parte en dos, el trozo de despues
        /// se lleva tambien las cancelaciones del de antes (CopiarCanceladas).
        /// </summary>
        private static void Recortar(MySqlConnection conexion, MySqlTransaction transaccion, List<FranjaReemplazada> quitadas, VentanaDeReemplazo v,
            int idLaboratorio, TimeSpan inicio, TimeSpan fin, string quienEntra, int? quien)
        {
            string problema = ProblemaAlRecortar(conexion, transaccion, quitadas, v, idLaboratorio, inicio, fin);
            if (problema != null)
            {
                throw new InvalidOperationException(problema);
            }

            foreach (FranjaReemplazada q in quitadas)
            {
                string porque = "La reemplaza " + quienEntra + " de " + HistorialCambios.Horas(inicio, fin) + "; "
                    + Duracion(q.Minutos) + " quedan en «Falta asignar».";
                List<TramoHorario> restos = Restos(q.Inicio, q.Fin, inicio, fin);
                if (restos.Count == 0)
                {
                    QuitarFranjaDesdeEn(conexion, transaccion, q.FranjaId, v.Corte, porque);
                }
                else
                {
                    int primera = CambiarFranjaDesdeEn(conexion, transaccion, q.FranjaId, q.ClaseId, idLaboratorio, q.DiaSemana, restos[0].Inicio, restos[0].Fin,
                        v.Corte, porque);
                    if (restos.Count > 1)
                    {
                        // El trozo de despues, con la misma vigencia que el de antes y,
                        // los dias en que el de antes esta cancelado, cancelado tambien.
                        FranjaGuardada antes = LeerFranjaGuardada(conexion, transaccion, primera);
                        DateTime? suDesde = antes == null ? (DateTime?)null : antes.FranjaDesde;
                        DateTime? suHasta = antes == null ? (DateTime?)null : antes.FranjaHasta;
                        string conflicto = ValidarFranjaEn(conexion, transaccion, null, q.ClaseId, idLaboratorio, q.DiaSemana, restos[1].Inicio, restos[1].Fin, suDesde, suHasta,
                            null, primera);
                        if (conflicto != null)
                        {
                            throw new InvalidOperationException(TextoDeTrozo(q, restos[1], conflicto));
                        }

                        int segunda = InsertarFranja(conexion, transaccion, q.ClaseId, idLaboratorio, q.DiaSemana, restos[1].Inicio, restos[1].Fin, suDesde, suHasta);
                        List<DateTime> canceladas = CopiarCanceladas(conexion, transaccion, primera, segunda, v.Corte);
                        if (HistorialCambios.Activo(conexion, transaccion))
                        {
                            FotoFranja foto = FotoDeFranja(conexion, transaccion, segunda);
                            AnotarFranja(conexion, transaccion, AccionDeCambio.Alta, segunda, null, foto, suDesde, foto == null ? null
                                : foto.Clase + ": " + foto.Donde + " (lo que le queda después de lo que se le quitó"
                                  + (canceladas.Count == 1 ? "; también con su cancelación del " + ListaDeFechas(canceladas)
                                     : canceladas.Count > 1 ? "; también con sus " + canceladas.Count + " cancelaciones de esa fecha en adelante" : "")
                                  + "). " + porque);
                        }
                    }
                }

                InsertarPendiente(conexion, transaccion, q.ClaseId, q.Minutos, idLaboratorio, q.DiaSemana, q.QuitaInicio, q.QuitaFin, v.Corte,
                    MotivoDesplazada, quien, TextoDeClase(q.Materia, q.Grupo, q.Profesor), "las desplazó " + quienEntra);
            }
        }

        /// <summary>" En lugar de Redes - 5A (Ana Ruiz), lunes de 08:00 a 10:00." o "".</summary>
        private static string EnLugarDe(List<FranjaReemplazada> quitadas)
        {
            if (quitadas.Count == 0)
            {
                return "";
            }

            return " En lugar de " + HistorialCambios.Enumerar(quitadas.Select(q => TextoDeClase(q.Materia, q.Grupo, q.Profesor) + ", "
                + NombreDia(q.DiaSemana).ToLowerInvariant() + " de " + HistorialCambios.Horas(q.QuitaInicio, q.QuitaFin)).ToList()) + ".";
        }

        /// <summary>Las horas de una franja que se quita para darlas en otro horario ('quitada').</summary>
        private static void PendienteDeQuitar(MySqlConnection conexion, MySqlTransaction transaccion, FranjaGuardada franja, DateTime desde)
        {
            ClaseInfo clase = LeerClaseEn(conexion, transaccion, franja.ClaseId);
            InsertarPendiente(conexion, transaccion, franja.ClaseId, MinutosEntre(franja.Inicio, franja.Fin), franja.LaboratorioId, franja.DiaSemana,
                franja.Inicio, franja.Fin, desde, MotivoQuitada, IdDeLaSesion(conexion, transaccion),
                clase == null ? "La clase " + franja.ClaseId : TextoDeClase(clase.Materia, clase.Grupo, clase.Profesor),
                "se quitaron con «Quitar horas» para darlas en otro horario");
        }

        private static void InsertarPendiente(MySqlConnection conexion, MySqlTransaction transaccion, int idClase, int minutos, int idLaboratorio, int diaSemana,
            TimeSpan inicio, TimeSpan fin, DateTime desde, string motivo, int? quien, string clase, string porque)
        {
            if (minutos <= 0)
            {
                return;
            }

            int id;
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO horas_por_asignar (clases_idclases, minutos, minutos_originales, laboratorio_id, dia_semana, hora_inicio, hora_fin, desde,
                                               motivo, creada_en, creada_por, estado)
                VALUES (@Clase, @Minutos, @Minutos, @Lab, @Dia, @Inicio, @Fin, @Desde, @Motivo, NOW(), @Quien, @Pendiente);
                SELECT LAST_INSERT_ID();", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Minutos", minutos);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Motivo", motivo);
                comando.Parameters.AddWithValue("@Quien", quien.HasValue ? (object)quien.Value : DBNull.Value);
                comando.Parameters.AddWithValue("@Pendiente", EstadoPendiente);
                id = Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            string donde = NombreDia(diaSemana).ToLowerInvariant() + " de " + HistorialCambios.Horas(inicio, fin) + " en " + NombreDeLaboratorio(conexion, transaccion, idLaboratorio);
            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = TipoDeCambio.PorAsignar,
                Accion = AccionDeCambio.Alta,
                Tabla = "horas_por_asignar",
                RegistroId = id,
                LaboratorioId = idLaboratorio,
                Fecha = desde.Date,
                Descripcion = clase + ": " + Duracion(minutos) + " por asignar desde el " + HistorialCambios.Fecha(desde) + " (salieron del " + donde + "; " + porque + ").",
                Despues = HistorialCambios.Datos(
                    "Clase", clase,
                    "Faltan", Duracion(minutos),
                    "Salieron de", donde,
                    "Desde", HistorialCambios.Fecha(desde),
                    "Motivo", motivo == MotivoQuitada ? "Se quitaron para darlas en otro horario" : "Otra clase se puso en su lugar")
            });
        }

        /// <summary>
        /// Lo que le faltaba a una clase de una plantilla pasa, al aplicarla, a
        /// "Falta asignar" de la clase que se creo (CicloPreliminarDatos.Aplicar).
        /// Sin un renglon por cada una en el historial: Aplicar deja uno solo con
        /// todo el horario. Quien llama sabe que hay db/25.
        /// </summary>
        internal static void InsertarPendienteDePlantilla(MySqlConnection conexion, MySqlTransaction transaccion, int idClase, int minutos, int minutosOriginales,
            int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin, DateTime desde, string motivo, int quien)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO horas_por_asignar (clases_idclases, minutos, minutos_originales, laboratorio_id, dia_semana, hora_inicio, hora_fin, desde,
                                               motivo, creada_en, creada_por, estado)
                VALUES (@Clase, @Minutos, @Originales, @Lab, @Dia, @Inicio, @Fin, @Desde, @Motivo, NOW(), @Quien, @Pendiente)", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Minutos", minutos);
                comando.Parameters.AddWithValue("@Originales", minutosOriginales);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Motivo", motivo);
                comando.Parameters.AddWithValue("@Quien", quien > 0 ? (object)quien : DBNull.Value);
                comando.Parameters.AddWithValue("@Pendiente", EstadoPendiente);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>Un renglon pendiente (para resolverlo, con lo que dice el historial).</summary>
        private sealed class Pendiente
        {
            public int Id;
            public int ClaseId;
            public int Minutos;
            public int? LaboratorioId;
            public DateTime Desde;
            public string Clase = "";
            public string Donde = "";
        }

        private static List<Pendiente> LeerPendientes(MySqlConnection conexion, MySqlTransaction transaccion, string condicion, Action<MySqlCommand> parametros)
        {
            List<Pendiente> lista = new List<Pendiente>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT p.idhoras_por_asignar, p.clases_idclases, p.minutos, p.laboratorio_id, p.desde, p.dia_semana, p.hora_inicio, p.hora_fin,
                       l.nombre_laboratorio, m.nombre_materia, g.nombre_grupo, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor
                FROM horas_por_asignar p
                LEFT JOIN clases c       ON c.idclases = p.clases_idclases
                LEFT JOIN materias m     ON m.id_materia = c.materias_id_materia
                LEFT JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                LEFT JOIN usuarios u     ON u.idusuarios = c.usuarios_idusuarios
                LEFT JOIN laboratorios l ON l.idlaboratorios = p.laboratorio_id
                WHERE p.estado = @Pendiente AND p.minutos > 0 AND " + condicion + @"
                ORDER BY p.creada_en, p.idhoras_por_asignar
                FOR UPDATE", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Pendiente", EstadoPendiente);
                parametros(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int dia = Convert.ToInt32(reader["dia_semana"], CultureInfo.InvariantCulture);
                        lista.Add(new Pendiente
                        {
                            Id = Convert.ToInt32(reader["idhoras_por_asignar"], CultureInfo.InvariantCulture),
                            ClaseId = Convert.ToInt32(reader["clases_idclases"], CultureInfo.InvariantCulture),
                            Minutos = Convert.ToInt32(reader["minutos"], CultureInfo.InvariantCulture),
                            LaboratorioId = reader["laboratorio_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["laboratorio_id"], CultureInfo.InvariantCulture),
                            Desde = Convert.ToDateTime(reader["desde"], CultureInfo.InvariantCulture).Date,
                            Clase = TextoDeClase(Texto(reader, "nombre_materia"), Texto(reader, "nombre_grupo"), Texto(reader, "profesor")),
                            Donde = NombreDia(dia).ToLowerInvariant() + " de " + HistorialCambios.Horas(LeerHora(reader["hora_inicio"]) ?? TimeSpan.Zero,
                                LeerHora(reader["hora_fin"]) ?? TimeSpan.Zero) + (Texto(reader, "nombre_laboratorio").Length > 0 ? " en " + Texto(reader, "nombre_laboratorio") : "")
                        });
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Las horas nuevas de una clase bajan lo que le faltaba, de lo mas
        /// antiguo a lo mas nuevo: lo que llega a cero queda 'asignada'. Devuelve
        /// cuantos minutos bajaron; queFaltan, lo que le sigue faltando. Sin db/25, 0.
        /// </summary>
        private static int Asignar(MySqlConnection conexion, MySqlTransaction transaccion, ClaseInfo clase, int minutos, string donde, int? quien, out int queFaltan)
        {
            queFaltan = 0;
            if (clase == null || minutos <= 0 || !Esquema.HorasPorAsignar(conexion, transaccion))
            {
                return 0;
            }

            List<Pendiente> pendientes = LeerPendientes(conexion, transaccion, "p.clases_idclases = @Clase", cmd => cmd.Parameters.AddWithValue("@Clase", clase.Id));
            int quedan = minutos;
            int bajaron = 0;
            foreach (Pendiente p in pendientes)
            {
                if (quedan <= 0)
                {
                    queFaltan += p.Minutos;
                    continue;
                }

                int baja = Math.Min(quedan, p.Minutos);
                int resto = p.Minutos - baja;
                quedan -= baja;
                bajaron += baja;
                queFaltan += resto;
                using (MySqlCommand comando = new MySqlCommand(resto == 0
                    ? "UPDATE horas_por_asignar SET minutos = 0, estado = @Asignada, resuelta_en = NOW(), resuelta_por = @Quien WHERE idhoras_por_asignar = @Id"
                    : "UPDATE horas_por_asignar SET minutos = @Resto WHERE idhoras_por_asignar = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Asignada", EstadoAsignada);
                    comando.Parameters.AddWithValue("@Quien", quien.HasValue ? (object)quien.Value : DBNull.Value);
                    comando.Parameters.AddWithValue("@Resto", resto);
                    comando.Parameters.AddWithValue("@Id", p.Id);
                    comando.ExecuteNonQuery();
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.PorAsignar,
                    Accion = AccionDeCambio.Asignar,
                    Tabla = "horas_por_asignar",
                    RegistroId = p.Id,
                    LaboratorioId = p.LaboratorioId,
                    Fecha = p.Desde,
                    Descripcion = p.Clase + ": se le pusieron " + Duracion(baja) + " (" + donde + ") de las que salieron del " + p.Donde
                        + (resto == 0 ? "; ya no falta nada de ahí." : "; faltan " + Duracion(resto) + "."),
                    Antes = HistorialCambios.Datos("Faltan", Duracion(p.Minutos), "Estado", "Por asignar"),
                    Despues = HistorialCambios.Datos("Faltan", Duracion(resto), "Estado", resto == 0 ? "Asignada" : "Por asignar")
                });
            }

            return bajaron;
        }

        /// <summary>El id de quien tiene la sesion abierta (como HistorialCambios.Anotar), o null.</summary>
        private static int? IdDeLaSesion(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            string usuario = Datos_User.getUser();
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }

            using (MySqlCommand comando = new MySqlCommand("SELECT idusuarios FROM usuarios WHERE numero_identificador = @Usuario LIMIT 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Usuario", usuario.Trim());
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Sin db/25 no se reemplaza nada (la pantalla no deberia llegar aqui).</summary>
        private static void ExigirPorAsignar(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            if (!Esquema.HorasPorAsignar(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base no tiene lo de las horas por asignar (db/25_horas_por_asignar.sql): pide a quien administra el servidor que la aplique.");
            }
        }
    }
}
