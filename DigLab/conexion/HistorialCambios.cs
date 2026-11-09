using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Que se cambio: historial_cambios.tipo (db/21).</summary>
    internal static class TipoDeCambio
    {
        public const string Franja = "franja";
        public const string Clase = "clase";
        public const string Cancelacion = "cancelacion";
        public const string Extra = "extra";
        public const string Bitacora = "bitacora";
        public const string Laboratorio = "laboratorio";
        public const string FormaDeEntrar = "forma_de_entrar";
        /// <summary>Una cuenta de estudiante o docente: anonimizarla (derechos ARCO), enlazarla con el SIIA (o deshacerlo), o crearla o completar su carrera con lo que da el SIIA.</summary>
        public const string Cuenta = "cuenta";
        /// <summary>
        /// Un horario entero que se aplico (o cuya aplicacion se deshizo) desde
        /// Horarios > Aplicacion: un solo renglon para todas sus clases, no uno por clase.
        /// </summary>
        public const string Horario = "horario";
        /// <summary>
        /// Horas que le faltan a una clase en el horario en uso (db/25): las que se
        /// crean al reemplazarla o al quitarlas, y como se resuelven.
        /// </summary>
        public const string PorAsignar = "por_asignar";
        /// <summary>Una memoria USB de emergencia que se registro o se quito (db/28).</summary>
        public const string MemoriaEmergencia = "memoria_emergencia";

        /// <summary>Todos, en el orden del filtro Tipo del historial.</summary>
        public static readonly string[] Todos = { Franja, Clase, Cancelacion, Extra, Horario, PorAsignar, Bitacora, Laboratorio, FormaDeEntrar, Cuenta, MemoriaEmergencia };

        /// <summary>Los que avisan como alerta "Cambio de horario": el horario de las clases (tambien uno entero que se aplico).</summary>
        public static bool EsDeHorario(string tipo)
        {
            return tipo == Franja || tipo == Clase || tipo == Cancelacion || tipo == Extra || tipo == Horario;
        }

        /// <summary>Para el filtro Tipo del historial: "Franjas", "Cancelaciones"...</summary>
        public static string Plural(string tipo)
        {
            switch (tipo)
            {
                case Franja:
                    return "Franjas del horario";
                case Clase:
                    return "Clases";
                case Cancelacion:
                    return "Cancelaciones";
                case Extra:
                    return "Sesiones extra";
                case Horario:
                    return "Horarios aplicados";
                case PorAsignar:
                    return "Horas por asignar";
                case Bitacora:
                    return "Bitácoras (corregidas y fallas resueltas)";
                case Laboratorio:
                    return "Laboratorios";
                case FormaDeEntrar:
                    return "Formas de entrar";
                case Cuenta:
                    return "Cuentas (anonimizadas y del SIIA)";
                case MemoriaEmergencia:
                    return "Memorias de emergencia";
                default:
                    return tipo ?? "";
            }
        }
    }

    /// <summary>Que se hizo: historial_cambios.accion (db/21).</summary>
    internal static class AccionDeCambio
    {
        public const string Alta = "alta";
        public const string Cambio = "cambio";
        /// <summary>Se le puso fin en una fecha: lo que ya paso queda como fue.</summary>
        public const string Fin = "fin";
        public const string Quitar = "quitar";
        /// <summary>Una clase que ese dia se da solo en parte (una excepcion le quita lo que se cruza).</summary>
        public const string Recorte = "recorte";
        public const string Activar = "activar";
        public const string Desactivar = "desactivar";
        /// <summary>Una cuenta dada de baja pierde sus datos personales; sus bitacoras quedan (derechos ARCO).</summary>
        public const string Anonimizar = "anonimizar";
        /// <summary>Un horario entero queda aplicado: sus clases nuevas y el fin de las de antes (TipoDeCambio.Horario).</summary>
        public const string Aplicar = "aplicar";
        /// <summary>Se deshizo la aplicacion de un horario que todavia no empezaba.</summary>
        public const string Deshacer = "deshacer";
        /// <summary>Se dio de baja el horario que valia ese dia: nada corre hasta aplicar otro (TipoDeCambio.Horario).</summary>
        public const string DarDeBaja = "dar_de_baja";
        /// <summary>
        /// "No tomar en cuenta" (Faltas): las clases sin bitacoras de unas fechas
        /// se cancelan en lote y dejan de contar. Un solo renglon (TipoDeCambio.Cancelacion).
        /// </summary>
        public const string NoContar = "no_contar";
        /// <summary>"Volver a contar": se quitan las cancelaciones de un "No tomar en cuenta".</summary>
        public const string VolverAContar = "volver_a_contar";
        /// <summary>
        /// La administracion enlaza la cuenta de un docente con el numero de
        /// empleado con que entro al SIIA: su numero pasa a ser ese (TipoDeCambio.Cuenta).
        /// </summary>
        public const string Enlazar = "enlazar";
        /// <summary>Se deshace un enlace con el SIIA hecho por error: vuelve su numero de antes.</summary>
        public const string Desenlazar = "desenlazar";
        /// <summary>Una solicitud de enlace con el SIIA que no corresponde a nadie se descarta.</summary>
        public const string Descartar = "descartar";
        /// <summary>Horas por asignar (TipoDeCambio.PorAsignar) que bajan o se resuelven al ponerle horas a su clase.</summary>
        public const string Asignar = "asignar";
        /// <summary>Horas por asignar que ya no hacen falta ("Ya no hace falta").</summary>
        public const string NoHaceFalta = "no_hace_falta";
        /// <summary>Lo que se reporto en una sesion se da por resuelto (TipoDeCambio.Bitacora, db/26).</summary>
        public const string Resolver = "resolver";
        /// <summary>Se deshace "resuelta": vuelve a estar pendiente (TipoDeCambio.Bitacora, db/26).</summary>
        public const string Reabrir = "reabrir";
        /// <summary>Se le suspende el acceso a un estudiante por unos dias (TipoDeCambio.Cuenta, db/27).</summary>
        public const string Suspender = "suspender";
        /// <summary>Se le quita la suspension antes de tiempo (TipoDeCambio.Cuenta, db/27).</summary>
        public const string QuitarSuspension = "quitar_suspension";
    }

    /// <summary>Un cambio por anotar (HistorialCambios.Anotar), en la misma transaccion que el cambio.</summary>
    internal sealed class CambioPorAnotar
    {
        public string Tipo;
        public string Accion;
        /// <summary>La tabla y la fila que cambio.</summary>
        public string Tabla;
        public int? RegistroId;
        /// <summary>El laboratorio y el dia a los que se refiere, si se refiere a uno.</summary>
        public int? LaboratorioId;
        public DateTime? Fecha;
        /// <summary>Lo que paso, en una linea, como lo lee el administrador.</summary>
        public string Descripcion;
        /// <summary>Un dato por linea ("Dia: Martes"); null en un alta (Antes) o al quitar (Despues).</summary>
        public string Antes;
        public string Despues;
    }

    /// <summary>Un renglon del historial ya leido (VentanaHistorialCambios y las alertas).</summary>
    internal sealed class CambioRegistrado
    {
        public int Id;
        public DateTime Cuando;
        /// <summary>El numero con que entro quien lo hizo (matricula o numero de empleado).</summary>
        public string Usuario = "";
        /// <summary>Su nombre de hoy, o su numero si la cuenta ya no esta.</summary>
        public string Quien = "";
        public string Tipo = "";
        public string Accion = "";
        public string Tabla = "";
        public int? RegistroId;
        public int? LaboratorioId;
        /// <summary>El nombre de hoy del laboratorio, o "".</summary>
        public string Laboratorio = "";
        public DateTime? Fecha;
        public string Descripcion = "";
        public string Antes = "";
        public string Despues = "";

        public string Titulo
        {
            get { return HistorialCambios.Titulo(Tipo, Accion); }
        }
    }

    /// <summary>Lo que se leyo del historial en un periodo.</summary>
    internal sealed class LecturaHistorial
    {
        /// <summary>False sin db/21 (o si esta cuenta no puede leer la tabla): no hay historial.</summary>
        public bool Disponible;
        /// <summary>La hora del servidor al leer.</summary>
        public DateTime Ahora;
        public List<CambioRegistrado> Cambios = new List<CambioRegistrado>();
        /// <summary>Habia mas de HistorialCambios.MaximoPorLectura: se ven los mas recientes.</summary>
        public bool Recortada;
    }

    /// <summary>
    /// Quien cambio que y cuando (db/21_historial_cambios.sql): los cambios del
    /// horario (franjas, clases, cancelaciones y sesiones extra), las
    /// correcciones de bitacoras y los cambios de los laboratorios y de su forma
    /// de entrar. Cada metodo que cambia algo de eso lo anota con Anotar en SU
    /// transaccion: si el cambio queda, su renglon tambien, y al reves. Nunca se
    /// cambia ni se borra un renglon. Sin db/21 no anota nada y todo sigue como
    /// antes (Esquema.HistorialDeCambios). Lo leen las alertas ("Cambio de
    /// horario", AlertasDatos) y VentanaHistorialCambios, tambien el encargado
    /// de laboratorio (digbit_consulta solo la lee).
    /// </summary>
    internal static class HistorialCambios
    {
        /// <summary>Lo mas que se lee de una vez para la ventana del historial.</summary>
        public const int MaximoPorLectura = 3000;
        /// <summary>Lo mas que se lee de una vez para las alertas (son de unos dias).</summary>
        private const int MaximoParaAlertas = 500;
        private const int LargoDescripcion = 500;
        private const int LargoDatos = 4000;
        private const int LargoUsuario = 45;
        private const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

        /// <summary>True si la base tiene la tabla (y esta cuenta la ve): solo entonces vale la pena describir el cambio.</summary>
        public static bool Activo(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            return Esquema.HistorialDeCambios(conexion, transaccion);
        }

        /// <summary>
        /// Anota un cambio con la hora del servidor y quien tiene la sesion
        /// abierta (Datos_User), o quien, si se dice (el estudiante cuya cuenta se
        /// crea con el SIIA, antes de tener sesion). Va en la transaccion del
        /// cambio: si falla, falla el cambio. Sin db/21, nada.
        /// </summary>
        public static void Anotar(MySqlConnection conexion, MySqlTransaction transaccion, CambioPorAnotar cambio, string quien = null)
        {
            if (cambio == null || !Activo(conexion, transaccion))
            {
                return;
            }

            string usuario = Limpiar(quien ?? Datos_User.getUser(), LargoUsuario);
            object idUsuario = DBNull.Value;
            if (usuario != null)
            {
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT idusuarios FROM usuarios WHERE numero_identificador = @Usuario LIMIT 1", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Usuario", usuario);
                    object valor = comando.ExecuteScalar();
                    if (valor != null && valor != DBNull.Value)
                    {
                        idUsuario = Convert.ToInt32(valor, CultureInfo.InvariantCulture);
                    }
                }
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO historial_cambios (cuando, usuario_id, usuario, tipo, accion, tabla, registro_id, laboratorio_id, fecha, descripcion, antes, despues)
                VALUES (NOW(), @UsuarioId, @Usuario, @Tipo, @Accion, @Tabla, @Registro, @Lab, @Fecha, @Descripcion, @Antes, @Despues)", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@UsuarioId", idUsuario);
                comando.Parameters.AddWithValue("@Usuario", (object)usuario ?? DBNull.Value);
                comando.Parameters.AddWithValue("@Tipo", cambio.Tipo ?? "");
                comando.Parameters.AddWithValue("@Accion", cambio.Accion ?? "");
                comando.Parameters.AddWithValue("@Tabla", cambio.Tabla ?? "");
                comando.Parameters.AddWithValue("@Registro", cambio.RegistroId.HasValue ? (object)cambio.RegistroId.Value : DBNull.Value);
                comando.Parameters.AddWithValue("@Lab", cambio.LaboratorioId.HasValue && cambio.LaboratorioId.Value > 0 ? (object)cambio.LaboratorioId.Value : DBNull.Value);
                comando.Parameters.AddWithValue("@Fecha", cambio.Fecha.HasValue ? (object)cambio.Fecha.Value.Date : DBNull.Value);
                comando.Parameters.AddWithValue("@Descripcion", Limpiar(cambio.Descripcion, LargoDescripcion) ?? Titulo(cambio.Tipo, cambio.Accion));
                comando.Parameters.AddWithValue("@Antes", (object)Limpiar(cambio.Antes, LargoDatos) ?? DBNull.Value);
                comando.Parameters.AddWithValue("@Despues", (object)Limpiar(cambio.Despues, LargoDatos) ?? DBNull.Value);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Los cambios de desde a hasta (los dos dias enteros), del mas reciente al
        /// mas viejo, como mucho MaximoPorLectura. Sin db/21, Disponible = false.
        /// </summary>
        public static LecturaHistorial Leer(DateTime desde, DateTime hasta)
        {
            LecturaHistorial lectura = new LecturaHistorial();
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
                {
                    lectura.Ahora = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                if (!Activo(conexion, null))
                {
                    return lectura;
                }

                lectura.Disponible = true;
                lectura.Cambios = LeerEn(conexion, "hc.cuando >= @Desde AND hc.cuando < @Hasta", comando =>
                {
                    comando.Parameters.AddWithValue("@Desde", desde.Date);
                    comando.Parameters.AddWithValue("@Hasta", hasta.Date.AddDays(1));
                }, MaximoPorLectura + 1);
                if (lectura.Cambios.Count > MaximoPorLectura)
                {
                    lectura.Recortada = true;
                    lectura.Cambios.RemoveAt(lectura.Cambios.Count - 1);
                }
            }

            return lectura;
        }

        /// <summary>
        /// Para las alertas: los cambios del horario de las clases (franjas,
        /// clases, cancelaciones, sesiones extra y horarios enteros que se
        /// aplicaron o se deshicieron) desde ese dia, sin los que hizo
        /// sinLosDe (quien tiene la sesion abierta). Sin db/21, ninguno.
        /// </summary>
        public static List<CambioRegistrado> DeHorario(MySqlConnection conexion, DateTime desde, string sinLosDe)
        {
            if (!Activo(conexion, null))
            {
                return new List<CambioRegistrado>();
            }

            bool sinLosSuyos = !string.IsNullOrWhiteSpace(sinLosDe);
            return LeerEn(conexion,
                "hc.cuando >= @Desde AND hc.tipo IN ('" + TipoDeCambio.Franja + "', '" + TipoDeCambio.Clase + "', '"
                + TipoDeCambio.Cancelacion + "', '" + TipoDeCambio.Extra + "', '" + TipoDeCambio.Horario + "')"
                + (sinLosSuyos ? " AND (hc.usuario IS NULL OR hc.usuario <> @Yo)" : ""), comando =>
                {
                    comando.Parameters.AddWithValue("@Desde", desde.Date);
                    if (sinLosSuyos)
                    {
                        comando.Parameters.AddWithValue("@Yo", sinLosDe.Trim());
                    }
                }, MaximoParaAlertas);
        }

        private static List<CambioRegistrado> LeerEn(MySqlConnection conexion, string condicion, Action<MySqlCommand> parametros, int maximo)
        {
            List<CambioRegistrado> cambios = new List<CambioRegistrado>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT hc.idhistorial_cambios, DATE_FORMAT(hc.cuando, '%Y-%m-%d %H:%i:%s') AS cuando, hc.usuario,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS nombre,
                       hc.tipo, hc.accion, hc.tabla, hc.registro_id, hc.laboratorio_id, l.nombre_laboratorio,
                       DATE_FORMAT(hc.fecha, '%Y-%m-%d') AS fecha, hc.descripcion, hc.antes, hc.despues
                FROM historial_cambios hc
                LEFT JOIN usuarios u ON u.idusuarios = hc.usuario_id
                LEFT JOIN laboratorios l ON l.idlaboratorios = hc.laboratorio_id
                WHERE " + condicion + @"
                ORDER BY hc.cuando DESC, hc.idhistorial_cambios DESC
                LIMIT " + maximo.ToString(CultureInfo.InvariantCulture), conexion))
            {
                parametros(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime cuando;
                        if (!DateTime.TryParseExact(Texto(reader, "cuando"), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out cuando))
                        {
                            continue;
                        }

                        DateTime fecha;
                        string usuario = Texto(reader, "usuario");
                        string nombre = Texto(reader, "nombre").Trim();
                        cambios.Add(new CambioRegistrado
                        {
                            Id = Convert.ToInt32(reader["idhistorial_cambios"], CultureInfo.InvariantCulture),
                            Cuando = cuando,
                            Usuario = usuario,
                            Quien = nombre.Length > 0 ? nombre : usuario.Length > 0 ? usuario : "(sin sesión)",
                            Tipo = Texto(reader, "tipo"),
                            Accion = Texto(reader, "accion"),
                            Tabla = Texto(reader, "tabla"),
                            RegistroId = reader["registro_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["registro_id"], CultureInfo.InvariantCulture),
                            LaboratorioId = reader["laboratorio_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["laboratorio_id"], CultureInfo.InvariantCulture),
                            Laboratorio = Texto(reader, "nombre_laboratorio"),
                            Fecha = DateTime.TryParseExact(Texto(reader, "fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha)
                                ? fecha : (DateTime?)null,
                            Descripcion = Texto(reader, "descripcion"),
                            Antes = Texto(reader, "antes"),
                            Despues = Texto(reader, "despues")
                        });
                    }
                }
            }

            return cambios;
        }

        private static string Texto(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == null || valor == DBNull.Value ? "" : Convert.ToString(valor, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------------
        // Textos: lo mismo en las alertas, en la ventana y al anotar
        // ---------------------------------------------------------------------

        /// <summary>"Clase cancelada", "Franja nueva"...: el titulo de la alerta y la columna Que del historial.</summary>
        public static string Titulo(string tipo, string accion)
        {
            switch (tipo)
            {
                case TipoDeCambio.Franja:
                    return accion == AccionDeCambio.Alta ? "Franja nueva" : accion == AccionDeCambio.Cambio ? "Franja cambiada" : "Franja quitada";
                case TipoDeCambio.Clase:
                    switch (accion)
                    {
                        case AccionDeCambio.Alta:
                            return "Clase nueva";
                        case AccionDeCambio.Fin:
                            return "Clase terminada";
                        case AccionDeCambio.Activar:
                            return "Clase activada";
                        case AccionDeCambio.Desactivar:
                            return "Clase desactivada";
                        default:
                            return "Clase cambiada";
                    }

                case TipoDeCambio.Cancelacion:
                    switch (accion)
                    {
                        case AccionDeCambio.Quitar:
                            return "Cancelación quitada";
                        case AccionDeCambio.Recorte:
                            return "Clase recortada";
                        case AccionDeCambio.NoContar:
                            return "Faltas sin tomar en cuenta";
                        case AccionDeCambio.VolverAContar:
                            return "Faltas que vuelven a contar";
                        default:
                            return "Clase cancelada";
                    }
                case TipoDeCambio.Extra:
                    return accion == AccionDeCambio.Quitar ? "Sesión extra quitada" : accion == AccionDeCambio.Cambio ? "Sesión extra recortada" : "Sesión extra nueva";
                case TipoDeCambio.Horario:
                    return accion == AccionDeCambio.Deshacer ? "Aplicación deshecha"
                        : accion == AccionDeCambio.DarDeBaja ? "Horario dado de baja" : "Horario aplicado";
                case TipoDeCambio.PorAsignar:
                    return accion == AccionDeCambio.Asignar ? "Horas asignadas"
                        : accion == AccionDeCambio.NoHaceFalta ? "Horas que ya no hace falta asignar" : "Horas por asignar";
                case TipoDeCambio.Bitacora:
                    return accion == AccionDeCambio.Resolver ? "Falla resuelta"
                        : accion == AccionDeCambio.Reabrir ? "Falla sin resolver otra vez" : "Bitácora corregida";
                case TipoDeCambio.Laboratorio:
                    switch (accion)
                    {
                        case AccionDeCambio.Alta:
                            return "Laboratorio nuevo";
                        case AccionDeCambio.Quitar:
                            return "Laboratorio borrado";
                        case AccionDeCambio.Activar:
                            return "Laboratorio habilitado";
                        case AccionDeCambio.Desactivar:
                            return "Laboratorio deshabilitado";
                        default:
                            return "Laboratorio renombrado";
                    }

                case TipoDeCambio.FormaDeEntrar:
                    return "Forma de entrar cambiada";
                case TipoDeCambio.Cuenta:
                    return accion == AccionDeCambio.Enlazar ? "Docente enlazado con el SIIA"
                        : accion == AccionDeCambio.Desenlazar ? "Docente desenlazado del SIIA"
                        : accion == AccionDeCambio.Descartar ? "Solicitud del SIIA descartada"
                        : accion == AccionDeCambio.Suspender ? "Acceso suspendido"
                        : accion == AccionDeCambio.QuitarSuspension ? "Suspensión quitada"
                        : accion == AccionDeCambio.Alta ? "Cuenta creada con el SIIA"
                        : accion == AccionDeCambio.Cambio ? "Carrera tomada del SIIA" : "Cuenta anonimizada";
                case TipoDeCambio.MemoriaEmergencia:
                    return accion == AccionDeCambio.Quitar ? "Memoria de emergencia quitada" : "Memoria de emergencia registrada";
                default:
                    return ((tipo ?? "") + " " + (accion ?? "")).Trim();
            }
        }

        /// <summary>"13/10/2026", siempre con "/".</summary>
        public static string Fecha(DateTime fecha)
        {
            return fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>"martes 13/10/2026".</summary>
        public static string DiaYFecha(DateTime fecha)
        {
            return HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1].ToLowerInvariant() + " " + Fecha(fecha);
        }

        /// <summary>"07:00 a 09:00".</summary>
        public static string Horas(TimeSpan inicio, TimeSpan fin)
        {
            return HorariosDatos.Hora(inicio) + " a " + HorariosDatos.Hora(fin);
        }

        /// <summary>"del 01/09/2026 al 31/01/2027".</summary>
        public static string Vigencia(DateTime desde, DateTime hasta)
        {
            return "del " + Fecha(desde) + " al " + Fecha(hasta);
        }

        /// <summary>"si" o "no", con su acento.</summary>
        public static string SiNo(bool valor)
        {
            return valor ? "sí" : "no";
        }

        /// <summary>
        /// Un dato por linea ("Dia: Martes"), de pares campo y valor; los que no
        /// tienen valor (null) no salen. Null si no queda ninguno.
        /// </summary>
        public static string Datos(params string[] camposYValores)
        {
            List<string> lineas = new List<string>();
            for (int i = 0; i + 1 < camposYValores.Length; i += 2)
            {
                if (camposYValores[i + 1] != null)
                {
                    lineas.Add(camposYValores[i] + ": " + camposYValores[i + 1]);
                }
            }

            return lineas.Count == 0 ? null : string.Join("\n", lineas);
        }

        /// <summary>
        /// Los campos (de dos textos de Datos) cuyo valor cambio, en minusculas para
        /// una frase: "el dia y el horario". Vacio si no cambio ninguno.
        /// </summary>
        public static List<string> QueCambia(string antes, string despues)
        {
            Dictionary<string, string> previos = PorCampo(antes);
            Dictionary<string, string> nuevos = PorCampo(despues);
            return nuevos.Keys.Union(previos.Keys)
                .Where(campo =>
                {
                    string a, d;
                    previos.TryGetValue(campo, out a);
                    nuevos.TryGetValue(campo, out d);
                    return !string.Equals(a, d, StringComparison.Ordinal);
                })
                .ToList();
        }

        /// <summary>"el dia", "la vigencia": un campo de Datos con su articulo, para una frase ("cambia el dia y la vigencia").</summary>
        public static string ConArticulo(string campo)
        {
            if (campo == "Activa" || campo == "Activo")
            {
                return "el estado";
            }

            string minusculas = (campo ?? "").ToLowerInvariant();
            bool femenino = minusculas.StartsWith("falla", StringComparison.Ordinal) || minusculas.StartsWith("forma", StringComparison.Ordinal)
                || minusculas == "clase" || minusculas == "vigencia" || minusculas == "materia";
            return (femenino ? "la " : "el ") + minusculas;
        }

        /// <summary>"a", "a y b", "a, b y c".</summary>
        public static string Enumerar(IList<string> partes)
        {
            if (partes == null || partes.Count == 0)
            {
                return "";
            }

            return partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }

        private static Dictionary<string, string> PorCampo(string datos)
        {
            Dictionary<string, string> campos = new Dictionary<string, string>();
            foreach (string linea in (datos ?? "").Split('\n'))
            {
                int dos = linea.IndexOf(": ", StringComparison.Ordinal);
                if (dos > 0)
                {
                    campos[linea.Substring(0, dos)] = linea.Substring(dos + 2);
                }
            }

            return campos;
        }

        /// <summary>
        /// Sin caracteres de control (salvo los saltos de linea), sin nada fuera
        /// del BMP (emojis: la tabla es utf8 de 3 bytes; en MySQL estricto darian
        /// error y harian fallar el cambio, y en la escuela se cortarian) y con
        /// maximo caracteres como mucho. Vacio = null.
        /// </summary>
        internal static string Limpiar(string texto, int maximo)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return null;
            }

            StringBuilder limpio = new StringBuilder(texto.Length);
            foreach (char c in texto.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (c == '\t')
                {
                    limpio.Append(' ');
                }
                else if (c == '\n' || (!char.IsControl(c) && !char.IsSurrogate(c) && c != '\uFFFD'))
                {
                    limpio.Append(c);
                }
            }

            string resultado = limpio.ToString().Trim();
            if (resultado.Length > maximo)
            {
                resultado = resultado.Substring(0, maximo - 3).TrimEnd() + "...";
            }

            return resultado.Length == 0 ? null : resultado;
        }
    }
}
