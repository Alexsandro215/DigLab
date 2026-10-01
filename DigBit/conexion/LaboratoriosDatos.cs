using System;
using System.Collections.Generic;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Un laboratorio tal como lo ve la pantalla de Laboratorios del administrador.</summary>
    public class LaboratorioInfo
    {
        public int Id;
        public string Nombre;
        public bool Activo;
        /// <summary>Franjas de clases activas y vigentes: mientras haya, no se puede quitar.</summary>
        public int FranjasVigentes;
        /// <summary>Todas sus franjas, de clases vigentes o no.</summary>
        public int Franjas;
        public int Sesiones;
        public DateTime? UltimaSesion;
        /// <summary>Equipos distintos que registraron bitacoras en los ultimos 30 dias.</summary>
        public int EquiposRecientes;

        /// <summary>Sin franjas ni sesiones: se creo por error y se puede borrar sin perder nada.</summary>
        public bool SinUso
        {
            get { return Franjas == 0 && Sesiones == 0; }
        }
    }

    /// <summary>
    /// Datos de la pantalla de Laboratorios. Un laboratorio con historial no se
    /// borra: se desactiva (db/08_migracion_bajas.sql), porque las claves foraneas
    /// borrarian en cascada su horario y dejarian sus sesiones sin laboratorio.
    /// </summary>
    internal class LaboratoriosDatos
    {
        private const int DiasEquiposRecientes = 30;

        private readonly Conexion mconexion = new Conexion();

        /// <summary>False si la base aun no tiene laboratorios.activo: solo se pueden borrar los que no se usaron.</summary>
        public bool DesactivacionDisponible { get; private set; }

        public List<LaboratorioInfo> Listar()
        {
            List<LaboratorioInfo> lista = new List<LaboratorioInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                DesactivacionDisponible = Esquema.LaboratoriosDesactivables(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT l.idlaboratorios, l.nombre_laboratorio, " + (DesactivacionDisponible ? "l.activo" : "1 AS activo") + @",
                           (SELECT COUNT(*) FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases
                             WHERE h.laboratorios_idlaboratorios = l.idlaboratorios
                               AND c.activa = 1 AND c.vigente_hasta >= CURDATE()) AS franjas_vigentes,
                           (SELECT COUNT(*) FROM horarios h WHERE h.laboratorios_idlaboratorios = l.idlaboratorios) AS franjas,
                           (SELECT COUNT(*) FROM codigos_accesos ca WHERE ca.laboratorios_idlaboratorios = l.idlaboratorios) AS sesiones,
                           (SELECT MAX(COALESCE(ca.fecha, DATE(ca.hora_registro))) FROM codigos_accesos ca
                             WHERE ca.laboratorios_idlaboratorios = l.idlaboratorios) AS ultima,
                           (SELECT COUNT(DISTINCT rb.numero_computadora) FROM registros_bitacoras rb
                             INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
                             WHERE ca.laboratorios_idlaboratorios = l.idlaboratorios
                               AND COALESCE(ca.fecha, DATE(ca.hora_registro)) >= CURDATE() - INTERVAL @Dias DAY) AS equipos
                    FROM laboratorios l
                    ORDER BY l.nombre_laboratorio", conexion))
                {
                    comando.Parameters.AddWithValue("@Dias", DiasEquiposRecientes);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new LaboratorioInfo
                            {
                                Id = Convert.ToInt32(reader["idlaboratorios"]),
                                Nombre = reader["nombre_laboratorio"].ToString(),
                                Activo = Convert.ToInt32(reader["activo"]) == 1,
                                FranjasVigentes = Convert.ToInt32(reader["franjas_vigentes"]),
                                Franjas = Convert.ToInt32(reader["franjas"]),
                                Sesiones = Convert.ToInt32(reader["sesiones"]),
                                UltimaSesion = reader["ultima"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["ultima"]),
                                EquiposRecientes = Convert.ToInt32(reader["equipos"])
                            });
                        }
                    }
                }
            }

            return lista;
        }

        public void Crear(string nombre)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                ComprobarNombreLibre(conexion, nombre, null);
                using (MySqlCommand comando = new MySqlCommand(
                    "INSERT INTO laboratorios (nombre_laboratorio) VALUES (@Nombre)", conexion))
                {
                    comando.Parameters.AddWithValue("@Nombre", nombre);
                    comando.ExecuteNonQuery();
                }
            }

            Log.Info("Laboratorio nuevo '" + nombre + "' por " + Quien() + ".");
        }

        public void Renombrar(int id, string anterior, string nuevo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                ComprobarNombreLibre(conexion, nuevo, id);
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE laboratorios SET nombre_laboratorio = @Nombre WHERE idlaboratorios = @Id", conexion))
                {
                    comando.Parameters.AddWithValue("@Nombre", nuevo);
                    comando.Parameters.AddWithValue("@Id", id);
                    comando.ExecuteNonQuery();
                }
            }

            // Queda en el log porque los equipos de ese laboratorio se configuran con
            // el nombre: hasta reconfigurarlos no lo reconocen.
            Log.Info("Laboratorio '" + anterior + "' renombrado a '" + nuevo + "' por " + Quien() + ".");
        }

        /// <summary>Borra un laboratorio que nunca se uso. Lo vuelve a comprobar en la base antes de borrar.</summary>
        public void Borrar(int id)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                DELETE FROM laboratorios
                 WHERE idlaboratorios = @Id
                   AND NOT EXISTS (SELECT 1 FROM horarios h WHERE h.laboratorios_idlaboratorios = @Id)
                   AND NOT EXISTS (SELECT 1 FROM codigos_accesos ca WHERE ca.laboratorios_idlaboratorios = @Id)
                   AND NOT EXISTS (SELECT 1 FROM horario_excepciones x WHERE x.laboratorios_idlaboratorios = @Id)", conexion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                if (comando.ExecuteNonQuery() == 0)
                {
                    throw new ErrorDeDatos("Ese laboratorio ya tiene horario o sesiones, así que no se puede borrar; desactívalo.");
                }
            }

            Log.Info("Laboratorio id " + id + " borrado (no tenia uso) por " + Quien() + ".");
        }

        /// <summary>Desactiva un laboratorio con historial. No se permite con franjas vigentes.</summary>
        public void Desactivar(int id)
        {
            CambiarEstado(id, false);
        }

        public void Reactivar(int id)
        {
            CambiarEstado(id, true);
        }

        private void CambiarEstado(int id, bool activo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.LaboratoriosDesactivables(conexion))
                {
                    throw new ErrorDeDatos("La base de datos todavía no permite desactivar laboratorios: falta aplicar db/08_migracion_bajas.sql.");
                }

                using (MySqlCommand comando = new MySqlCommand(activo
                    ? "UPDATE laboratorios SET activo = 1 WHERE idlaboratorios = @Id"
                    : @"UPDATE laboratorios l SET l.activo = 0
                         WHERE l.idlaboratorios = @Id
                           AND NOT EXISTS (SELECT 1 FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases
                                            WHERE h.laboratorios_idlaboratorios = @Id
                                              AND c.activa = 1 AND c.vigente_hasta >= CURDATE())", conexion))
                {
                    comando.Parameters.AddWithValue("@Id", id);
                    if (comando.ExecuteNonQuery() == 0 && !activo)
                    {
                        throw new ErrorDeDatos("Ese laboratorio tiene clases vigentes en el horario: muévelas o quítalas en Horarios antes de desactivarlo.");
                    }
                }
            }

            Log.Info((activo ? "Reactivado" : "Desactivado") + " el laboratorio id " + id + " por " + Quien() + ".");
        }

        private static void ComprobarNombreLibre(MySqlConnection conexion, string nombre, int? excepto)
        {
            // Con el cotejamiento de la tabla, "Cisco" y "CISCO" son el mismo nombre,
            // igual que para el equipo que busca su laboratorio por nombre.
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM laboratorios WHERE nombre_laboratorio = @Nombre" + (excepto.HasValue ? " AND idlaboratorios <> @Id" : ""), conexion))
            {
                comando.Parameters.AddWithValue("@Nombre", nombre);
                if (excepto.HasValue)
                {
                    comando.Parameters.AddWithValue("@Id", excepto.Value);
                }

                if (Convert.ToInt32(comando.ExecuteScalar()) > 0)
                {
                    throw new ErrorDeDatos("Ya hay un laboratorio llamado " + nombre + ".");
                }
            }
        }

        private static string Quien()
        {
            return Datos_User.getUser() ?? "-";
        }
    }
}
