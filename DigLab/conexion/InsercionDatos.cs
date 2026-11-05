using MySql.Data.MySqlClient;
using DigLab.conexion;
using DigLab.Infraestructura;
using System;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace DigLab
{
    public class InsercionDatos
    {
        private Conexion mconexion;

        public InsercionDatos()
        {
            mconexion = new Conexion();
        }

        public bool InsertarUsuario(string nombre, string apellidoPaterno, string apellidoMaterno, string matricula, string contraseña, int tipoUsuario, string correo)
        {
            try
            {
                // PBKDF2 con sal aleatoria (ver Contrasenas): nunca la contrasena en claro.
                contraseña = Contrasenas.Hash(contraseña);

                // Realizar la inserción en la base de datos
                string consulta = "INSERT INTO usuarios (nombre, apellido_paterno, apellido_materno, numero_identificador, password, fk_tipo_usuario, correo) " +
                                  "VALUES (@Nombre, @ApellidoPaterno, @ApellidoMaterno, @Matricula, @Password, @TipoUsuario, @correo)";

                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    if (conexion != null)
                    {
                        using (MySqlCommand mysqlcomand = new MySqlCommand(consulta, conexion))
                        {
                            mysqlcomand.Parameters.AddWithValue("@Nombre", nombre);
                            mysqlcomand.Parameters.AddWithValue("@ApellidoPaterno", apellidoPaterno);
                            mysqlcomand.Parameters.AddWithValue("@ApellidoMaterno", apellidoMaterno);
                            mysqlcomand.Parameters.AddWithValue("@Matricula", matricula);
                            mysqlcomand.Parameters.AddWithValue("@Password", contraseña);
                            mysqlcomand.Parameters.AddWithValue("@TipoUsuario", tipoUsuario);
                            mysqlcomand.Parameters.AddWithValue("@correo", correo);

                            mysqlcomand.ExecuteNonQuery();

                            return true; // Inserción exitosa
                        }
                    }
                    else
                    {
                        MessageBox.Show(MensajeDeFallo.SinConexion("No se pudo registrar la cuenta", MensajeDeFallo.AvisaALaAdministracion),
                            "DigLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false; // Error al conectar a la base de datos
                    }
                }
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Registrar la cuenta " + matricula, ex);
                MessageBox.Show(MensajeDeFallo.Explicar("No se pudo registrar la cuenta", ex, MensajeDeFallo.AvisaALaAdministracion),
                    "DigLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false; // Error durante la inserción
            }
        }

        // hora_registro, fecha y hora_entrada las pone el SERVIDOR (NOW, CURDATE,
        // CURTIME): son la ventana de validez del codigo y no pueden depender del
        // reloj del equipo del profesor. Solo la hora de salida viaja como parametro.    }
}
