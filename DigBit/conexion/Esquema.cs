using System;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>
    /// Que migraciones tiene la base a la que se conecta. La aplicacion nueva
    /// puede llegar a los equipos antes de que se migre la base: en vez de romper
    /// el inicio de sesion con "Unknown column", se comporta como antes hasta que
    /// la columna exista.
    /// </summary>
    internal static class Esquema
    {
        private static readonly Columna Bajas = new Columna("usuarios", "activo",
            "La base no tiene usuarios.activo: falta db/08_migracion_bajas.sql. "
            + "Hasta aplicarla no se puede dar de baja a nadie y todos pueden entrar.");

        private static readonly Columna LaboratoriosActivos = new Columna("laboratorios", "activo",
            "La base no tiene laboratorios.activo: falta db/08_migracion_bajas.sql. "
            + "Hasta aplicarla solo se pueden quitar los laboratorios que nunca se usaron.");

        private static readonly Columna CodigosDeProfesor = new Columna("usuarios", "codigo_clase",
            "La base no tiene usuarios.codigo_clase: falta db/09_codigo_por_profesor.sql. "
            + "Hasta aplicarla cada clase sigue con su propio codigo.");

        /// <summary>True si usuarios tiene activo y baja_en (db/08_migracion_bajas.sql).</summary>
        public static bool TieneBajas(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return Bajas.Existe(conexion, transaccion);
        }

        /// <summary>True si cada profesor tiene un codigo para todas sus clases (db/09_codigo_por_profesor.sql).</summary>
        public static bool CodigoPorProfesor(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return CodigosDeProfesor.Existe(conexion, transaccion);
        }

        /// <summary>True si laboratorios tiene activo (db/08_migracion_bajas.sql).</summary>
        public static bool LaboratoriosDesactivables(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return LaboratoriosActivos.Existe(conexion, transaccion);
        }

        /// <summary>
        /// Una columna que llega con una migracion. Una vez vista, no se vuelve a
        /// preguntar; mientras falte, se pregunta en cada llamada, para notar la
        /// migracion sin reiniciar.
        /// </summary>
        private class Columna
        {
            private readonly string tabla;
            private readonly string nombre;
            private readonly string aviso;
            private readonly object candado = new object();
            private bool existe;
            private bool avisado;

            public Columna(string tabla, string nombre, string aviso)
            {
                this.tabla = tabla;
                this.nombre = nombre;
                this.aviso = aviso;
            }

            public bool Existe(MySqlConnection conexion, MySqlTransaction transaccion)
            {
                lock (candado)
                {
                    if (existe)
                    {
                        return true;
                    }
                }

                bool hay;
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Tabla AND COLUMN_NAME = @Columna", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Tabla", tabla);
                    comando.Parameters.AddWithValue("@Columna", nombre);
                    hay = Convert.ToInt32(comando.ExecuteScalar()) > 0;
                }

                lock (candado)
                {
                    existe = hay;
                    if (!hay && !avisado)
                    {
                        avisado = true;
                        Log.Aviso(aviso);
                    }
                }

                return hay;
            }
        }
    }
}
