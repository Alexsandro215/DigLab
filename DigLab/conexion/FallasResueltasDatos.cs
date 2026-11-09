using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Una cosa reportada en una sesion, por un estudiante en su bitacora o por su docente desde su panel.</summary>
    internal sealed class FallaDeSesion
    {
        /// <summary>FallasResueltasDatos.OrigenBitacora u OrigenDocente.</summary>
        public string Origen;
        /// <summary>registros_bitacoras.idregistros_bitacoras o reportes_profesor.idreportes_profesor.</summary>
        public int RegistroId;
        /// <summary>El equipo: "PC-07", o "Todo el laboratorio" si el docente reporto algo general.</summary>
        public string Equipo;
        /// <summary>Quien lo reporto: el nombre del estudiante, o "Su docente".</summary>
        public string Quien;
        /// <summary>Lo que reporto, en una linea.</summary>
        public string Falla;
        public bool Resuelta;
        /// <summary>"Resuelta el 06/11/2026 a las 10:30. Se cambio el teclado."; vacio si sigue pendiente.</summary>
        public string Estado;

        /// <summary>El renglon de la lista de "Resolver fallas...".</summary>
        public override string ToString()
        {
            return Equipo + "  ·  " + Quien + "  ·  " + Falla + (Resuelta ? "  ·  " + Estado : "");
        }
    }

    /// <summary>
    /// Las fallas que la administracion da por resueltas en Bitacoras (db/26,
    /// fallas_resueltas): salen de naranja y de Alertas, y van al final del PDF. No
    /// cambian lo que se reporto: Estadisticas lo sigue contando todo. Quitar el
    /// renglon la deja otra vez pendiente, y cada cambio queda en el historial.
    /// </summary>
    internal static class FallasResueltasDatos
    {
        public const string OrigenBitacora = "bitacora";
        public const string OrigenDocente = "docente";
        public const int LargoNota = 200;

        /// <summary>
        /// Lo reportado en esa sesion, primero los estudiantes y luego su docente: lo
        /// que ofrece "Resolver fallas...". Un aviso de otros equipos sin internet
        /// tambien cuenta: es algo que atender, aunque no sea de su maquina.
        /// </summary>
        public static List<FallaDeSesion> DeLaSesion(int idSesion)
        {
            List<FallaDeSesion> fallas = new List<FallaDeSesion>();
            DataTable bitacoras = new Consultas().consultaRegistroSesion(idSesion);
            foreach (DataRow fila in bitacoras.Rows.Cast<DataRow>().Where(ReportaAlgo))
            {
                string equipo = Convert.ToString(fila["numero_computadora"], CultureInfo.InvariantCulture).Trim();
                bool resuelta = Consultas.Resuelta(fila);
                fallas.Add(new FallaDeSesion
                {
                    Origen = OrigenBitacora,
                    RegistroId = Convert.ToInt32(fila["idregistros_bitacoras"], CultureInfo.InvariantCulture),
                    Equipo = equipo.Length == 0 ? "—" : equipo,
                    Quien = Convert.ToString(fila["nombre_completo"], CultureInfo.InvariantCulture).Trim(),
                    Falla = Consultas.DescribirFallas(fila),
                    Resuelta = resuelta,
                    Estado = resuelta ? Consultas.TextoResuelta(fila) : ""
                });
            }

            foreach (ReporteProfesor reporte in new ReportesProfesorDatos().DeLaSesion(idSesion))
            {
                fallas.Add(new FallaDeSesion
                {
                    Origen = OrigenDocente,
                    RegistroId = reporte.Id,
                    Equipo = string.IsNullOrWhiteSpace(reporte.Equipo) ? "Todo el laboratorio" : reporte.Equipo.Trim(),
                    Quien = "Su docente",
                    Falla = ReportesProfesorDatos.NombreDeArea(reporte.Area) + ": " + (reporte.Falla ?? "").Trim().TrimEnd('.')
                        + (string.IsNullOrWhiteSpace(reporte.Comentarios) ? "" : ". " + reporte.Comentarios.Trim()),
                    Resuelta = reporte.Resuelta,
                    Estado = reporte.TextoResuelta
                });
            }

            return fallas;
        }

        /// <summary>Si esa bitacora reporta algo que se pueda atender (una falla, un comentario o un aviso de otros equipos).</summary>
        public static bool ReportaAlgo(DataRow fila)
        {
            return Consultas.ConFalla(fila) || Consultas.AvisoSinInternet(fila).Length > 0;
        }

        /// <summary>
        /// Da por resueltas unas y deja otras pendientes otra vez, en una sola
        /// transaccion, con un renglon del historial por cada una. nota: lo que se
        /// hizo (de las que se resuelven); vacio, sin nota.
        /// </summary>
        public static void Guardar(int idSesion, IEnumerable<FallaDeSesion> resolver, IEnumerable<FallaDeSesion> reabrir, string nota)
        {
            Permisos.Exigir("Dar por resueltas las fallas de una sesion");
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                if (!Esquema.FallasResueltas(conexion))
                {
                    throw new ErrorDeDatos("Todavía no se pueden dar por resueltas las fallas: a la base de datos le falta una actualización (db/26).");
                }

                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    int? laboratorio = null;
                    DateTime? fecha = null;
                    using (MySqlCommand comando = new MySqlCommand(
                        "SELECT laboratorios_idlaboratorios, COALESCE(fecha, DATE(hora_registro)) AS dia FROM codigos_accesos WHERE idcodigos_accesos = @Sesion",
                        conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Sesion", idSesion);
                        using (MySqlDataReader lector = comando.ExecuteReader())
                        {
                            if (lector.Read())
                            {
                                laboratorio = lector.IsDBNull(0) ? (int?)null : lector.GetInt32(0);
                                fecha = lector.IsDBNull(1) ? (DateTime?)null : lector.GetDateTime(1);
                            }
                        }
                    }

                    string notaLimpia = HistorialCambios.Limpiar(nota, LargoNota);
                    object quien = IdDeLaSesion(conexion, transaccion);
                    foreach (FallaDeSesion falla in resolver)
                    {
                        // Si alguien mas ya la marco, se queda como la dejo (y sin otro renglon).
                        using (MySqlCommand comando = new MySqlCommand(@"
                            INSERT INTO fallas_resueltas (origen, registro_id, resuelta_en, resuelta_por, nota)
                            SELECT @Origen, @Registro, NOW(), @Por, @Nota FROM DUAL
                            WHERE NOT EXISTS (SELECT 1 FROM fallas_resueltas x WHERE x.origen = @Origen AND x.registro_id = @Registro)", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Origen", falla.Origen);
                            comando.Parameters.AddWithValue("@Registro", falla.RegistroId);
                            comando.Parameters.AddWithValue("@Por", quien);
                            comando.Parameters.AddWithValue("@Nota", notaLimpia == null ? (object)DBNull.Value : notaLimpia);
                            if (comando.ExecuteNonQuery() > 0)
                            {
                                Anotar(conexion, transaccion, AccionDeCambio.Resolver, falla, laboratorio, fecha, notaLimpia);
                            }
                        }
                    }

                    foreach (FallaDeSesion falla in reabrir)
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "DELETE FROM fallas_resueltas WHERE origen = @Origen AND registro_id = @Registro", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Origen", falla.Origen);
                            comando.Parameters.AddWithValue("@Registro", falla.RegistroId);
                            if (comando.ExecuteNonQuery() > 0)
                            {
                                Anotar(conexion, transaccion, AccionDeCambio.Reabrir, falla, laboratorio, fecha, null);
                            }
                        }
                    }

                    transaccion.Commit();
                }
            }
        }

        private static void Anotar(MySqlConnection conexion, MySqlTransaction transaccion, string accion, FallaDeSesion falla, int? laboratorio, DateTime? fecha, string nota)
        {
            bool resuelve = accion == AccionDeCambio.Resolver;
            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = TipoDeCambio.Bitacora,
                Accion = accion,
                Tabla = falla.Origen == OrigenDocente ? "reportes_profesor" : "registros_bitacoras",
                RegistroId = falla.RegistroId,
                LaboratorioId = laboratorio,
                Fecha = fecha,
                Descripcion = (resuelve ? "Falla resuelta: " : "Falla sin resolver otra vez: ") + falla.Equipo + ", " + falla.Quien + ". " + falla.Falla,
                Antes = HistorialCambios.Datos("Estado", resuelve ? "Sin resolver" : "Resuelta"),
                Despues = HistorialCambios.Datos("Estado", resuelve ? "Resuelta" : "Sin resolver", "Qué se hizo", nota)
            });
        }

        private static object IdDeLaSesion(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idusuarios FROM usuarios WHERE numero_identificador = @Yo LIMIT 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Yo", Datos_User.getUser() ?? "");
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? (object)DBNull.Value : Convert.ToInt32(valor, CultureInfo.InvariantCulture);
            }
        }
    }
}
