using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// Quien entro con su contrasena del SIIA y espera que la administracion lo
    /// enlace (siia_solicitudes, db/24). Lo guarda el equipo en que entro, con lo
    /// que dio el SIIA; nunca la contrasena.
    /// </summary>
    internal sealed class SolicitudSiia
    {
        public int Id;
        /// <summary>El numero de empleado con que entro al SIIA.</summary>
        public string Numero = "";
        public string Nombre = "";
        public string Paterno = "";
        public string Materno = "";
        public string NombreCompleto = "";
        public string Correo = "";
        /// <summary>'P', 'A', 'E' o "".</summary>
        public string Tipo = "";
        public DateTime PrimerIntento;
        public DateTime UltimoIntento;
        public int Intentos;

        /// <summary>"Zita López Ruiz": el nombre y los apellidos, o el nombre completo si el SIIA solo dio ese.</summary>
        public string NombreParaMostrar
        {
            get
            {
                string partes = string.Join(" ", new[] { Nombre, Paterno, Materno }.Where(p => !string.IsNullOrWhiteSpace(p)));
                return partes.Length > 0 ? partes : NombreCompleto.Length > 0 ? NombreCompleto : "(el SIIA no dio su nombre)";
            }
        }

        /// <summary>El tipo de cuenta del SIIA, en palabras.</summary>
        public string TipoParaMostrar
        {
            get
            {
                switch (Tipo)
                {
                    case "P":
                        return "Docente";
                    case "A":
                        return "Administrativo";
                    case "E":
                        return "Estudiante";
                    default:
                        return "—";
                }
            }
        }
    }

    /// <summary>Lo que ensena Docentes: quien esta enlazado y quien espera (sin db/24, Disponible = false).</summary>
    internal sealed class LecturaEnlaces
    {
        public static readonly LecturaEnlaces Ninguna = new LecturaEnlaces();

        public bool Disponible;
        /// <summary>Los docentes enlazados (su idusuarios) y desde cuando.</summary>
        public Dictionary<int, DateTime> Enlazados = new Dictionary<int, DateTime>();
        /// <summary>Las solicitudes pendientes, la mas reciente primero.</summary>
        public List<SolicitudSiia> Pendientes = new List<SolicitudSiia>();
    }

    /// <summary>
    /// El enlace de cada docente de DigLab con su numero de empleado del SIIA
    /// (db/24). Con el inicio de sesion con el SIIA encendido, un docente entra
    /// solo si la administracion ya enlazo su cuenta (usuarios.siia_enlazado_en).
    /// Si el SIIA acepta un numero sin enlazar, el equipo deja su solicitud
    /// (GuardarSolicitud) y el docente espera (VentanaEnlacePendiente) a que la
    /// administracion la enlace con un docente de la lista (Enlazar): su
    /// numero_identificador pasa a ser el de empleado y conserva sus clases,
    /// sesiones y bitacoras. Nada se enlaza solo; Descartar quita una solicitud
    /// que no corresponde y Desenlazar deshace un enlace hecho por error. Cada
    /// accion de la administracion queda en el historial (db/21).
    ///
    /// Enlazar y desenlazar dejan la contrasena de la cuenta en SinHuella: la
    /// huella que vale sin el SIIA es la de la ultima contrasena que el SIIA le
    /// acepto despues de enlazarla, nunca una de antes.
    /// </summary>
    internal static class EnlacesSiia
    {
        public const string Pendiente = "pendiente";
        public const string Enlazada = "enlazada";
        public const string Descartada = "descartada";

        /// <summary>usuarios.password de una cuenta sin huella del SIIA: ni PBKDF2 ni MD5, Contrasenas.Verificar nunca la acepta.</summary>
        public const string SinHuella = "siia:sin-huella";

        private const int LargoNombre = 100;

        // --- Desde el equipo en que entra el docente -------------------------------

        /// <summary>
        /// La solicitud de ese numero, con lo que dio el SIIA: nueva o, si ya la
        /// habia, al dia (ultimo intento, uno mas) y otra vez pendiente. Lanza si
        /// no se puede guardar (sin db/24 o sin permiso).
        /// </summary>
        public static void GuardarSolicitud(string numero, DatosSiia datos)
        {
            // Primero UPDATE y, si no estaba, INSERT: MariaDB 5.5 pide para un
            // INSERT ... ON DUPLICATE KEY UPDATE permiso de UPDATE tambien en las
            // columnas que no cambia (numero_empleado), y digbit_equipo no lo tiene.
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                if (Escribir(conexion, @"
                    UPDATE siia_solicitudes SET nombre = @Nombre, apellido_paterno = @Paterno, apellido_materno = @Materno,
                           nombre_completo = @Completo, correo = @Correo, tipo_siia = @Tipo, ultimo_intento = NOW(),
                           intentos = intentos + 1, estado = @Pendiente
                     WHERE numero_empleado = @Numero", numero, datos) > 0)
                {
                    return;
                }

                try
                {
                    Escribir(conexion, @"
                        INSERT INTO siia_solicitudes (numero_empleado, nombre, apellido_paterno, apellido_materno, nombre_completo, correo, tipo_siia,
                                                      primer_intento, ultimo_intento, intentos, estado)
                        VALUES (@Numero, @Nombre, @Paterno, @Materno, @Completo, @Correo, @Tipo, NOW(), NOW(), 1, @Pendiente)", numero, datos);
                }
                catch (MySqlException ex) when (ex.Number == 1062)
                {
                    // Otro equipo la dejo a la vez: un intento mas en la suya.
                    Escribir(conexion, @"
                        UPDATE siia_solicitudes SET ultimo_intento = NOW(), intentos = intentos + 1, estado = @Pendiente
                         WHERE numero_empleado = @Numero", numero, datos);
                }
            }
        }

        private static int Escribir(MySqlConnection conexion, string sql, string numero, DatosSiia datos)
        {
            using (MySqlCommand comando = new MySqlCommand(sql, conexion))
            {
                comando.Parameters.AddWithValue("@Numero", numero);
                comando.Parameters.AddWithValue("@Nombre", Dato(datos == null ? null : datos.Nombre, LargoNombre));
                comando.Parameters.AddWithValue("@Paterno", Dato(datos == null ? null : datos.Paterno, LargoNombre));
                comando.Parameters.AddWithValue("@Materno", Dato(datos == null ? null : datos.Materno, LargoNombre));
                comando.Parameters.AddWithValue("@Completo", Dato(datos == null ? null : datos.NombreCompleto, 255));
                comando.Parameters.AddWithValue("@Correo", Dato(datos == null ? null : datos.Correo, 120));
                comando.Parameters.AddWithValue("@Tipo", Dato(datos == null ? null : datos.Tipo, 4));
                comando.Parameters.AddWithValue("@Pendiente", Pendiente);
                return comando.ExecuteNonQuery();
            }
        }

        /// <summary>True si la administracion ya enlazo la cuenta de docente de ese numero. Lanza si no se puede leer.</summary>
        public static bool EstaEnlazado(string numero)
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM usuarios WHERE numero_identificador = @Numero AND fk_tipo_usuario = @Tipo AND siia_enlazado_en IS NOT NULL", conexion))
            {
                comando.Parameters.AddWithValue("@Numero", numero ?? "");
                comando.Parameters.AddWithValue("@Tipo", TiposUsuario.Profesor);
                return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        // --- Lo que lee la administracion (tambien la persona responsable) ----------

        /// <summary>Quien esta enlazado y quien espera. Sin db/24, LecturaEnlaces.Ninguna. Lanza si no se puede leer.</summary>
        public static LecturaEnlaces Leer()
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                if (!Esquema.EnlacesSiia(conexion))
                {
                    return LecturaEnlaces.Ninguna;
                }

                LecturaEnlaces lectura = new LecturaEnlaces { Disponible = true };
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT idusuarios, siia_enlazado_en FROM usuarios WHERE fk_tipo_usuario = @Tipo AND siia_enlazado_en IS NOT NULL", conexion))
                {
                    comando.Parameters.AddWithValue("@Tipo", TiposUsuario.Profesor);
                    using (MySqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            lectura.Enlazados[Convert.ToInt32(lector["idusuarios"], CultureInfo.InvariantCulture)] = Convert.ToDateTime(lector["siia_enlazado_en"]);
                        }
                    }
                }

                lectura.Pendientes = LeerPendientes(conexion, 0);
                return lectura;
            }
        }

        /// <summary>Las pendientes, la del intento mas reciente primero (como mucho cuantas; 0 = todas).</summary>
        private static List<SolicitudSiia> LeerPendientes(MySqlConnection conexion, int cuantas)
        {
            List<SolicitudSiia> pendientes = new List<SolicitudSiia>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT idsiia_solicitudes, numero_empleado, nombre, apellido_paterno, apellido_materno, nombre_completo, correo, tipo_siia,
                       primer_intento, ultimo_intento, intentos
                  FROM siia_solicitudes WHERE estado = @Pendiente ORDER BY ultimo_intento DESC, idsiia_solicitudes DESC"
                + (cuantas > 0 ? " LIMIT " + cuantas.ToString(CultureInfo.InvariantCulture) : ""), conexion))
            {
                comando.Parameters.AddWithValue("@Pendiente", Pendiente);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        pendientes.Add(new SolicitudSiia
                        {
                            Id = Convert.ToInt32(lector["idsiia_solicitudes"], CultureInfo.InvariantCulture),
                            Numero = Texto(lector, "numero_empleado"),
                            Nombre = Texto(lector, "nombre"),
                            Paterno = Texto(lector, "apellido_paterno"),
                            Materno = Texto(lector, "apellido_materno"),
                            NombreCompleto = Texto(lector, "nombre_completo"),
                            Correo = Texto(lector, "correo"),
                            Tipo = Texto(lector, "tipo_siia").ToUpperInvariant(),
                            PrimerIntento = Convert.ToDateTime(lector["primer_intento"]),
                            UltimoIntento = Convert.ToDateTime(lector["ultimo_intento"]),
                            Intentos = Convert.ToInt32(lector["intentos"], CultureInfo.InvariantCulture)
                        });
                    }
                }
            }

            return pendientes;
        }

        /// <summary>
        /// La alerta "Docentes por enlazar (N)" mientras haya pendientes; null si no
        /// hay (o sin db/24). Su momento es el ultimo intento: vuelve a ser nueva
        /// (y a avisar, como mucho una vez por hora) cuando alguien mas entra.
        /// </summary>
        public static Alerta ParaAlertas(MySqlConnection conexion)
        {
            if (!Esquema.EnlacesSiia(conexion))
            {
                return null;
            }

            int cuantas;
            using (MySqlCommand comando = new MySqlCommand("SELECT COUNT(*) FROM siia_solicitudes WHERE estado = @Pendiente", conexion))
            {
                comando.Parameters.AddWithValue("@Pendiente", Pendiente);
                cuantas = Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            if (cuantas == 0)
            {
                return null;
            }

            List<SolicitudSiia> recientes = LeerPendientes(conexion, 3);
            DateTime ultimo = recientes.Count > 0 ? recientes[0].UltimoIntento : DateTime.Now;
            return new Alerta
            {
                Tipo = TipoAlerta.DocentesPorEnlazar,
                Fecha = ultimo.Date,
                Momento = ultimo,
                PorEnlazar = cuantas,
                QuienesPorEnlazar = string.Join(", ", recientes.Select(s => s.Numero + " " + s.NombreParaMostrar))
                    + (cuantas > recientes.Count ? " y " + (cuantas - recientes.Count) + " más" : "")
            };
        }

        // --- Lo que solo hace la administracion -------------------------------------

        /// <summary>
        /// Enlaza la solicitud con ese docente (activo, sin enlazar): su numero pasa
        /// a ser el de empleado, queda enlazado y sin huella hasta que vuelva a
        /// entrar con el SIIA. Todo en una transaccion, con su renglon del historial.
        /// Lo que se le puede decir a la administracion sale como ErrorDeDatos.
        /// </summary>
        public static void Enlazar(SolicitudSiia solicitud, ProfesorInfo docente)
        {
            Permisos.Exigir("Enlazar un docente con el SIIA");
            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ComprobarPendiente(conexion, transaccion, solicitud);

                string numeroAntes;
                bool enlazado;
                bool activo;
                LeerDocente(conexion, transaccion, docente.Id, out numeroAntes, out enlazado, out activo);
                if (enlazado)
                {
                    throw new ErrorDeDatos(docente.NombreCompleto + " ya está enlazado con el SIIA. Si fue un error, desenlázalo antes en Docentes.");
                }

                if (!activo)
                {
                    throw new ErrorDeDatos("La cuenta de " + docente.NombreCompleto + " está dada de baja: reactívala en Docentes antes de enlazarla.");
                }

                string otra = QuienTieneElNumero(conexion, transaccion, solicitud.Numero, docente.Id);
                if (otra != null)
                {
                    throw new ErrorDeDatos("El número " + solicitud.Numero + " ya lo tiene otra cuenta de DigLab (" + otra + "), así que no se puede enlazar. "
                        + "Revisa esa cuenta en Docentes o en Estudiantes.");
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE usuarios SET numero_identificador = @Numero, siia_enlazado_en = NOW(), password = @SinHuella
                     WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo AND siia_enlazado_en IS NULL", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Numero", solicitud.Numero);
                    comando.Parameters.AddWithValue("@SinHuella", SinHuella);
                    comando.Parameters.AddWithValue("@Id", docente.Id);
                    comando.Parameters.AddWithValue("@Tipo", TiposUsuario.Profesor);
                    comando.ExecuteNonQuery();
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE siia_solicitudes SET estado = @Enlazada, usuario_id = @Id, numero_anterior = @Antes, resuelta_en = NOW(), resuelta_por = @Admin
                     WHERE idsiia_solicitudes = @Solicitud", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Enlazada", Enlazada);
                    comando.Parameters.AddWithValue("@Id", docente.Id);
                    comando.Parameters.AddWithValue("@Antes", numeroAntes);
                    comando.Parameters.AddWithValue("@Admin", IdDeLaSesion(conexion, transaccion));
                    comando.Parameters.AddWithValue("@Solicitud", solicitud.Id);
                    comando.ExecuteNonQuery();
                }

                bool cambiaNumero = !string.Equals(numeroAntes, solicitud.Numero, StringComparison.Ordinal);
                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Cuenta,
                    Accion = AccionDeCambio.Enlazar,
                    Tabla = "usuarios",
                    RegistroId = docente.Id,
                    Descripcion = "Docente enlazado con el SIIA: " + docente.NombreCompleto + " entra ahora con su número de empleado " + solicitud.Numero
                        + (cambiaNumero ? " (antes " + numeroAntes + ")" : ""),
                    Antes = HistorialCambios.Datos("Número", numeroAntes, "Enlace con el SIIA", "Sin enlazar"),
                    Despues = HistorialCambios.Datos("Número", solicitud.Numero, "Enlace con el SIIA", "Enlazado",
                        "Nombre en el SIIA", solicitud.NombreParaMostrar)
                });
                transaccion.Commit();
                Log.Info("SIIA: el docente id " + docente.Id + " ('" + numeroAntes + "') quedo enlazado con el numero de empleado '" + solicitud.Numero
                    + "' por " + (Datos_User.getUser() ?? "-") + ".");
            }
        }

        /// <summary>Quita de la lista una solicitud que no corresponde a nadie, con su renglon del historial. Si vuelve a entrar, vuelve a estar pendiente.</summary>
        public static void Descartar(SolicitudSiia solicitud)
        {
            Permisos.Exigir("Descartar una solicitud de enlace con el SIIA");
            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ComprobarPendiente(conexion, transaccion, solicitud);
                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE siia_solicitudes SET estado = @Descartada, resuelta_en = NOW(), resuelta_por = @Admin WHERE idsiia_solicitudes = @Solicitud",
                    conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Descartada", Descartada);
                    comando.Parameters.AddWithValue("@Admin", IdDeLaSesion(conexion, transaccion));
                    comando.Parameters.AddWithValue("@Solicitud", solicitud.Id);
                    comando.ExecuteNonQuery();
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Cuenta,
                    Accion = AccionDeCambio.Descartar,
                    Tabla = "siia_solicitudes",
                    RegistroId = solicitud.Id,
                    Descripcion = "Solicitud de enlace con el SIIA descartada: " + solicitud.Numero + ", " + solicitud.NombreParaMostrar,
                    Antes = HistorialCambios.Datos("Número de empleado", solicitud.Numero, "Nombre en el SIIA", solicitud.NombreParaMostrar,
                        "Intentos", solicitud.Intentos.ToString(CultureInfo.InvariantCulture))
                });
                transaccion.Commit();
                Log.Info("SIIA: solicitud de enlace de '" + solicitud.Numero + "' descartada por " + (Datos_User.getUser() ?? "-") + ".");
            }
        }

        /// <summary>
        /// Deshace el enlace de un docente: vuelve a su numero de antes (o a uno
        /// provisional, si ya era ese o lo tiene otro), queda sin enlazar y sin
        /// huella, y la solicitud de ese numero de empleado vuelve a estar
        /// pendiente para enlazarla con quien es. Devuelve el numero que le queda.
        /// </summary>
        public static string Desenlazar(ProfesorInfo docente)
        {
            Permisos.Exigir("Desenlazar un docente del SIIA");
            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                string numero;
                bool enlazado;
                bool activo;
                LeerDocente(conexion, transaccion, docente.Id, out numero, out enlazado, out activo);
                if (!enlazado)
                {
                    throw new ErrorDeDatos(docente.NombreCompleto + " ya no está enlazado con el SIIA: vuelve a leer la lista (F5).");
                }

                string anterior = null;
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT numero_anterior FROM siia_solicitudes WHERE usuario_id = @Id AND estado = @Enlazada
                     ORDER BY resuelta_en DESC, idsiia_solicitudes DESC LIMIT 1", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", docente.Id);
                    comando.Parameters.AddWithValue("@Enlazada", Enlazada);
                    object valor = comando.ExecuteScalar();
                    anterior = valor == null || valor == DBNull.Value ? null : valor.ToString().Trim();
                }

                string nuevo = !string.IsNullOrEmpty(anterior) && !string.Equals(anterior, numero, StringComparison.OrdinalIgnoreCase)
                    && QuienTieneElNumero(conexion, transaccion, anterior, docente.Id) == null
                    ? anterior : Provisional(conexion, transaccion, docente.Id);

                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE usuarios SET numero_identificador = @Nuevo, siia_enlazado_en = NULL, password = @SinHuella
                     WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Nuevo", nuevo);
                    comando.Parameters.AddWithValue("@SinHuella", SinHuella);
                    comando.Parameters.AddWithValue("@Id", docente.Id);
                    comando.Parameters.AddWithValue("@Tipo", TiposUsuario.Profesor);
                    comando.ExecuteNonQuery();
                }

                // Que el numero de empleado se pueda enlazar otra vez, con quien si es.
                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE siia_solicitudes SET estado = @Pendiente, usuario_id = NULL, numero_anterior = NULL, resuelta_en = NULL, resuelta_por = NULL
                     WHERE numero_empleado = @Numero", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Pendiente", Pendiente);
                    comando.Parameters.AddWithValue("@Numero", numero);
                    comando.ExecuteNonQuery();
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Cuenta,
                    Accion = AccionDeCambio.Desenlazar,
                    Tabla = "usuarios",
                    RegistroId = docente.Id,
                    Descripcion = "Docente desenlazado del SIIA: " + docente.NombreCompleto + " deja el número de empleado " + numero + " y queda con el "
                        + nuevo + "; la solicitud de " + numero + " vuelve a estar por enlazar",
                    Antes = HistorialCambios.Datos("Número", numero, "Enlace con el SIIA", "Enlazado"),
                    Despues = HistorialCambios.Datos("Número", nuevo, "Enlace con el SIIA", "Sin enlazar")
                });
                transaccion.Commit();
                Log.Info("SIIA: el docente id " + docente.Id + " quedo desenlazado del numero de empleado '" + numero + "' (ahora '" + nuevo + "') por "
                    + (Datos_User.getUser() ?? "-") + ".");
                return nuevo;
            }
        }

        // --- Sugerencias por nombre --------------------------------------------------

        /// <summary>
        /// Si ese docente parece el de la solicitud: el mismo numero, o el mismo
        /// nombre (nombre, apellido paterno y materno sin acentos, sin mayusculas y
        /// con los espacios normalizados; el materno puede faltar en uno de los dos
        /// lados si lo demas coincide; si el SIIA solo dio el nombre completo, se
        /// compara entero). Es solo una sugerencia: enlaza la administracion.
        /// </summary>
        public static string Coincidencia(SolicitudSiia solicitud, UsuarioInfo docente)
        {
            if (string.Equals((docente.Matricula ?? "").Trim(), solicitud.Numero, StringComparison.OrdinalIgnoreCase))
            {
                return "Mismo número";
            }

            string nombre = Normalizar(solicitud.Nombre);
            string paterno = Normalizar(solicitud.Paterno);
            string materno = Normalizar(solicitud.Materno);
            string suNombre = Normalizar(docente.Nombre);
            string suPaterno = Normalizar(docente.ApellidoPaterno);
            string suMaterno = Normalizar(docente.ApellidoMaterno);
            bool igual;
            if (nombre.Length > 0 && paterno.Length > 0)
            {
                igual = suNombre == nombre && suPaterno == paterno && (suMaterno == materno || suMaterno.Length == 0 || materno.Length == 0);
            }
            else
            {
                string completo = Normalizar(solicitud.NombreCompleto);
                igual = completo.Length > 0 && Normalizar(suNombre + " " + suPaterno + " " + suMaterno) == completo;
            }

            return igual ? "Mismo nombre" : "";
        }

        /// <summary>Sin acentos, en minusculas y con un espacio entre palabras: "Zita  LÓPEZ" y "zita lopez" son iguales.</summary>
        public static string Normalizar(string texto)
        {
            StringBuilder limpio = new StringBuilder();
            foreach (char c in (texto ?? "").Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    limpio.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
                }
            }

            return Regex.Replace(limpio.ToString(), " {2,}", " ").Trim();
        }

        // --- Interior ------------------------------------------------------------------

        private static void ComprobarPendiente(MySqlConnection conexion, MySqlTransaction transaccion, SolicitudSiia solicitud)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT estado FROM siia_solicitudes WHERE idsiia_solicitudes = @Solicitud FOR UPDATE", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Solicitud", solicitud.Id);
                object estado = comando.ExecuteScalar();
                if (estado == null || estado == DBNull.Value || estado.ToString() != Pendiente)
                {
                    throw new ErrorDeDatos("La solicitud de " + solicitud.Numero + " ya no está por enlazar (la resolvió alguien más): vuelve a abrir la lista.");
                }
            }
        }

        private static void LeerDocente(MySqlConnection conexion, MySqlTransaction transaccion, int id, out string numero, out bool enlazado, out bool activo)
        {
            string columnaActivo = Esquema.TieneBajas(conexion, transaccion) ? "activo" : "1 AS activo";
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT numero_identificador, siia_enlazado_en, " + columnaActivo + " FROM usuarios WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo FOR UPDATE",
                conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                comando.Parameters.AddWithValue("@Tipo", TiposUsuario.Profesor);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    if (!lector.Read())
                    {
                        throw new ErrorDeDatos("Ese docente ya no está en DigLab: vuelve a leer la lista (F5).");
                    }

                    numero = Texto(lector, "numero_identificador");
                    enlazado = lector["siia_enlazado_en"] != DBNull.Value;
                    activo = Convert.ToInt32(lector["activo"], CultureInfo.InvariantCulture) == 1;
                }
            }
        }

        /// <summary>"estudiante Ana Ruiz", si otra cuenta (no la de ese id) ya tiene ese numero; null si esta libre.</summary>
        private static string QuienTieneElNumero(MySqlConnection conexion, MySqlTransaction transaccion, string numero, int salvoId)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT fk_tipo_usuario, nombre, apellido_paterno FROM usuarios WHERE numero_identificador = @Numero AND idusuarios <> @Id LIMIT 1",
                conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Numero", numero);
                comando.Parameters.AddWithValue("@Id", salvoId);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    if (!lector.Read())
                    {
                        return null;
                    }

                    int tipo = Convert.ToInt32(lector["fk_tipo_usuario"], CultureInfo.InvariantCulture);
                    string tipoTexto = tipo == TiposUsuario.Alumno ? "estudiante" : tipo == TiposUsuario.Profesor ? "docente"
                        : tipo == TiposUsuario.Encargado ? "persona responsable de laboratorio" : "administración";
                    return tipoTexto + " " + (Texto(lector, "nombre") + " " + Texto(lector, "apellido_paterno")).Trim();
                }
            }
        }

        /// <summary>"SINSIIA" y su id (con una letra mas si ya lo tiene otra cuenta): un numero provisional, como los del horario real.</summary>
        private static string Provisional(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
            string basico = "SINSIIA" + id.ToString(CultureInfo.InvariantCulture);
            foreach (string candidato in new[] { "" }.Concat("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString())))
            {
                string numero = basico + candidato;
                if (QuienTieneElNumero(conexion, transaccion, numero, id) == null)
                {
                    return numero;
                }
            }

            throw new ErrorDeDatos("No se encontró un número provisional libre para esta cuenta. Cámbiale el número en Docentes y vuelve a intentarlo.");
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

        /// <summary>Un dato del SIIA como se guarda: sin caracteres de control ni fuera del BMP (utf8 de 3 bytes), recortado; NULL si queda vacio.</summary>
        private static object Dato(string valor, int largo)
        {
            StringBuilder limpio = new StringBuilder();
            foreach (char c in (valor ?? "").Trim())
            {
                if (!char.IsControl(c) && !char.IsSurrogate(c))
                {
                    limpio.Append(c);
                }
            }

            string texto = Regex.Replace(limpio.ToString(), @"\s{2,}", " ");
            if (texto.Length > largo)
            {
                texto = texto.Substring(0, largo).TrimEnd();
            }

            return texto.Length == 0 ? (object)DBNull.Value : texto;
        }

        private static string Texto(MySqlDataReader lector, string columna)
        {
            object valor = lector[columna];
            return valor == null || valor == DBNull.Value ? "" : Convert.ToString(valor, CultureInfo.InvariantCulture).Trim();
        }
    }
}
