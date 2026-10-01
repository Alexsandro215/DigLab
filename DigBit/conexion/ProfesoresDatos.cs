using System;
using System.Collections.Generic;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Un profesor tal como lo ve la pantalla de Profesores del administrador.</summary>
    public class ProfesorInfo : UsuarioInfo
    {
        /// <summary>Clases activas y vigentes hoy o mas adelante.</summary>
        public int ClasesActivas;
        /// <summary>Laboratorios donde tienen horario esas clases, separados por comas.</summary>
        public string Laboratorios;
        /// <summary>Sesiones (codigos_accesos) de todas sus clases, de siempre.</summary>
        public int Sesiones;
        public DateTime? UltimaSesion;
        /// <summary>El codigo que teclean sus alumnos, el mismo en todas sus clases (db/09).</summary>
        public string Codigo;
    }

    /// <summary>Lo que se guarda de un profesor: lo comun y su codigo.</summary>
    public class DatosProfesor : DatosUsuario
    {
        public string Codigo;
    }

    /// <summary>
    /// El codigo de cada profesor (usuarios.codigo_clase, db/09). Es uno para
    /// todas sus clases y se copia a clases.codigo, que es lo que buscan el
    /// servidor y la copia local al resolver lo que teclea el alumno.
    /// </summary>
    internal static class CodigosDeProfesor
    {
        public const int Largo = 5;

        public static string Leer(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT codigo_clase FROM usuarios WHERE idusuarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idProfesor);
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? null : valor.ToString();
            }
        }

        /// <summary>
        /// Libre para ese profesor: no lo tiene otro profesor ni lo usa ninguna
        /// clase de otro (datos de antes de la migracion). Sin distinguir
        /// mayusculas: el alumno puede teclearlo de cualquier forma.
        /// </summary>
        public static bool Libre(MySqlConnection conexion, MySqlTransaction transaccion, string codigo, int idProfesor)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT (SELECT COUNT(*) FROM usuarios WHERE LOWER(codigo_clase) = LOWER(@Codigo) AND idusuarios <> @Id)
                     + (SELECT COUNT(*) FROM clases WHERE LOWER(codigo) = LOWER(@Codigo) AND usuarios_idusuarios <> @Id)", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                comando.Parameters.AddWithValue("@Id", idProfesor);
                return Convert.ToInt32(comando.ExecuteScalar()) == 0;
            }
        }

        public static string Proponer(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor)
        {
            for (int intento = 0; intento < 50; intento++)
            {
                string codigo = CodigoGenerador.GenerarCodigoAleatorio(Largo);
                if (Libre(conexion, transaccion, codigo, idProfesor))
                {
                    return codigo;
                }
            }

            throw new InvalidOperationException("No se pudo proponer un codigo libre.");
        }

        /// <summary>Guarda el codigo del profesor y lo pone en todas sus clases.</summary>
        public static void Asignar(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor, string codigo)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                UPDATE usuarios SET codigo_clase = @Codigo WHERE idusuarios = @Id;
                UPDATE clases SET codigo = @Codigo WHERE usuarios_idusuarios = @Id;", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                comando.Parameters.AddWithValue("@Id", idProfesor);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>El codigo del profesor; si aun no tiene, se le da uno libre y se guarda.</summary>
        public static string ObtenerOCrear(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor)
        {
            string codigo = Leer(conexion, transaccion, idProfesor);
            if (!string.IsNullOrEmpty(codigo))
            {
                return codigo;
            }

            codigo = Proponer(conexion, transaccion, idProfesor);
            Asignar(conexion, transaccion, idProfesor, codigo);
            Log.Info("Codigo " + codigo + " asignado al profesor id " + idProfesor + " al crearle su primera clase.");
            return codigo;
        }
    }

    /// <summary>Datos de la pantalla de Profesores.</summary>
    internal class ProfesoresDatos : UsuariosDatos
    {
        // Una clase cuenta como vigente si esta activa y no ha terminado su periodo.
        private const string ClaseVigente = "c.activa = 1 AND c.vigente_hasta >= CURDATE()";

        protected override int TipoUsuario
        {
            get { return 2; }
        }

        public override string NombreIdentificador
        {
            get { return "el número de empleado"; }
        }

        /// <summary>False si la base aun no tiene la migracion 09: cada clase sigue con su propio codigo.</summary>
        public bool CodigoPorProfesor { get; private set; }

        /// <summary>Un codigo libre para ese profesor (0 si aun no existe).</summary>
        public string ProponerCodigo(int idProfesor)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return CodigosDeProfesor.Proponer(conexion, null, idProfesor);
            }
        }

        /// <summary>El codigo se guarda con el profesor y se copia a todas sus clases.</summary>
        protected override void GuardarExtras(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosUsuario datos)
        {
            DatosProfesor profesor = datos as DatosProfesor;
            if (profesor == null || !Esquema.CodigoPorProfesor(conexion, transaccion))
            {
                return;
            }

            string codigo = (profesor.Codigo ?? "").Trim();
            if (!CodigosDeProfesor.Libre(conexion, transaccion, codigo, id))
            {
                throw new ErrorDeDatos("El código " + codigo + " ya lo tiene otro profesor.");
            }

            string anterior = CodigosDeProfesor.Leer(conexion, transaccion, id);
            if (!string.Equals(anterior, codigo, StringComparison.Ordinal))
            {
                CodigosDeProfesor.Asignar(conexion, transaccion, id, codigo);
                Log.Info("Codigo del profesor " + profesor.Matricula + ": " + (anterior ?? "(ninguno)") + " -> " + codigo + ", por " + Quien() + ".");
            }
        }

        public List<ProfesorInfo> Listar()
        {
            List<ProfesorInfo> lista = new List<ProfesorInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                CodigoPorProfesor = Esquema.CodigoPorProfesor(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT u.idusuarios, u.numero_identificador, u.nombre, u.apellido_paterno, u.apellido_materno, u.correo,
                           " + (CodigoPorProfesor ? "u.codigo_clase" : "NULL AS codigo_clase") + @",
                           " + ColumnasEstado(conexion) + @",
                           (SELECT COUNT(*) FROM clases c WHERE c.usuarios_idusuarios = u.idusuarios AND " + ClaseVigente + @") AS clases_activas,
                           (SELECT GROUP_CONCAT(DISTINCT l.nombre_laboratorio ORDER BY l.nombre_laboratorio SEPARATOR ', ')
                              FROM clases c
                              INNER JOIN horarios h     ON h.clases_idclases = c.idclases
                              INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                             WHERE c.usuarios_idusuarios = u.idusuarios AND " + ClaseVigente + @") AS laboratorios,
                           (SELECT COUNT(*) FROM codigos_accesos ca WHERE ca.usuarios_idusuarios = u.idusuarios) AS sesiones,
                           (SELECT MAX(COALESCE(ca.fecha, DATE(ca.hora_registro))) FROM codigos_accesos ca
                             WHERE ca.usuarios_idusuarios = u.idusuarios) AS ultima
                    FROM usuarios u
                    WHERE u.fk_tipo_usuario = @Tipo
                    ORDER BY u.apellido_paterno, u.apellido_materno, u.nombre", conexion))
                {
                    comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ProfesorInfo profesor = new ProfesorInfo
                            {
                                ClasesActivas = Convert.ToInt32(reader["clases_activas"]),
                                Laboratorios = Texto(reader, "laboratorios"),
                                Sesiones = Convert.ToInt32(reader["sesiones"]),
                                UltimaSesion = Fecha(reader, "ultima"),
                                Codigo = Texto(reader, "codigo_clase")
                            };
                            LeerComunes(reader, profesor);
                            lista.Add(profesor);
                        }
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Da de baja al profesor y, si se pide, desactiva sus clases vigentes para
        /// que su codigo deje de aceptarse en los laboratorios. Una clase con
        /// sesiones no puede cambiar de profesor (ver DialogoClase): si un suplente
        /// sigue con el grupo, se le crea una clase nueva en Horarios.
        /// </summary>
        public int DarDeBaja(int id, bool desactivarClases)
        {
            int desactivadas = 0;
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    CambiarEstado(conexion, transaccion, id, false);
                    if (desactivarClases)
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "UPDATE clases c SET c.activa = 0 WHERE c.usuarios_idusuarios = @Id AND " + ClaseVigente, conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Id", id);
                            desactivadas = comando.ExecuteNonQuery();
                        }
                    }

                    transaccion.Commit();
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }

            if (desactivadas > 0)
            {
                Log.Info("Desactivadas " + desactivadas + " clase(s) del profesor id " + id + " al darlo de baja.");
            }

            return desactivadas;
        }
    }
}
