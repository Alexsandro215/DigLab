using System;
using System.Collections.Generic;
using System.Data;
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

        protected override string NombreDelTipo
        {
            get { return "estudiante"; }
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
        /// Las bitacoras de un alumno, de la mas reciente a la mas antigua: su
        /// historial (VentanaHistorialAlumno) y lo que se le entrega al exportar sus
        /// datos. Una fila por bitacora con su sesion (fecha, horas, laboratorio,
        /// clase y profesor; uso_libre sin clase, db/14), el equipo, cuando registro
        /// y las columnas de falla con los nombres de consultaRegistroSesion, para
        /// que Consultas.DescribirFallas, ConFalla y AvisoSinInternet valgan igual.
        /// retirada_en: cuando su profesor le quito esa asistencia (db/17; NULL si
        /// no, o si esta cuenta de MySQL no ve la tabla). Solo lee: tambien en solo
        /// consulta. La fecha de las sesiones de antes de la fase 6, que no la
        /// tienen, es la de su creacion.
        /// </summary>
        public DataTable Bitacoras(int idAlumno)
        {
            DataTable tabla = new DataTable();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string avisos = Esquema.EquiposSinInternet(conexion) ? "COALESCE(rb.equipos_sin_internet, '')" : "''";
                bool conRetiro = Esquema.AsistenciaRetirable(conexion);
                // sin_conexion con IF: TINYINT(1) llegaria como True/False (TreatTinyAsBoolean).
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT rb.idregistros_bitacoras, ca.idcodigos_accesos,
                           COALESCE(ca.fecha, DATE(ca.hora_registro)) AS fecha,
                           ca.hora_entrada, ca.hora_salida, ca.horarios_idhorarios, ca.horario_excepciones_id,
                           IF(" + HorariosDatos.CondicionUsoLibre + @", 1, 0) AS uso_libre,
                           COALESCE(l.nombre_laboratorio, '') AS laboratorio,
                           COALESCE(m.nombre_materia, '') AS materia,
                           COALESCE(g.nombre_grupo, '') AS grupo,
                           COALESCE(CONCAT_WS(' ', p.nombre, p.apellido_paterno, p.apellido_materno), '') AS profesor,
                           COALESCE(rb.numero_computadora, '') AS numero_computadora,
                           COALESCE(rb.falla_red, '') AS falla_red, COALESCE(rb.comentarios_red, '') AS comentarios_red,
                           COALESCE(rb.falla_hardware, '') AS falla_hardware, COALESCE(rb.comentarios_hardware, '') AS comentarios_hardware,
                           COALESCE(rb.falla_software, '') AS falla_software, COALESCE(rb.comentarios_software, '') AS comentarios_software,
                           IF(rb.registrado_sin_conexion = 1, 1, 0) AS sin_conexion,
                           rb.registrado_en,
                           CASE WHEN " + Consultas.CondicionConFalla + @" THEN 1 ELSE 0 END AS con_falla,
                           " + avisos + @" AS avisa_sin_internet,
                           " + (conRetiro ? "ar.retirada_en" : "NULL") + @" AS retirada_en
                    FROM registros_bitacoras rb
                    INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
                    LEFT JOIN laboratorios l ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                    LEFT JOIN materias m     ON m.id_materia = ca.materias_id_materia
                    LEFT JOIN grupos g       ON g.idgrupos = ca.grupos_idgrupos
                    LEFT JOIN usuarios p     ON p.idusuarios = ca.usuarios_idusuarios
                    " + (conRetiro ? "LEFT JOIN asistencias_retiradas ar ON ar.fk_bitacora = rb.idregistros_bitacoras" : "") + @"
                    WHERE rb.fk_usuario = @Id
                    ORDER BY fecha DESC, ca.hora_entrada DESC, rb.idregistros_bitacoras DESC", conexion))
                {
                    comando.Parameters.AddWithValue("@Id", idAlumno);
                    using (MySqlDataAdapter adaptador = new MySqlDataAdapter(comando))
                    {
                        adaptador.Fill(tabla);
                    }
                }
            }

            return tabla;
        }

        /// <summary>
        /// Si la base deja dar de alta a un alumno sin grupo (db/10). Sin eso,
        /// "Crear cuenta" fallaria al final, despues de que el alumno lo escribio
        /// todo. Lanza si no hay servidor.
        /// </summary>
        public bool AltaDisponible()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return Esquema.CarreraSinGrupo(conexion);
            }
        }

        /// <summary>
        /// Carrera y semestre del alumno. Si no tenia fila (datos viejos), se
        /// crea: antes el cambio se perdia sin avisar. Si tenia varias, se
        /// actualizan todas para que no queden contradiciendose. El grupo ya no se
        /// pide: uno nuevo queda sin grupo y a uno que ya lo tenia no se le toca.
        /// "Crear cuenta" del inicio de sesion tambien pasa por aqui: sin sesion
        /// no es solo consulta, asi que Exigir no la frena.
        /// </summary>
        protected override void GuardarExtras(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosUsuario datos)
        {
            Permisos.Exigir("Guardar la carrera y el semestre de un alumno");
            DatosAlumno alumno = (DatosAlumno)datos;
            int filas;
            using (MySqlCommand contar = new MySqlCommand(
                "SELECT COUNT(*) FROM carrera_grupo_semestre WHERE usuarios_numero_identificador = @Id", conexion, transaccion))
            {
                contar.Parameters.AddWithValue("@Id", id);
                filas = Convert.ToInt32(contar.ExecuteScalar());
            }

            // En la base de la escuela grupo y semestre eran NOT NULL (hasta la
            // 10): una fila nueva sin grupo daria "cannot be null", y un semestre
            // vacio en una existente se volveria 0 y chocaria con su clave foranea.
            bool sinGrupo = Esquema.CarreraSinGrupo(conexion, transaccion);
            if (filas == 0 && !sinGrupo)
            {
                throw new ErrorDeDatos("La base de datos todavía no admite estudiantes sin grupo: falta aplicar db/10_red_y_autoregistro.sql. "
                    + "Pide a quien administra el servidor que la aplique.");
            }

            string consulta = filas == 0
                ? "INSERT INTO carrera_grupo_semestre (usuarios_numero_identificador, carreras_idcarreras, grupos_idgrupos, semestres_idsemestres) " +
                  "VALUES (@Id, @Carrera, NULL, @Semestre)"
                : "UPDATE carrera_grupo_semestre SET carreras_idcarreras = @Carrera, semestres_idsemestres = "
                  + (sinGrupo ? "@Semestre" : "COALESCE(@Semestre, semestres_idsemestres)") + " " +
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
