using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Lo que hara "Dar de baja el horario actual", para preguntarlo antes. Solo se lee.</summary>
    public class InfoDeBaja
    {
        public DateTime Hoy;
        /// <summary>Las que empezaron antes de hoy: terminan ayer.</summary>
        public int Terminan;
        /// <summary>Las que empezaron hoy sin nada registrado: se quitan.</summary>
        public int SeQuitan;
        /// <summary>Las que empezaron hoy y ya tienen algo registrado: quedan terminadas, sin dias propios (ClaseInfo.SinDiasPropios).</summary>
        public int ConRegistrosDeHoy;
        /// <summary>Las activas que empiezan despues de hoy: no se tocan, y siguen empezando ese dia.</summary>
        public int EmpiezanDespues;
        /// <summary>Las sesiones extra de hoy en adelante de esas clases que nadie abrio: ya no se darian y se quitan.</summary>
        public int ExtrasQueSeQuitan;
        /// <summary>Las clases que empiezan despues y siguen canceladas un dia en que se quita la extra que ocupaba sus horas.</summary>
        public List<ExtraFuera> SiguenCanceladas = new List<ExtraFuera>();
        /// <summary>El nombre que llevara la copia del horario (plantilla guardada).</summary>
        public string NombreCopia = "";
        /// <summary>Por que no se puede, o null si se puede.</summary>
        public string Motivo;
    }

    public class ResultadoBaja
    {
        public int Terminan;
        public int SeQuitan;
        public int ConRegistrosDeHoy;
        public int ExtrasQueSeQuitan;
        /// <summary>La copia que se guardo como plantilla; null si no habia clases que copiar.</summary>
        public string NombreCopia;
        /// <summary>Los horarios aplicados que no llegaron a correr y volvieron a ser plantillas guardadas.</summary>
        public List<string> VueltasAPlantilla = new List<string>();
    }

    /// <summary>
    /// "Dar de baja el horario actual" (pedido del usuario, 2026-11-04): deja la escuela sin
    /// clases desde ahora, sin aplicar otro horario. Para sustituirlo no hace falta: "Ahora mismo"
    /// hace lo mismo con el horario de hoy, en el mismo paso (AplicarConFechas, con TerminarYQuitar).
    ///
    /// Segun cada clase que vale hoy:
    /// - empezo antes de hoy: termina ayer. Lo de hoy se queda y cuenta como con "Ahora mismo"
    ///   (AsistenciaDatos, HorariosDatos.EmparejarSesiones);
    /// - empezo hoy, sin nada registrado en el servidor: se borra, como en Deshacer;
    /// - empezo hoy con algo registrado: tambien termina ayer, y se queda sin dias propios
    ///   (vigente_hasta = vispera de su vigente_desde: ClaseInfo.SinDiasPropios). No se borra
    ///   (las sesiones se quedarian sin enlace y las asistencias dadas son documento oficial) ni
    ///   se desactiva (desactivada, desaparece de Mis grupos y Estadisticas, y su historia con
    ///   ella). Lo de hoy cuenta igual que lo de una clase que termino ayer: en la clase de su
    ///   misma terna que se quede con ese dia o, si no sigue, en esta
    ///   (AsistenciaDatos.OtraSeQuedaElDia y DadasQueCuentan).
    /// Nunca borra sesiones, bitacoras ni asistencias. Las sesiones extra de hoy en adelante que
    /// nadie abrio se quitan: ya no se darian. Un horario aplicado del que no quedo ninguna clase
    /// vuelve a ser plantilla; a los demas que cambian se les pone su fecha de fin real. Antes de
    /// tocar nada guarda una copia del horario como plantilla, con lo que le falta asignar, para
    /// poder volver a aplicarlo. Todo en una transaccion, con un solo renglon en el historial.
    /// </summary>
    internal partial class CicloPreliminarDatos
    {
        /// <summary>SQL: la clase (alias c) vale @Fin (aqui, hoy): todas las que la baja puede cambiar.</summary>
        private const string ValeEseDia = "c.vigente_desde <= @Fin AND c.vigente_hasta >= @Fin";

        private sealed class PlanDeBaja
        {
            public DateTime Hoy;
            public List<int> Terminan = new List<int>();
            public int TerminanActivas;
            public List<int> SeQuitan = new List<int>();
            public List<int> ConRegistrosDeHoy = new List<int>();
            public int EmpiezanDespues;
            public int ExtrasQueSeQuitan;
            public string Motivo;

            /// <summary>Las de hoy, con registros o sin ellos.</summary>
            public List<int> DeHoy
            {
                get { return SeQuitan.Concat(ConRegistrosDeHoy).ToList(); }
            }
        }

        /// <summary>Lo que hara, y por que no se puede (Motivo). Solo lee.</summary>
        public InfoDeBaja LeerParaDarDeBaja()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                PlanDeBaja plan = LeerPlanDeBaja(conexion, null, false);
                return new InfoDeBaja
                {
                    Hoy = plan.Hoy,
                    Terminan = plan.TerminanActivas,
                    SeQuitan = plan.SeQuitan.Count,
                    ConRegistrosDeHoy = plan.ConRegistrosDeHoy.Count,
                    EmpiezanDespues = plan.EmpiezanDespues,
                    ExtrasQueSeQuitan = plan.ExtrasQueSeQuitan,
                    SiguenCanceladas = ClasesQueSiguenCanceladas(conexion, null, plan.Hoy),
                    NombreCopia = NombreLibre(conexion, null, NombreDeLaCopia(plan.Hoy), EstadoConfiguracion.Guardada),
                    Motivo = plan.Motivo
                };
            }
        }

        private static string NombreDeLaCopia(DateTime hoy)
        {
            return "Horario antes de la baja del " + hoy.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Da de baja el horario que vale hoy. ErrorDeDatos si no se puede, con su motivo. Solo la
        /// administracion (Permisos.Exigir).
        /// </summary>
        public ResultadoBaja DarDeBajaElHorarioActual(int idAdministrador)
        {
            Permisos.Exigir("Dar de baja el horario actual");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ExigirBaseParaAplicar(conexion, transaccion);
                PlanDeBaja plan = LeerPlanDeBaja(conexion, transaccion, true);
                if (plan.Motivo != null)
                {
                    throw new ErrorDeDatos(plan.Motivo);
                }

                ResultadoBaja resultado = new ResultadoBaja
                {
                    Terminan = plan.TerminanActivas,
                    SeQuitan = plan.SeQuitan.Count,
                    ConRegistrosDeHoy = plan.ConRegistrosDeHoy.Count,
                    ExtrasQueSeQuitan = plan.ExtrasQueSeQuitan
                };

                // Lo de antes, mientras sigue: la baja no deja otra forma de saberlo.
                object finDeAntes = ValorEnLaBase(conexion, transaccion, @"
                    SELECT c.vigente_hasta FROM clases c
                     WHERE c.activa = 1 AND @Hoy BETWEEN c.vigente_desde AND c.vigente_hasta
                     GROUP BY c.vigente_hasta ORDER BY COUNT(*) DESC, c.vigente_hasta DESC LIMIT 1",
                    c => c.Parameters.AddWithValue("@Hoy", plan.Hoy));
                List<KeyValuePair<int, string>> aplicadas = AplicadasQueCambian(conexion, transaccion, plan.Hoy, plan.Terminan.Concat(plan.DeHoy).ToList());

                // a) La copia, antes de tocar nada: las clases activas como estan hoy.
                string nombreCopia = NombreLibre(conexion, transaccion, NombreDeLaCopia(plan.Hoy), EstadoConfiguracion.Guardada);
                int idCopia = InsertarCiclo(conexion, transaccion, nombreCopia, EstadoConfiguracion.Guardada, plan.Hoy, null, idAdministrador);
                if (CopiarClases(conexion, transaccion, idCopia, plan.Hoy, null, null, true) == 0)
                {
                    CambiarEnLaBase(conexion, transaccion, "DELETE FROM ciclos_preliminares WHERE idciclo = @G AND estado = 'guardado'",
                        c => c.Parameters.AddWithValue("@G", idCopia));
                }
                else
                {
                    resultado.NombreCopia = nombreCopia;
                    PasarLoQueFaltaALaCopia(conexion, transaccion, idCopia, plan.Hoy);
                }

                // b) Terminan ayer las de antes y las de hoy con registros; las de hoy sin ellos se borran.
                TerminarYQuitar(conexion, transaccion, plan.Hoy, plan.Terminan.Concat(plan.ConRegistrosDeHoy).ToList(), plan.SeQuitan, idAdministrador);

                // c) Los horarios aplicados que tenian esas clases.
                AjustarAplicadas(conexion, transaccion, aplicadas, idAdministrador, resultado.VueltasAPlantilla);
                CerrarElUltimoAplicado(conexion, transaccion, plan.Hoy, idAdministrador);

                // d) Un solo renglon para todo.
                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Horario,
                    Accion = AccionDeCambio.DarDeBaja,
                    Tabla = "clases",
                    Fecha = plan.Hoy,
                    Descripcion = "Horario actual dado de baja el " + HistorialCambios.Fecha(plan.Hoy) + ": "
                        + TextoDeLaBaja(resultado.Terminan, resultado.SeQuitan, resultado.ConRegistrosDeHoy, resultado.ExtrasQueSeQuitan) + ".",
                    Antes = HistorialCambios.Datos(
                        "Iba a terminar", finDeAntes == null ? null : HistorialCambios.Fecha(Convert.ToDateTime(finDeAntes, CultureInfo.InvariantCulture)),
                        "Horarios aplicados", aplicadas.Count > 0 ? string.Join(", ", aplicadas.Select(a => a.Value)) : null),
                    Despues = HistorialCambios.Datos(
                        "Terminan ayer", resultado.Terminan > 0 ? RevisionCiclo.TextoClases(resultado.Terminan) : null,
                        "Se quitan", resultado.SeQuitan > 0 ? RevisionCiclo.TextoClases(resultado.SeQuitan) + " de hoy, sin nada registrado" : null,
                        "Quedan terminadas", resultado.ConRegistrosDeHoy > 0 ? RevisionCiclo.TextoClases(resultado.ConRegistrosDeHoy) + " de hoy, con registros" : null,
                        "Sesiones extra que se quitan", resultado.ExtrasQueSeQuitan > 0 ? resultado.ExtrasQueSeQuitan.ToString(CultureInfo.InvariantCulture) : null,
                        "Copia guardada", resultado.NombreCopia,
                        "Vuelven a ser plantilla", resultado.VueltasAPlantilla.Count > 0 ? string.Join(", ", resultado.VueltasAPlantilla) : null)
                });

                transaccion.Commit();
                Log.Info("Horario actual dado de baja: " + resultado.Terminan + " clases terminan ayer, " + resultado.SeQuitan + " se quitan, "
                    + resultado.ConRegistrosDeHoy + " de hoy con registros quedan terminadas" + (resultado.NombreCopia != null ? "; copia en la plantilla " + resultado.NombreCopia : "")
                    + " por " + (Datos_User.getUser() ?? "-") + ".");
                return resultado;
            }
        }

        /// <summary>
        /// Lo comun de sustituir el horario de hoy (la baja y "Ahora mismo"), dentro de su
        /// transaccion. Las clases de terminan acaban ayer, sin tocar activa (las que empezaron
        /// hoy se quedan sin dias propios); sus extras de hoy en adelante que nadie abrio ya no se
        /// darian y se quitan; sus horas de hoy sin terminar se cancelan (quien no alcanzo a
        /// registrar no tiene falta). Las de seQuitan se borran con sus franjas y excepciones (en
        /// cascada); horas_por_asignar no tiene claves foraneas y se haria huerfana.
        /// </summary>
        private static void TerminarYQuitar(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy, List<int> terminan, List<int> seQuitan, int idAdministrador)
        {
            if (terminan.Count > 0)
            {
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Fin", hoy.AddDays(-1));
                    comando.CommandText = "UPDATE clases SET vigente_hasta = @Fin WHERE idclases IN (" + Lista(comando, "t", terminan) + ")";
                    comando.ExecuteNonQuery();
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Hoy", hoy);
                    comando.CommandText = @"
                        DELETE FROM horario_excepciones
                         WHERE tipo = 'extra' AND fecha >= @Hoy AND clases_idclases IN (" + Lista(comando, "e", terminan) + @")
                           AND NOT EXISTS (SELECT 1 FROM codigos_accesos s WHERE s.horario_excepciones_id = horario_excepciones.idhorario_excepciones)";
                    comando.ExecuteNonQuery();
                }

                CancelarLasQueNoTerminaron(conexion, transaccion, terminan, hoy, idAdministrador);

                // Lo de hoy puesto como "No tomar en cuenta" se queda cancelado, sin la marca:
                // "Volver a contar" no podria devolver una clase que ya no vale hoy, y solo
                // convertiria en F el "-" de quien no alcanzo a registrar.
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Hoy", hoy.Date);
                    comando.Parameters.AddWithValue("@Marca", HorariosDatos.MarcaNoTomarEnCuenta);
                    comando.CommandText = @"
                        UPDATE horario_excepciones x
                        INNER JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
                           SET x.motivo = TRIM(TRAILING @Marca FROM x.motivo)
                         WHERE x.tipo = 'cancelada' AND x.fecha = @Hoy AND x.motivo LIKE CONCAT('%', @Marca)
                           AND h.clases_idclases IN (" + Lista(comando, "m", terminan) + ")";
                    comando.ExecuteNonQuery();
                }
            }

            if (seQuitan.Count > 0)
            {
                if (Esquema.HorasPorAsignar(conexion, transaccion))
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        comando.CommandText = "DELETE FROM horas_por_asignar WHERE clases_idclases IN (" + Lista(comando, "p", seQuitan) + ")";
                        comando.ExecuteNonQuery();
                    }
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "DELETE FROM clases WHERE idclases IN (" + Lista(comando, "k", seQuitan) + ")";
                    comando.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Lo que falta asignar (db/25) de las clases de la copia, como renglones de
        /// QuitadaPorAsignar: asi, al cargarla y aplicarla, vuelve a salir. Antes de borrar ni
        /// recortar nada. Con el formato de Cifrar. Lo de un cambio posterior a ese dia (el que se
        /// copia) entra solo si ya paso y la copia ya no tiene esas horas (la franja se quito en
        /// su sitio: se perderian). Un cambio futuro no: la copia aun tiene esas horas, o nunca
        /// las tuvo porque eran de una franja que empezaba despues, y saldrian de mas.
        /// </summary>
        private static void PasarLoQueFaltaALaCopia(MySqlConnection conexion, MySqlTransaction transaccion, int idCopia, DateTime dia)
        {
            if (!Esquema.HorasPorAsignar(conexion, transaccion))
            {
                return;
            }

            CambiarEnLaBase(conexion, transaccion, @"
                INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada, horario_origen, carga_origen)
                SELECT cc.idciclo, cc.profesor, cc.grupo, cc.materia, @PorAsignar,
                       CONCAT_WS('|', p.motivo, p.minutos, IFNULL(p.laboratorio_id, 0), p.dia_semana,
                                 TIME_FORMAT(p.hora_inicio, '%H:%i'), TIME_FORMAT(p.hora_fin, '%H:%i')),
                       cc.idcarga
                FROM ciclo_cargas cc
                INNER JOIN horas_por_asignar p ON p.clases_idclases = cc.clase_origen AND p.estado = 'pendiente' AND p.minutos > 0
                WHERE cc.idciclo = @C AND cc.quitada = 0
                  AND (p.desde <= @Dia OR (p.desde <= CURDATE() AND NOT EXISTS (
                        SELECT 1 FROM ciclo_franjas f
                         WHERE f.idcarga = cc.idcarga AND f.laboratorio = p.laboratorio_id AND f.dia_semana = p.dia_semana
                           AND f.hora_inicio <= p.hora_inicio AND f.hora_fin >= p.hora_fin)))
                ORDER BY cc.idcarga, p.idhoras_por_asignar", c =>
            {
                c.Parameters.AddWithValue("@C", idCopia);
                c.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                c.Parameters.AddWithValue("@Dia", dia.Date);
            });
        }

        /// <summary>"3 clases terminan ayer, 2 se quitan y 1 clase con registros de hoy queda como terminada", solo con las que hay.</summary>
        internal static string TextoDeLaBaja(int terminan, int seQuitan, int conRegistrosDeHoy, int extrasQueSeQuitan)
        {
            List<string> partes = new List<string>();
            if (terminan > 0)
            {
                partes.Add(RevisionCiclo.TextoClases(terminan) + (terminan == 1 ? " termina ayer" : " terminan ayer"));
            }

            if (seQuitan > 0)
            {
                partes.Add(RevisionCiclo.TextoClases(seQuitan) + (seQuitan == 1 ? " se quita" : " se quitan"));
            }

            if (conRegistrosDeHoy > 0)
            {
                partes.Add(RevisionCiclo.TextoClases(conRegistrosDeHoy) + " con registros de hoy "
                    + (conRegistrosDeHoy == 1 ? "queda como terminada" : "quedan como terminadas"));
            }

            if (extrasQueSeQuitan > 0)
            {
                partes.Add(extrasQueSeQuitan == 1 ? "1 sesión extra pendiente se quita" : extrasQueSeQuitan + " sesiones extra pendientes se quitan");
            }

            return partes.Count <= 1 ? string.Concat(partes)
                : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }

        /// <summary>
        /// Que clases cambian y como. Con bloquear (dentro de la transaccion), las deja
        /// bloqueadas: una sesion nueva espera a que esto termine y, si su franja se borro,
        /// no se crea enlazada a ella.
        /// </summary>
        private static PlanDeBaja LeerPlanDeBaja(MySqlConnection conexion, MySqlTransaction transaccion, bool bloquear)
        {
            string bloqueo = bloquear ? " FOR UPDATE" : "";
            PlanDeBaja plan = new PlanDeBaja { Hoy = Hoy(conexion, transaccion) };
            DateTime hoy = plan.Hoy;

            string porEmpezar = Convert.ToString(ValorEnLaBase(conexion, transaccion,
                "SELECT nombre FROM ciclos_preliminares WHERE estado = 'publicado' AND desde > @Hoy ORDER BY desde LIMIT 1" + bloqueo,
                c => c.Parameters.AddWithValue("@Hoy", hoy)), CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(porEmpezar))
            {
                plan.Motivo = "«" + porEmpezar + "» ya está aplicado y todavía no empieza: deshaz su aplicación en «3. Aplicar con fechas» y vuelve a intentarlo.";
                return plan;
            }

            // Las de antes: las mismas que recorta "Ahora mismo", tambien las desactivadas.
            using (MySqlCommand comando = new MySqlCommand("SELECT c.idclases, c.activa FROM clases c WHERE " + SeRecorta + " ORDER BY c.idclases" + bloqueo, conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Fin", hoy.AddDays(-1));
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        plan.Terminan.Add(reader.GetInt32(0));
                        if (Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) == 1)
                        {
                            plan.TerminanActivas++;
                        }
                    }
                }
            }

            int deHoyActivas;
            List<int> deHoy = IdsDeLasDeHoy(conexion, transaccion, hoy, bloquear, out deHoyActivas);
            if (plan.TerminanActivas == 0 && deHoyActivas == 0)
            {
                plan.Motivo = "Hoy no hay ningún horario en uso: no hay clases que dar de baja. Ya puedes aplicar otro.";
                return plan;
            }

            // Nunca antes de su ultima sesion. Lo de hoy si se queda; lo de despues de hoy
            // (un reloj mal puesto, una sesion futura) no se puede dejar sin clase. De todas
            // las que valen hoy: tambien las que terminan hoy, que "Ahora mismo" tambien mira.
            bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
            SesionPosterior posterior = UltimaSesionDespues(conexion, transaccion, c =>
            {
                c.Parameters.AddWithValue("@Fin", hoy);
                c.Parameters.AddWithValue("@Limite", hoy);
            }, conDadas, ValeEseDia);
            if (posterior != null)
            {
                plan.Motivo = "No se puede dar de baja: el " + HistorialCambios.Fecha(posterior.Fecha) + " ya "
                    + (posterior.EsAsistenciaDada ? "se dio asistencia en " : "hay una sesión de ") + posterior.Clase
                    + ". Una clase no puede terminar antes de lo que ya tiene registrado.";
                return plan;
            }

            plan.EmpiezanDespues = Convert.ToInt32(ValorEnLaBase(conexion, transaccion,
                "SELECT COUNT(*) FROM clases c WHERE c.activa = 1 AND c.vigente_desde > @Hoy",
                c => c.Parameters.AddWithValue("@Hoy", hoy)), CultureInfo.InvariantCulture);

            SepararLasDeHoy(conexion, transaccion, plan, deHoy, conDadas, bloquear);

            // Las extras que ya no se darian: de todas las clases que cambian.
            plan.ExtrasQueSeQuitan = ContarExtrasQueSeQuitan(conexion, transaccion, hoy, plan.Terminan.Concat(plan.SeQuitan).Concat(plan.ConRegistrosDeHoy).ToList());
            return plan;
        }

        /// <summary>Las extras de hoy en adelante de esas clases que nadie abrio: al sustituirlas ya no se darian y se quitan.</summary>
        private static int ContarExtrasQueSeQuitan(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy, List<int> cambian)
        {
            if (cambian.Count == 0)
            {
                return 0;
            }

            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Hoy", hoy.Date);
                comando.CommandText = @"
                    SELECT COUNT(*) FROM horario_excepciones x
                     WHERE x.tipo = 'extra' AND x.fecha >= @Hoy AND x.clases_idclases IN (" + Lista(comando, "e", cambian) + @")
                       AND NOT EXISTS (SELECT 1 FROM codigos_accesos s WHERE s.horario_excepciones_id = x.idhorario_excepciones)";
                return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Las clases que empiezan despues de hoy y estan canceladas un dia en que se dan y se
        /// cruza con sus horas una sesion extra pendiente de las que valen hoy, las que se sustituyen.
        /// Esa extra se quita y la cancelacion se queda: pudo provocarla ella o ponerse por otro
        /// motivo (HorariosDatos.CancelacionesBajoExtra), asi que solo se avisa. Solo lee.
        /// </summary>
        private static List<ExtraFuera> ClasesQueSiguenCanceladas(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy)
        {
            List<ExtraFuera> canceladas = new List<ExtraFuera>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT DISTINCT y.idhorario_excepciones, y.fecha, m.nombre_materia, g.nombre_grupo
                FROM horario_excepciones x
                INNER JOIN clases c   ON c.idclases = x.clases_idclases
                INNER JOIN horario_excepciones y ON y.tipo = 'cancelada' AND y.fecha = x.fecha
                INNER JOIN horarios h ON h.idhorarios = y.horarios_idhorarios AND h.laboratorios_idlaboratorios = x.laboratorios_idlaboratorios
                                     AND h.hora_inicio < x.hora_fin AND h.hora_fin > x.hora_inicio AND h.clases_idclases <> x.clases_idclases
                INNER JOIN clases k   ON k.idclases = h.clases_idclases AND k.vigente_desde > @Hoy
                                     AND " + VigenciaFranjas.SeDa(Esquema.VigenciaDeFranjas(conexion, transaccion), "y.fecha", "h", "k") + @"
                INNER JOIN materias m ON m.id_materia = k.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = k.grupos_idgrupos
                WHERE x.tipo = 'extra' AND x.fecha > @Hoy AND c.vigente_desde <= @Hoy AND c.vigente_hasta >= @Hoy
                  AND NOT EXISTS (SELECT 1 FROM codigos_accesos s WHERE s.horario_excepciones_id = x.idhorario_excepciones)
                ORDER BY y.fecha, m.nombre_materia, g.nombre_grupo", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Hoy", hoy.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        canceladas.Add(new ExtraFuera
                        {
                            ExcepcionId = reader.GetInt32("idhorario_excepciones"),
                            Fecha = reader.GetDateTime("fecha").Date,
                            Materia = Texto(reader, "nombre_materia"),
                            Grupo = Texto(reader, "nombre_grupo")
                        });
                    }
                }
            }

            return canceladas;
        }

        /// <summary>
        /// Las clases que empezaron hoy y valen hoy, tambien una desactivada: seguiria atando su
        /// horario aplicado. En activas, cuantas estan activas.
        /// </summary>
        private static List<int> IdsDeLasDeHoy(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy, bool bloquear, out int activas)
        {
            activas = 0;
            List<int> deHoy = new List<int>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT c.idclases, c.activa FROM clases c WHERE c.vigente_desde = @Hoy AND c.vigente_hasta >= @Hoy ORDER BY c.idclases" + (bloquear ? " FOR UPDATE" : ""), conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Hoy", hoy.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        deHoy.Add(reader.GetInt32(0));
                        if (Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) == 1)
                        {
                            activas++;
                        }
                    }
                }
            }

            return deHoy;
        }

        /// <summary>
        /// Esas clases de hoy, en plan.SeQuitan (sin nada registrado) y plan.ConRegistrosDeHoy.
        /// Con bloquear, bloqueando antes sus franjas y sus excepciones: una sesion nueva espera.
        /// </summary>
        private static void SepararLasDeHoy(MySqlConnection conexion, MySqlTransaction transaccion, PlanDeBaja plan, List<int> deHoy, bool conDadas, bool bloquear)
        {
            if (deHoy.Count == 0)
            {
                return;
            }

            if (bloquear)
            {
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM horarios WHERE clases_idclases IN (" + Lista(comando, "h", deHoy) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM horario_excepciones WHERE clases_idclases IN (" + Lista(comando, "x", deHoy) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }
            }

            HashSet<int> conRegistros = ClasesConAlgoRegistrado(conexion, transaccion, deHoy, conDadas, bloquear);
            plan.SeQuitan = deHoy.Where(id => !conRegistros.Contains(id)).ToList();
            plan.ConRegistrosDeHoy = deHoy.Where(conRegistros.Contains).ToList();
        }

        /// <summary>Las de hoy, ya separadas: lo que "Ahora mismo" necesita de ellas.</summary>
        private static PlanDeBaja LasDeHoy(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy, bool conDadas, bool bloquear)
        {
            PlanDeBaja plan = new PlanDeBaja { Hoy = hoy.Date };
            int activas;
            SepararLasDeHoy(conexion, transaccion, plan, IdsDeLasDeHoy(conexion, transaccion, hoy, bloquear, out activas), conDadas, bloquear);
            return plan;
        }

        /// <summary>
        /// De esas clases, las que tienen sesiones (enlazadas a una franja o a una extra, o
        /// sin enlace de su misma terna en su vigencia) o asistencias dadas. Las mismas partes
        /// que UltimaSesionDespues y que Deshacer, clase por clase. Con bloquear, con lecturas
        /// que bloquean: la transaccion ve una foto de cuando leyo por primera vez, y sin ellas
        /// una sesion creada despues de esa foto pero antes de bloquear las franjas no se veria
        /// y su clase se borraria. Lo que ya espera a esas franjas se ve o ya no se crea.
        /// </summary>
        private static HashSet<int> ClasesConAlgoRegistrado(MySqlConnection conexion, MySqlTransaction transaccion, List<int> clases, bool conDadas, bool bloquear)
        {
            string dia = VigenciaFranjas.DiaDeSesion("s");
            List<string> partes = new List<string>
            {
                @"SELECT hh.clases_idclases FROM horarios hh
                   INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios
                   WHERE hh.clases_idclases IN (#CLASES#)",
                @"SELECT xx.clases_idclases FROM horario_excepciones xx
                   INNER JOIN codigos_accesos s ON s.horario_excepciones_id = xx.idhorario_excepciones
                   WHERE xx.clases_idclases IN (#CLASES#)",
                @"SELECT c.idclases FROM clases c
                   INNER JOIN codigos_accesos s
                           ON s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
                          AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                          AND s.grupos_idgrupos = c.grupos_idgrupos AND " + dia + @" BETWEEN c.vigente_desde AND c.vigente_hasta
                   WHERE c.idclases IN (#CLASES#)"
            };
            if (conDadas)
            {
                partes.Add("SELECT ad.fk_clase FROM asistencias_dadas ad WHERE ad.fk_clase IN (#CLASES#)");
            }

            HashSet<int> conRegistros = new HashSet<int>();
            foreach (string parte in partes)
            {
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = parte.Replace("#CLASES#", Lista(comando, "k", clases)) + (bloquear ? " LOCK IN SHARE MODE" : "");
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            conRegistros.Add(reader.GetInt32(0));
                        }
                    }
                }
            }

            return conRegistros;
        }

        /// <summary>
        /// Los horarios aplicados que valen hoy y tienen alguna de las clases que la baja
        /// cambia, antes de cambiarlas (al borrarse una clase, su enlace se pierde). A uno que ya
        /// corto otro horario no le toca ninguna: se queda como esta, con su "iba al".
        /// </summary>
        private static List<KeyValuePair<int, string>> AplicadasQueCambian(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy, List<int> cambian)
        {
            List<KeyValuePair<int, string>> aplicadas = new List<KeyValuePair<int, string>>();
            if (cambian.Count == 0)
            {
                return aplicadas;
            }

            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Hoy", hoy.Date);
                comando.CommandText = @"
                    SELECT cp.idciclo, cp.nombre FROM ciclos_preliminares cp
                     WHERE cp.estado = 'publicado' AND cp.desde <= @Hoy AND cp.hasta >= @Hoy
                       AND EXISTS (SELECT 1 FROM ciclo_cargas cc WHERE cc.idciclo = cp.idciclo AND cc.clase_publicada IN (" + Lista(comando, "a", cambian) + @"))
                     ORDER BY cp.idciclo FOR UPDATE";
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        aplicadas.Add(new KeyValuePair<int, string>(reader.GetInt32(0), reader.GetString(1)));
                    }
                }
            }

            return aplicadas;
        }

        /// <summary>
        /// Los horarios aplicados que valen hoy y volveran a ser plantilla si se quitan esas
        /// clases: tienen alguna y no les queda otra. Es lo que hace AjustarAplicadas despues, y
        /// esos ya no ocupan su nombre entre los aplicados. Solo lee.
        /// </summary>
        private static HashSet<int> AplicadasQueVuelven(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy, List<int> seQuitan)
        {
            HashSet<int> vuelven = new HashSet<int>();
            if (seQuitan.Count == 0)
            {
                return vuelven;
            }

            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Hoy", hoy.Date);
                string clases = Lista(comando, "v", seQuitan);
                comando.CommandText = @"
                    SELECT cp.idciclo FROM ciclos_preliminares cp
                     WHERE cp.estado = 'publicado' AND cp.desde <= @Hoy AND cp.hasta >= @Hoy
                       AND EXISTS (SELECT 1 FROM ciclo_cargas cc WHERE cc.idciclo = cp.idciclo AND cc.clase_publicada IN (" + clases + @"))
                       AND NOT EXISTS (SELECT 1 FROM ciclo_cargas cc WHERE cc.idciclo = cp.idciclo AND cc.clase_publicada IS NOT NULL AND cc.clase_publicada NOT IN (" + clases + @"))";
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        vuelven.Add(reader.GetInt32(0));
                    }
                }
            }

            return vuelven;
        }

        /// <summary>
        /// Esos horarios aplicados, ya con sus clases cambiadas: al que no le queda ninguna, nunca
        /// le quedo nada que recordar y vuelve a ser plantilla (con otro nombre si ya hay una
        /// guardada que se llame igual); a los demas, su ultimo dia es el de sus clases, sin ser
        /// anterior a su primero: uno que empezo hoy y tiene registros de hoy valio hoy. Devuelve
        /// el ultimo dia que los que volvieron le habian puesto al horario de antes, el mas
        /// temprano, o null: quien los sustituye lo hereda.
        /// </summary>
        private static DateTime? AjustarAplicadas(MySqlConnection conexion, MySqlTransaction transaccion, List<KeyValuePair<int, string>> aplicadas, int idAdministrador, List<string> vueltasAPlantilla)
        {
            DateTime? finDeLasQueVolvieron = null;
            foreach (KeyValuePair<int, string> aplicada in aplicadas)
            {
                Action<MySqlCommand> parametros = c =>
                {
                    c.Parameters.AddWithValue("@C", aplicada.Key);
                    c.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                };
                object ultimo = ValorEnLaBase(conexion, transaccion, @"
                    SELECT MAX(c.vigente_hasta) FROM ciclo_cargas cc INNER JOIN clases c ON c.idclases = cc.clase_publicada WHERE cc.idciclo = @C", parametros);
                if (ultimo == null)
                {
                    object finAnterior = ValorEnLaBase(conexion, transaccion, "SELECT fin_anterior FROM ciclos_preliminares WHERE idciclo = @C", parametros);
                    if (finAnterior != null)
                    {
                        DateTime fin = Convert.ToDateTime(finAnterior, CultureInfo.InvariantCulture).Date;
                        finDeLasQueVolvieron = !finDeLasQueVolvieron.HasValue || fin < finDeLasQueVolvieron.Value ? fin : finDeLasQueVolvieron;
                    }

                    string nombre = NombreLibre(conexion, transaccion, aplicada.Value, EstadoConfiguracion.Guardada);
                    CambiarEnLaBase(conexion, transaccion, "DELETE FROM ciclo_recortes WHERE idciclo = @C", parametros);
                    CambiarEnLaBase(conexion, transaccion, @"
                        UPDATE ciclos_preliminares
                           SET estado = 'guardado', nombre = @Nombre, desde = NULL, hasta = NULL, fin_anterior = NULL, publicado_por = NULL, publicado_en = NULL,
                               cambiado_en = NOW(), cambiado_por = @Admin
                         WHERE idciclo = @C AND estado = 'publicado'", c =>
                    {
                        parametros(c);
                        c.Parameters.AddWithValue("@Nombre", nombre);
                    });
                    vueltasAPlantilla.Add(nombre);
                }
                else
                {
                    CambiarEnLaBase(conexion, transaccion, @"
                        UPDATE ciclos_preliminares SET hasta = GREATEST(@Ultimo, desde), cambiado_en = NOW(), cambiado_por = @Admin WHERE idciclo = @C AND estado = 'publicado'", c =>
                    {
                        parametros(c);
                        c.Parameters.AddWithValue("@Ultimo", Convert.ToDateTime(ultimo, CultureInfo.InvariantCulture).Date);
                    });
                }
            }

            return finDeLasQueVolvieron;
        }

        /// <summary>
        /// Al dar de baja no hay horario nuevo que corte al de antes: el ultimo aplicado que ya
        /// empezo, si sus fechas siguen llegando a hoy o despues, termina cuando terminan sus
        /// clases (lo que pasa si el que lo sustituyo nunca corrio y volvio a ser plantilla).
        /// A los que ya ajusto AjustarAplicadas no les cambia nada.
        /// </summary>
        private static void CerrarElUltimoAplicado(MySqlConnection conexion, MySqlTransaction transaccion, DateTime hoy, int idAdministrador)
        {
            object ultimoAplicado = ValorEnLaBase(conexion, transaccion,
                "SELECT idciclo FROM ciclos_preliminares WHERE estado = 'publicado' AND desde <= @Hoy ORDER BY desde DESC, idciclo DESC LIMIT 1",
                c => c.Parameters.AddWithValue("@Hoy", hoy.Date));
            if (ultimoAplicado == null)
            {
                return;
            }

            Action<MySqlCommand> parametros = c =>
            {
                c.Parameters.AddWithValue("@C", Convert.ToInt32(ultimoAplicado, CultureInfo.InvariantCulture));
                c.Parameters.AddWithValue("@Hoy", hoy.Date);
                c.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
            };
            object ultimoDia = ValorEnLaBase(conexion, transaccion, @"
                SELECT MAX(c.vigente_hasta) FROM ciclo_cargas cc INNER JOIN clases c ON c.idclases = cc.clase_publicada WHERE cc.idciclo = @C", parametros);
            if (ultimoDia == null)
            {
                return;
            }

            CambiarEnLaBase(conexion, transaccion, @"
                UPDATE ciclos_preliminares SET hasta = GREATEST(@Ultimo, desde), cambiado_en = NOW(), cambiado_por = @Admin
                 WHERE idciclo = @C AND estado = 'publicado' AND hasta >= @Hoy AND hasta > GREATEST(@Ultimo, desde)", c =>
            {
                parametros(c);
                c.Parameters.AddWithValue("@Ultimo", Convert.ToDateTime(ultimoDia, CultureInfo.InvariantCulture).Date);
            });
        }
    }
}
