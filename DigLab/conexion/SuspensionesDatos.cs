using System;
using System.Collections.Generic;
using System.Globalization;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Una suspension de acceso de un estudiante (db/27, suspensiones).</summary>
    internal sealed class Suspension
    {
        public DateTime Desde;
        public DateTime Hasta;
        public string Razon;
        /// <summary>Cuando se quito antes de tiempo; null si no se quito.</summary>
        public DateTime? LevantadaEn;
    }

    /// <summary>
    /// "Suspender acceso..." de Estudiantes: no puede iniciar sesion hasta una fecha,
    /// por una razon que ve al intentarlo. Vale desde el momento en que se aplica,
    /// con el reloj del servidor, y al vencer el plazo vuelve a entrar sin que nadie
    /// haga nada. Su cuenta sigue activa y no se le quita nada. Solo la administracion
    /// suspende y quita; los renglones no se borran. La razon no va al historial
    /// (lo lee la persona responsable y se queda aunque la cuenta se anonimice) ni a
    /// la copia local de los equipos.
    /// </summary>
    internal static class SuspensionesDatos
    {
        // Con su fecha entra en el aviso de la lista (DataGridView corta a 288 caracteres).
        public const int LargoRazon = 200;
        public const int MaximoDias = 365;

        /// <summary>
        /// Condicion para un SELECT sobre usuarios: que esa cuenta tenga una suspension
        /// vigente. Sin parametros: se puede pegar en cualquier consulta.
        /// </summary>
        public const string CondicionSuspendido = @"EXISTS (SELECT 1 FROM suspensiones s_vigente
            WHERE s_vigente.usuarios_idusuarios = u.idusuarios AND s_vigente.levantada_en IS NULL AND s_vigente.hasta > NOW())";

        /// <summary>
        /// La suspension vigente de la cuenta con ese identificador, o null: lo que
        /// mira el inicio de sesion, despues de comprobar quien es. Sin db/27 (o sin
        /// permiso para leerla: lo avisa Esquema en el log) no hay ninguna.
        /// </summary>
        public static Suspension VigentePorIdentificador(MySqlConnection conexion, string numero)
        {
            if (!Esquema.Suspensiones(conexion))
            {
                return null;
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT s.desde, s.hasta, s.razon
                FROM suspensiones s
                INNER JOIN usuarios u ON u.idusuarios = s.usuarios_idusuarios
                WHERE u.numero_identificador = @Numero AND s.levantada_en IS NULL AND s.hasta > NOW()
                ORDER BY s.hasta DESC LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Numero", numero ?? "");
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    return lector.Read() ? Leer(lector) : null;
                }
            }
        }

        /// <summary>Todas las de un estudiante, de la mas reciente a la mas antigua: lo que se le entrega al exportar sus datos. Lanza si no hay servidor.</summary>
        public static List<Suspension> DelEstudiante(int idAlumno)
        {
            List<Suspension> lista = new List<Suspension>();
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                if (!Esquema.Suspensiones(conexion))
                {
                    return lista;
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT s.desde, s.hasta, s.razon, s.levantada_en
                    FROM suspensiones s
                    WHERE s.usuarios_idusuarios = @Id
                    ORDER BY s.desde DESC, s.idsuspensiones DESC", conexion))
                {
                    comando.Parameters.AddWithValue("@Id", idAlumno);
                    using (MySqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            Suspension suspension = Leer(lector);
                            int columna = lector.GetOrdinal("levantada_en");
                            suspension.LevantadaEn = lector.IsDBNull(columna) ? (DateTime?)null : lector.GetDateTime(columna);
                            lista.Add(suspension);
                        }
                    }
                }
            }

            return lista;
        }

        /// <summary>La hora del servidor, para decir antes de guardar hasta cuando seria. Lanza si no hay servidor.</summary>
        public static DateTime AhoraEnElServidor()
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
            {
                return Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Le suspende el acceso desde ahora, por esos dias. Devuelve hasta cuando.
        /// Lo que no se puede (ya esta suspendido, la cuenta ya no esta activa, falta la
        /// migracion...) sale como ErrorDeDatos.
        /// </summary>
        public static DateTime Suspender(int idAlumno, int dias, string razon)
        {
            Permisos.Exigir("Suspender el acceso del usuario id " + idAlumno);
            string limpia = HistorialCambios.Limpiar(razon, LargoRazon);
            if (limpia == null)
            {
                throw new ErrorDeDatos("Escribe la razón: la verá al intentar entrar.");
            }

            if (dias < 1 || dias > MaximoDias)
            {
                throw new ErrorDeDatos("La suspensión va de 1 a " + MaximoDias + " días.");
            }

            DateTime hasta;
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTabla(conexion);
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    string matricula = LeerEstudianteQueSePuedeSuspender(conexion, transaccion, idAlumno);
                    using (MySqlCommand contar = new MySqlCommand(@"
                        SELECT COUNT(*) FROM suspensiones
                        WHERE usuarios_idusuarios = @Id AND levantada_en IS NULL AND hasta > NOW()", conexion, transaccion))
                    {
                        contar.Parameters.AddWithValue("@Id", idAlumno);
                        if (Convert.ToInt32(contar.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                        {
                            throw new ErrorDeDatos("Ya tiene el acceso suspendido. Para cambiar el plazo, quita la suspensión y vuelve a suspenderlo.");
                        }
                    }

                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO suspensiones (usuarios_idusuarios, desde, hasta, razon, creada_por)
                        VALUES (@Id, NOW(), DATE_ADD(NOW(), INTERVAL @Dias DAY), @Razon, @Por)", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", idAlumno);
                        comando.Parameters.AddWithValue("@Dias", dias);
                        comando.Parameters.AddWithValue("@Razon", limpia);
                        comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, transaccion));
                        comando.ExecuteNonQuery();
                    }

                    using (MySqlCommand leer = new MySqlCommand("SELECT MAX(hasta) FROM suspensiones WHERE usuarios_idusuarios = @Id", conexion, transaccion))
                    {
                        leer.Parameters.AddWithValue("@Id", idAlumno);
                        hasta = Convert.ToDateTime(leer.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }

                    string plazo = dias == 1 ? "1 día" : dias.ToString(CultureInfo.InvariantCulture) + " días";
                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.Cuenta,
                        Accion = AccionDeCambio.Suspender,
                        Tabla = "usuarios",
                        RegistroId = idAlumno,
                        // Sin la razon ni su nombre: ver el resumen de arriba.
                        Descripcion = "Acceso suspendido a la matrícula " + matricula + " por " + plazo + ", hasta el " + Fecha(hasta) + ".",
                        Antes = HistorialCambios.Datos("Acceso", "Normal"),
                        Despues = HistorialCambios.Datos("Acceso", "Suspendido", "Hasta", Fecha(hasta))
                    });
                    transaccion.Commit();
                }
            }

            Log.Info("Acceso suspendido al usuario id " + idAlumno + " por " + dias + " dias.");
            return hasta;
        }

        /// <summary>Le devuelve el acceso antes de tiempo. ErrorDeDatos si ya no estaba suspendido (ya vencio, o otra persona se la quito).</summary>
        public static void Quitar(int idAlumno)
        {
            Permisos.Exigir("Quitar la suspension del usuario id " + idAlumno);
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTabla(conexion);
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    string matricula;
                    using (MySqlCommand comando = new MySqlCommand("SELECT numero_identificador FROM usuarios WHERE idusuarios = @Id", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", idAlumno);
                        matricula = Convert.ToString(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }

                    int quitadas;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE suspensiones SET levantada_en = NOW(), levantada_por = @Por
                        WHERE usuarios_idusuarios = @Id AND levantada_en IS NULL AND hasta > NOW()", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", idAlumno);
                        comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, transaccion));
                        quitadas = comando.ExecuteNonQuery();
                    }

                    if (quitadas == 0)
                    {
                        throw new ErrorDeDatos("Ya no tiene el acceso suspendido: el plazo terminó o alguien más le quitó la suspensión.");
                    }

                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.Cuenta,
                        Accion = AccionDeCambio.QuitarSuspension,
                        Tabla = "usuarios",
                        RegistroId = idAlumno,
                        Descripcion = "Se quitó antes de tiempo la suspensión de la matrícula " + matricula + ".",
                        Antes = HistorialCambios.Datos("Acceso", "Suspendido"),
                        Despues = HistorialCambios.Datos("Acceso", "Normal")
                    });
                    transaccion.Commit();
                }
            }

            Log.Info("Se quito la suspension del usuario id " + idAlumno + ".");
        }

        /// <summary>Al anonimizar una cuenta, la razon puede llevar su nombre: se vacia. Las fechas quedan. Va en la transaccion de Anonimizar.</summary>
        public static void VaciarRazones(MySqlConnection conexion, MySqlTransaction transaccion, int idAlumno)
        {
            if (!Esquema.Suspensiones(conexion, transaccion))
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "UPDATE suspensiones SET razon = '' WHERE usuarios_idusuarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idAlumno);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>El texto del aviso al que intenta entrar. La razon, solo si queda (al anonimizar se vacia).</summary>
        public static string TextoParaElEstudiante(Suspension suspension)
        {
            return "Tu acceso a DigLab está suspendido hasta el " + Fecha(suspension.Hasta) + "."
                + (string.IsNullOrWhiteSpace(suspension.Razon) ? "" : Environment.NewLine + Environment.NewLine + "Razón: " + suspension.Razon.Trim())
                + Environment.NewLine + Environment.NewLine + "Si crees que es un error, acude a la administración de DigLab.";
        }

        /// <summary>"13/11/2026 a las 10:32".</summary>
        public static string Fecha(DateTime fecha)
        {
            return fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " a las " + fecha.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static Suspension Leer(MySqlDataReader lector)
        {
            return new Suspension
            {
                Desde = lector.GetDateTime("desde"),
                Hasta = lector.GetDateTime("hasta"),
                Razon = lector.GetString("razon")
            };
        }

        private static void ExigirTabla(MySqlConnection conexion)
        {
            if (!Esquema.Suspensiones(conexion))
            {
                throw new ErrorDeDatos("Todavía no se puede suspender el acceso: a la base de datos le falta una actualización (db/27).");
            }
        }

        /// <summary>Su matricula, si es una cuenta de estudiante activa y sin anonimizar; si no, ErrorDeDatos. La lista de la pantalla puede ser de antes.</summary>
        private static string LeerEstudianteQueSePuedeSuspender(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
            string columnaActivo = Esquema.TieneBajas(conexion, transaccion) ? "activo" : "1 AS activo";
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT numero_identificador, fk_tipo_usuario, " + columnaActivo + " FROM usuarios WHERE idusuarios = @Id FOR UPDATE", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    if (!lector.Read() || Convert.ToInt32(lector["fk_tipo_usuario"], CultureInfo.InvariantCulture) != TiposUsuario.Alumno)
                    {
                        throw new ErrorDeDatos("Esa cuenta de estudiante ya no existe. Actualiza la lista con F5.");
                    }

                    if (Convert.ToInt32(lector["activo"], CultureInfo.InvariantCulture) != 1)
                    {
                        throw new ErrorDeDatos("Esa cuenta está dada de baja: ya no puede entrar. Actualiza la lista con F5.");
                    }

                    return Convert.ToString(lector["numero_identificador"], CultureInfo.InvariantCulture);
                }
            }
        }

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
