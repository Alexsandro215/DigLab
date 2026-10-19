using System;
using System.Collections.Generic;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>
    /// Carreras, semestres y grupos para los dialogos del administrador. Los
    /// grupos ya no se piden al alumno: son de las clases (profesor + grupo +
    /// materia), y se crean desde Horarios.
    /// </summary>
    internal class CatalogosDatos
    {
        private readonly Conexion mconexion = new Conexion();

        public List<OpcionCombo> Carreras()
        {
            return LeerOpciones("SELECT idcarreras AS id, nombre_carrera AS texto FROM carreras ORDER BY nombre_carrera", null);
        }

        public List<OpcionCombo> Semestres()
        {
            return LeerOpciones("SELECT idsemestres AS id, numero_semestre AS texto FROM semestres ORDER BY idsemestres", null);
        }

        /// <summary>Crea un grupo dentro de su carrera (el alta vieja no guardaba la carrera y el grupo no aparecia en ninguna).</summary>
        public int CrearGrupo(string nombre, int idCarrera)
        {
            Permisos.Exigir("Crear un grupo");
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                using (MySqlCommand existe = new MySqlCommand(
                    "SELECT COUNT(*) FROM grupos WHERE nombre_grupo = @Nombre AND Carrera_id = @Carrera", conexion))
                {
                    existe.Parameters.AddWithValue("@Nombre", nombre);
                    existe.Parameters.AddWithValue("@Carrera", idCarrera);
                    if (Convert.ToInt32(existe.ExecuteScalar()) > 0)
                    {
                        throw new ErrorDeDatos("Esa carrera ya tiene un grupo " + nombre + ": cancela y elígelo en la lista de grupos, o escribe otro nombre.");
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(
                    "INSERT INTO grupos (nombre_grupo, Carrera_id) VALUES (@Nombre, @Carrera); SELECT LAST_INSERT_ID();", conexion))
                {
                    comando.Parameters.AddWithValue("@Nombre", nombre);
                    comando.Parameters.AddWithValue("@Carrera", idCarrera);
                    int id = Convert.ToInt32(comando.ExecuteScalar());
                    Log.Info("Grupo nuevo " + nombre + " (carrera " + idCarrera + ") por " + (Datos_User.getUser() ?? "-") + ".");
                    return id;
                }
            }
        }

        private List<OpcionCombo> LeerOpciones(string consulta, Action<MySqlCommand> parametros)
        {
            List<OpcionCombo> lista = new List<OpcionCombo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new OpcionCombo { Id = Convert.ToInt32(reader["id"]), Texto = reader["texto"].ToString() });
                    }
                }
            }

            return lista;
        }
    }
}
