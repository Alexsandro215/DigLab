using System;
using System.Globalization;
using System.Runtime.InteropServices;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// Apagar los equipos de un laboratorio desde la administracion (pedido del
    /// 2026-11-13: "Agregar el apagar desde admin. Manda senal y las apaga"). La
    /// administracion deja una orden en ordenes_equipos (db/29, la misma tabla de
    /// "Actualizar ya"), que caduca a los 10 minutos; el servicio DigLab.Vigilante de
    /// cada equipo del laboratorio la mira cada minuto (DigLab.exe --revisar-ordenes) y
    /// apaga con shutdown.exe, con el aviso de Windows: 2 minutos si alguien usa el
    /// equipo, unos segundos si esta libre. Un equipo que se encendio despues de
    /// mandarla no la cumple: si no, al encenderlo para la clase siguiente se apagaria.
    /// Solo la administracion la escribe; los equipos solo leen (digbit_equipo, SELECT).
    /// </summary>
    internal static class OrdenesEquiposDatos
    {
        public const string OrdenApagar = "apagar";

        /// <summary>Lo que dura la orden: un equipo que no la vio en este tiempo (sin red) ya no se apaga.</summary>
        public const int MinutosParaApagar = 10;

        /// <summary>DigLab.exe --revisar-ordenes (el vigilante, cada minuto).</summary>
        public const string ArgumentoRevisar = "--revisar-ordenes";

        /// <summary>Codigo de salida de --revisar-ordenes cuando hay que apagar (detalle "APAGAR id").</summary>
        public const int CodigoApagar = 20;

        /// <summary>
        /// Manda apagar los equipos encendidos de ese laboratorio (o de todos, con null).
        /// Lo que no se puede (falta la tabla) sale como ErrorDeDatos.
        /// </summary>
        public static void MandarApagar(int? idLaboratorio, string nombreLaboratorio)
        {
            Permisos.Exigir("Mandar apagar los equipos de un laboratorio");
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                if (!Esquema.OrdenesEquipos(conexion))
                {
                    throw new ErrorDeDatos("Todavía no se pueden apagar los equipos desde aquí: a la base de datos le falta db/29_actualizaciones.sql "
                        + "(la tabla de órdenes a los equipos).");
                }

                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    int id;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO ordenes_equipos (tipo, laboratorios_idlaboratorios, creada_en, creada_por, caduca_en)
                        VALUES (@Tipo, @Lab, NOW(), @Por, NOW() + INTERVAL " + MinutosParaApagar + " MINUTE)", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Tipo", OrdenApagar);
                        comando.Parameters.AddWithValue("@Lab", idLaboratorio.HasValue ? (object)idLaboratorio.Value : DBNull.Value);
                        comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, transaccion));
                        comando.ExecuteNonQuery();
                        id = (int)comando.LastInsertedId;
                    }

                    string donde = idLaboratorio.HasValue ? "los equipos de " + nombreLaboratorio : "los equipos de todos los laboratorios";
                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.Laboratorio,
                        Accion = AccionDeCambio.Apagar,
                        Tabla = "ordenes_equipos",
                        RegistroId = id,
                        LaboratorioId = idLaboratorio,
                        Descripcion = "Se mandó apagar " + donde + ".",
                        Despues = HistorialCambios.Datos("Laboratorio", idLaboratorio.HasValue ? nombreLaboratorio : "Todos")
                    });
                    transaccion.Commit();
                }
            }

            Log.Info("Orden de apagar mandada a " + (idLaboratorio.HasValue ? "el laboratorio " + idLaboratorio.Value : "todos los laboratorios") + ".");
        }

        /// <summary>
        /// DigLab.exe --revisar-ordenes (el vigilante, como SYSTEM): si la administracion
        /// mando apagar los equipos de este laboratorio (o de todos) mientras este ya estaba
        /// encendido y la orden no ha caducado. Codigo de salida: 0 nada; 20 apagar (detalle
        /// "APAGAR id"); 1 sin servidor; 3 la base no tiene la tabla. Sin acentos: va al log
        /// del vigilante, que es ASCII.
        /// </summary>
        public static int RevisarParaElEquipo(out string detalle)
        {
            try
            {
                using (MySqlConnection conexion = new Conexion().GetConexion())
                {
                    if (!Esquema.OrdenesEquipos(conexion))
                    {
                        detalle = "La base no tiene ordenes para los equipos (falta db/29, o esta cuenta no puede leerlas).";
                        return 3;
                    }

                    // Solo las mandadas despues de encenderse: el tiempo encendido, con un
                    // minuto de margen por si el reloj del equipo y el del servidor no van igual.
                    long encendido = (long)(GetTickCount64() / 1000UL) + 60;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        SELECT MAX(o.idordenes_equipos)
                        FROM ordenes_equipos o
                        WHERE o.tipo = @Tipo AND o.caduca_en > NOW()
                          AND o.creada_en >= NOW() - INTERVAL @Encendido SECOND
                          AND (o.laboratorios_idlaboratorios IS NULL
                               OR o.laboratorios_idlaboratorios IN (SELECT l.idlaboratorios FROM laboratorios l WHERE l.nombre_laboratorio = @Laboratorio))",
                        conexion))
                    {
                        comando.Parameters.AddWithValue("@Tipo", OrdenApagar);
                        comando.Parameters.AddWithValue("@Encendido", encendido);
                        comando.Parameters.AddWithValue("@Laboratorio", LaboratorioEquipo.NombreConfigurado ?? "");
                        object valor = comando.ExecuteScalar();
                        if (valor == null || valor == DBNull.Value)
                        {
                            detalle = "Sin ordenes.";
                            return 0;
                        }

                        detalle = "APAGAR " + Convert.ToInt32(valor, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                        return CodigoApagar;
                    }
                }
            }
            catch (Exception ex)
            {
                detalle = "No se pudo leer la base: " + ex.Message;
                return 1;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

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
