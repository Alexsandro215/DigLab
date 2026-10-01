using System;
using System.Collections.Generic;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Un alumno tal como lo ve la pantalla de Alumnos del administrador.</summary>
    public class AlumnoInfo : UsuarioInfo
    {
        public int? CarreraId;
        public string Carrera;
        public int? GrupoId;
        public string Grupo;
        public int? SemestreId;
        public string Semestre;
        public int Bitacoras;
        public DateTime? UltimaBitacora;
    }

    /// <summary>
    /// Lo que se guarda al dar de alta o editar a un alumno. Sin grupo: el codigo
    /// es del profesor y el alumno no tiene que declarar a que grupo va.
    /// </summary>
    public class DatosAlumno : DatosUsuario
    {
        public int CarreraId;
        public int? SemestreId;
    }

    /// <summary>Datos de la pantalla de Alumnos: la lista y la carrera y el semestre de cada uno.</summary>
    internal class AlumnosDatos : UsuariosDatos
    {
        protected override int TipoUsuario
        {
            get { return 1; }
        }

        public override string NombreIdentificador
        {
            get { return "la matrícula"; }
        }

        public List<AlumnoInfo> Listar()
        {
            List<AlumnoInfo> lista = new List<AlumnoInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                // Si un alumno tiene mas de una fila de carrera y grupo (datos
                // viejos), cuenta la ultima. La fecha de una bitacora es la de su
                // sesion; las sesiones anteriores a la fase 6 no tienen fecha y se
                // toma la de su creacion.
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT u.idusuarios, u.numero_identificador, u.nombre, u.apellido_paterno, u.apellido_materno, u.correo,
                           " + ColumnasEstado(conexion) + @",
                           cgs.carreras_idcarreras, c.nombre_carrera, cgs.grupos_idgrupos, g.nombre_grupo,
                           cgs.semestres_idsemestres, s.numero_semestre,
                           COALESCE(b.bitacoras, 0) AS bitacoras, b.ultima
                    FROM usuarios u
                    LEFT JOIN carrera_grupo_semestre cgs ON cgs.idcarrera_grupo_semestre =
                        (SELECT MAX(x.idcarrera_grupo_semestre) FROM carrera_grupo_semestre x
                          WHERE x.usuarios_numero_identificador = u.idusuarios)
                    LEFT JOIN carreras c  ON c.idcarreras = cgs.carreras_idcarreras
                    LEFT JOIN grupos g    ON g.idgrupos = cgs.grupos_idgrupos
                    LEFT JOIN semestres s ON s.idsemestres = cgs.semestres_idsemestres
                    LEFT JOIN (SELECT rb.fk_usuario, COUNT(*) AS bitacoras,
                                      MAX(COALESCE(ca.fecha, DATE(ca.hora_registro))) AS ultima
                                 FROM registros_bitacoras rb
                                 INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
                                GROUP BY rb.fk_usuario) b ON b.fk_usuario = u.idusuarios
                    WHERE u.fk_tipo_usuario = @Tipo
                    ORDER BY u.apellido_paterno, u.apellido_materno, u.nombre", conexion))
                {
                    comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            AlumnoInfo alumno = new AlumnoInfo
                            {
                                CarreraId = Entero(reader, "carreras_idcarreras"),
                                Carrera = Texto(reader, "nombre_carrera"),
                                GrupoId = Entero(reader, "grupos_idgrupos"),
                                Grupo = Texto(reader, "nombre_grupo"),
                                SemestreId = Entero(reader, "semestres_idsemestres"),
                                Semestre = Texto(reader, "numero_semestre"),
                                Bitacoras = Convert.ToInt32(reader["bitacoras"]),
                                UltimaBitacora = Fecha(reader, "ultima")
                            };
                            LeerComunes(reader, alumno);
                            lista.Add(alumno);
                        }
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Carrera y semestre del alumno. Si no tenia fila (datos viejos), se
        /// crea: antes el cambio se perdia sin avisar. Si tenia varias, se
        /// actualizan todas para que no queden contradiciendose. El grupo ya no se
        /// pide: uno nuevo queda sin grupo y a uno que ya lo tenia no se le toca.
        /// </summary>
        protected override void GuardarExtras(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosUsuario datos)
        {
            DatosAlumno alumno = (DatosAlumno)datos;
            int filas;
            using (MySqlCommand contar = new MySqlCommand(
                "SELECT COUNT(*) FROM carrera_grupo_semestre WHERE usuarios_numero_identificador = @Id", conexion, transaccion))
            {
                contar.Parameters.AddWithValue("@Id", id);
                filas = Convert.ToInt32(contar.ExecuteScalar());
            }

            string consulta = filas == 0
                ? "INSERT INTO carrera_grupo_semestre (usuarios_numero_identificador, carreras_idcarreras, grupos_idgrupos, semestres_idsemestres) " +
                  "VALUES (@Id, @Carrera, NULL, @Semestre)"
                : "UPDATE carrera_grupo_semestre SET carreras_idcarreras = @Carrera, semestres_idsemestres = @Semestre " +
                  "WHERE usuarios_numero_identificador = @Id";
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                comando.Parameters.AddWithValue("@Carrera", alumno.CarreraId);
                comando.Parameters.AddWithValue("@Semestre", alumno.SemestreId.HasValue ? (object)alumno.SemestreId.Value : DBNull.Value);
                comando.ExecuteNonQuery();
            }
        }
    }
}
