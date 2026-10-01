using System;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Lo comun a alumnos y profesores en las pantallas del administrador.</summary>
    public class UsuarioInfo
    {
        public int Id;
        /// <summary>numero_identificador: matricula del alumno o numero de empleado del profesor.</summary>
        public string Matricula;
        public string Nombre;
        public string ApellidoPaterno;
        public string ApellidoMaterno;
        public string Correo;
        public bool Activo;
        public DateTime? BajaEn;

        public string NombreCompleto
        {
            get
            {
                return string.Join(" ", new[] { Nombre, ApellidoPaterno, ApellidoMaterno }
                    .Where(p => !string.IsNullOrWhiteSpace(p)));
            }
        }

        /// <summary>"Barrera Islas, Hugo": como en las listas de la escuela, y como se ordena.</summary>
        public string NombreParaLista
        {
            get
            {
                string apellidos = string.Join(" ", new[] { ApellidoPaterno, ApellidoMaterno }
                    .Where(p => !string.IsNullOrWhiteSpace(p)));
                return string.IsNullOrWhiteSpace(Nombre) ? apellidos : apellidos + ", " + Nombre;
            }
        }
    }

    /// <summary>Lo que se guarda de cualquier usuario al darlo de alta o editarlo.</summary>
    public class DatosUsuario
    {
        public string Matricula;
        public string Nombre;
        public string ApellidoPaterno;
        public string ApellidoMaterno;
        public string Correo;
    }

    /// <summary>Un error que se le puede ensenar tal cual al administrador.</summary>
    public class ErrorDeDatos : Exception
    {
        public ErrorDeDatos(string mensaje) : base(mensaje)
        {
        }
    }

    /// <summary>
    /// Alta, edicion, contrasena y baja de usuarios de un tipo (alumno o
    /// profesor). No muestra mensajes: lanza la excepcion y la pantalla decide.
    /// Nadie se borra desde aqui: se da de baja (db/08_migracion_bajas.sql),
    /// porque las claves foraneas borrarian en cascada su historial.
    /// </summary>
    internal abstract class UsuariosDatos
    {
        protected const int ErrorClaveDuplicada = 1062;

        protected readonly Conexion mconexion = new Conexion();

        /// <summary>1 alumno, 2 profesor (usuarios.fk_tipo_usuario).</summary>
        protected abstract int TipoUsuario { get; }

        /// <summary>"matrícula" o "número de empleado", para los mensajes.</summary>
        public abstract string NombreIdentificador { get; }

        /// <summary>False si la base aun no tiene la migracion 08: se lista, pero no se da de baja a nadie.</summary>
        public bool BajasDisponibles { get; protected set; }

        public void Crear(DatosUsuario datos, string contrasena)
        {
            string hash = Contrasenas.Hash(contrasena);
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    int id;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO usuarios (numero_identificador, nombre, apellido_paterno, apellido_materno, correo, password, fk_tipo_usuario)
                        VALUES (@Matricula, @Nombre, @Paterno, @Materno, @Correo, @Password, @Tipo);
                        SELECT LAST_INSERT_ID();", conexion, transaccion))
                    {
                        ParametrosUsuario(comando, datos);
                        comando.Parameters.AddWithValue("@Password", hash);
                        comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                        id = Convert.ToInt32(comando.ExecuteScalar());
                    }

                    GuardarExtras(conexion, transaccion, id, datos);
                    transaccion.Commit();
                    Log.Info("Alta del usuario " + datos.Matricula + " (tipo " + TipoUsuario + ") por " + Quien() + ".");
                }
                catch (MySqlException ex) when (ex.Number == ErrorClaveDuplicada)
                {
                    transaccion.Rollback();
                    throw new ErrorDeDatos("Ya hay un usuario con " + NombreIdentificador + " " + datos.Matricula + ".");
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }
        }

        public void Actualizar(int id, DatosUsuario datos)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE usuarios
                           SET numero_identificador = @Matricula, nombre = @Nombre, apellido_paterno = @Paterno,
                               apellido_materno = @Materno, correo = @Correo
                         WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo", conexion, transaccion))
                    {
                        ParametrosUsuario(comando, datos);
                        comando.Parameters.AddWithValue("@Id", id);
                        comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                        comando.ExecuteNonQuery();
                    }

                    GuardarExtras(conexion, transaccion, id, datos);
                    transaccion.Commit();
                    Log.Info("Cambios en el usuario " + datos.Matricula + " (id " + id + ") por " + Quien() + ".");
                }
                catch (MySqlException ex) when (ex.Number == ErrorClaveDuplicada)
                {
                    transaccion.Rollback();
                    throw new ErrorDeDatos("Ya hay otro usuario con " + NombreIdentificador + " " + datos.Matricula + ".");
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Lo que cada tipo guarda ademas de la fila de usuarios, en la misma transaccion.</summary>
        protected virtual void GuardarExtras(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosUsuario datos)
        {
        }

        public void RestablecerContrasena(int id, string nueva)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "UPDATE usuarios SET password = @Password WHERE idusuarios = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Password", Contrasenas.Hash(nueva));
                comando.Parameters.AddWithValue("@Id", id);
                comando.ExecuteNonQuery();
            }

            Log.Info("Contrasena restablecida para el usuario id " + id + " por " + Quien() + ".");
        }

        public void DarDeBaja(int id)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                CambiarEstado(conexion, null, id, false);
            }
        }

        public void Reactivar(int id)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                CambiarEstado(conexion, null, id, true);
            }
        }

        protected void CambiarEstado(MySqlConnection conexion, MySqlTransaction transaccion, int id, bool activo)
        {
            if (!Esquema.TieneBajas(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base de datos todavía no admite bajas: falta aplicar db/08_migracion_bajas.sql.");
            }

            using (MySqlCommand comando = new MySqlCommand(activo
                ? "UPDATE usuarios SET activo = 1, baja_en = NULL WHERE idusuarios = @Id"
                : "UPDATE usuarios SET activo = 0, baja_en = NOW() WHERE idusuarios = @Id AND activo = 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                comando.ExecuteNonQuery();
            }

            Log.Info((activo ? "Reactivado" : "Dado de baja") + " el usuario id " + id + " por " + Quien() + ".");
        }

        /// <summary>Columnas de estado para un SELECT de usuarios u, sirva o no la base para bajas.</summary>
        protected string ColumnasEstado(MySqlConnection conexion)
        {
            BajasDisponibles = Esquema.TieneBajas(conexion);
            return BajasDisponibles ? "u.activo, u.baja_en" : "1 AS activo, NULL AS baja_en";
        }

        /// <summary>Rellena los campos comunes de un usuario leido con ColumnasEstado.</summary>
        protected static void LeerComunes(MySqlDataReader reader, UsuarioInfo usuario)
        {
            usuario.Id = Convert.ToInt32(reader["idusuarios"]);
            usuario.Matricula = Texto(reader, "numero_identificador");
            usuario.Nombre = Texto(reader, "nombre");
            usuario.ApellidoPaterno = Texto(reader, "apellido_paterno");
            usuario.ApellidoMaterno = Texto(reader, "apellido_materno");
            usuario.Correo = Texto(reader, "correo");
            usuario.Activo = Convert.ToInt32(reader["activo"]) == 1;
            usuario.BajaEn = Fecha(reader, "baja_en");
        }

        private static void ParametrosUsuario(MySqlCommand comando, DatosUsuario datos)
        {
            comando.Parameters.AddWithValue("@Matricula", datos.Matricula);
            comando.Parameters.AddWithValue("@Nombre", datos.Nombre);
            comando.Parameters.AddWithValue("@Paterno", datos.ApellidoPaterno);
            // apellido_materno es NOT NULL: vacio. El correo, en cambio, NULL si no
            // hay: en la base del TESCHI es UNICO (correo_UNIQUE) y admite NULL, asi
            // que con "" el segundo usuario sin correo chocaba con el primero.
            comando.Parameters.AddWithValue("@Materno", datos.ApellidoMaterno ?? "");
            comando.Parameters.AddWithValue("@Correo", string.IsNullOrWhiteSpace(datos.Correo) ? (object)DBNull.Value : datos.Correo.Trim());
        }

        protected static string Quien()
        {
            return Datos_User.getUser() ?? "-";
        }

        protected static string Texto(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? "" : valor.ToString();
        }

        protected static int? Entero(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor);
        }

        protected static DateTime? Fecha(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor);
        }
    }
}
