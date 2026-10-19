using System.Collections.Generic;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Un encargado de laboratorio tal como lo ve su pantalla: solo lo comun a cualquier usuario.</summary>
    public class EncargadoInfo : UsuarioInfo
    {
    }

    /// <summary>
    /// Alta, edicion, contrasena y baja de encargados de laboratorio (tipo 4,
    /// db/18): entran al panel del administrador en solo consulta (Permisos). Lo
    /// comun lo hace UsuariosDatos; no guardan nada mas que su fila de usuarios.
    /// </summary>
    internal class EncargadosDatos : UsuariosDatos
    {
        protected override int TipoUsuario
        {
            get { return TiposUsuario.Encargado; }
        }

        public override string NombreIdentificador
        {
            get { return "el número de empleado"; }
        }

        protected override string NombreDelTipo
        {
            get { return "responsable de laboratorio"; }
        }

        /// <summary>False si la base aun no tiene el tipo 4 (falta db/18): se lista, pero no se da de alta a nadie.</summary>
        public bool AltaDisponible { get; private set; }

        /// <summary>
        /// False si la base no tiene la cuenta de solo consulta (consulta_clave en
        /// configuracion): db/18 se aplico sin root. Los encargados se pueden dar
        /// de alta, pero no entran hasta que root la aplique.
        /// </summary>
        public bool PuedenEntrar { get; private set; }

        public List<EncargadoInfo> Listar()
        {
            List<EncargadoInfo> lista = new List<EncargadoInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                AltaDisponible = Esquema.TipoEncargado(conexion);
                PuedenEntrar = true;
                if (Esquema.Ajustes(conexion))
                {
                    using (MySqlCommand comando = new MySqlCommand(
                        "SELECT COUNT(*) FROM configuracion WHERE clave = 'consulta_clave' AND valor IS NOT NULL AND valor <> ''", conexion))
                    {
                        PuedenEntrar = System.Convert.ToInt32(comando.ExecuteScalar()) > 0;
                    }
                }
                else
                {
                    PuedenEntrar = false;
                }
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT u.idusuarios, u.numero_identificador, u.nombre, u.apellido_paterno, u.apellido_materno, u.correo,
                           " + ColumnasEstado(conexion) + @"
                    FROM usuarios u
                    WHERE u.fk_tipo_usuario = @Tipo
                    ORDER BY u.apellido_paterno, u.apellido_materno, u.nombre", conexion))
                {
                    comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            EncargadoInfo encargado = new EncargadoInfo();
                            LeerComunes(reader, encargado);
                            lista.Add(encargado);
                        }
                    }
                }
            }

            return lista;
        }
    }
}
