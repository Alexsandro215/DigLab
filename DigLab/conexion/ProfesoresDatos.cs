using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Un profesor tal como lo ve la pantalla de Profesores del administrador.</summary>
    public class ProfesorInfo : UsuarioInfo
    {
        /// <summary>Clases activas y vigentes hoy o mas adelante.</summary>
        public int ClasesActivas;
        /// <summary>
        /// Laboratorios donde esas clases tienen franjas que aun se dan o se
        /// daran (sin las terminadas, db/20), separados por comas.
        /// </summary>
        public string Laboratorios;
        /// <summary>Sesiones (codigos_accesos) de todas sus clases, de siempre.</summary>
        public int Sesiones;
        public DateTime? UltimaSesion;
        /// <summary>El codigo que teclean sus alumnos, el mismo en todas sus clases (db/09).</summary>
        public string Codigo;
        /// <summary>El color elegido para sus clases, '#RRGGBB', o "" si es automatico (db/12).</summary>
        public string Color;
    }

    /// <summary>Lo que se exporta de un docente ademas de su perfil (ProfesoresDatos.DatosParaExportar).</summary>
    public class DatosDeDocente
    {
        /// <summary>idclases, materia, grupo, vigente_desde, vigente_hasta, activa (1/0).</summary>
        public DataTable Clases;
        /// <summary>clases_idclases, dia_semana, hora_inicio, hora_fin, laboratorio, vigente_desde y vigente_hasta (db/20; si no, NULL).</summary>
        public DataTable Franjas;
        /// <summary>
        /// idcodigos_accesos, fecha, hora_entrada, hora_salida, laboratorio, materia, grupo, extra (1/0), bitacoras
        /// y, con db/22, dadas (las asistencias que dio sin bitacora y que cuentan; sin la 22 no hay columna).
        /// </summary>
        public DataTable Sesiones;
        /// <summary>Las fallas que reporto (db/15), o null sin la tabla.</summary>
        public DataTable Reportes;
    }

    /// <summary>Lo que se guarda de un profesor: lo comun, su codigo y su color.</summary>
    public class DatosProfesor : DatosUsuario
    {
        public string Codigo;
        /// <summary>True si el dialogo ofrecio el color: solo entonces se toca la columna.</summary>
        public bool ConColor;
        /// <summary>'#RRGGBB', o null para el automatico.</summary>
        public string Color;
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

            // Llega a la vista ("No se pudo proponer un codigo: ..."): con acentos.
            throw new InvalidOperationException("Los que se probaron ya están en uso; escribe uno.");
        }

        /// <summary>
        /// Guarda el codigo del profesor y lo pone en todas sus clases. Con db/21
        /// anota en el historial, en la misma transaccion, "cambia el codigo" de
        /// cada clase suya vigente (activa y sin terminar): las otras ya no
        /// reciben alumnos y no tienen por que avisar a nadie. El codigo no se
        /// escribe (el encargado de laboratorio tambien lee el historial).
        /// </summary>
        public static void Asignar(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor, string codigo)
        {
            // Tambien cubre ObtenerOCrear, que solo escribe por aqui.
            Permisos.Exigir("Asignar el codigo de un profesor");
            List<ClaseInfo> antes = HistorialCambios.Activo(conexion, transaccion)
                ? ClasesVigentes(conexion, transaccion, idProfesor)
                : new List<ClaseInfo>();
            using (MySqlCommand comando = new MySqlCommand(@"
                UPDATE usuarios SET codigo_clase = @Codigo WHERE idusuarios = @Id;
                UPDATE clases SET codigo = @Codigo WHERE usuarios_idusuarios = @Id;", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                comando.Parameters.AddWithValue("@Id", idProfesor);
                comando.ExecuteNonQuery();
            }

            // Una clase que ya tenia ese codigo no cambia: AnotarClase no la anota.
            foreach (ClaseInfo clase in antes)
            {
                HorariosDatos.AnotarClase(conexion, transaccion, AccionDeCambio.Cambio, clase.Id, clase, null);
            }
        }

        /// <summary>Las clases vigentes del profesor como estan ahora (HorariosDatos.LeerClaseEn), para el historial.</summary>
        private static List<ClaseInfo> ClasesVigentes(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor)
        {
            List<int> ids = new List<int>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT c.idclases FROM clases c WHERE c.usuarios_idusuarios = @Id AND " + ProfesoresDatos.ClaseVigente + " ORDER BY c.idclases",
                conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idProfesor);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ids.Add(reader.GetInt32(0));
                    }
                }
            }

            // Ya cerrado el lector: la conexion no lee dos consultas a la vez.
            List<ClaseInfo> clases = new List<ClaseInfo>();
            foreach (int id in ids)
            {
                ClaseInfo clase = HorariosDatos.LeerClaseEn(conexion, transaccion, id);
                if (clase != null)
                {
                    clases.Add(clase);
                }
            }

            return clases;
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
        // Una clase cuenta como vigente si esta activa y no ha terminado su periodo
        // (tambien para el historial del cambio de codigo, CodigosDeProfesor).
        internal const string ClaseVigente = "c.activa = 1 AND c.vigente_hasta >= CURDATE()";

        // Una clase que sigue despues de hoy. Es la que impide anonimizar a un docente
        // (ya de baja): la que termina hoy, como las que termina la baja, ya no la usa
        // nadie, porque su cuenta no entra y los equipos solo aceptan el codigo de un
        // docente activo (u.activo = 1).
        private const string ClaseQueSigue = "c.activa = 1 AND c.vigente_hasta > CURDATE()";

        // El porque de los renglones del historial de la baja (QuitarClasesVigentes).
        private const string PorLaBaja = " al dar de baja a su docente";

        protected override int TipoUsuario
        {
            get { return 2; }
        }

        public override string NombreIdentificador
        {
            get { return "el número de empleado"; }
        }

        protected override string NombreDelTipo
        {
            get { return "docente"; }
        }

        /// <summary>
        /// Un docente con clases que siguen despues de hoy no se anonimiza: su
        /// nombre cambiaria en un horario que sigue en uso (el panel, Horarios, la
        /// pantalla de sus estudiantes). Antes se terminan (hoy como ultimo dia) o
        /// se desactivan en Horarios. Las que terminan hoy no cuentan (ClaseQueSigue):
        /// asi se anonimiza el mismo dia en que se le da de baja con "Terminar
        /// tambien sus clases", que las deja terminando hoy y ya no se pueden
        /// terminar antes (tienen sesiones) ni desactivar.
        /// </summary>
        protected override void ComprobarAntesDeAnonimizar(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM clases c WHERE c.usuarios_idusuarios = @Id AND " + ClaseQueSigue, conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                int siguen = Convert.ToInt32(comando.ExecuteScalar());
                if (siguen > 0)
                {
                    throw new ErrorDeDatos(siguen == 1
                        ? "Todavía tiene 1 clase que continúa después de hoy, así que su nombre sigue en el horario. "
                          + "En Horarios, pestaña Clases, termínala con hoy como último día (o desactívala, si nunca se dio) y vuelve a intentarlo."
                        : "Todavía tiene " + siguen + " clases que continúan después de hoy, así que su nombre sigue en el horario. "
                          + "En Horarios, pestaña Clases, termínalas con hoy como último día (o desactiva las que nunca se dieron) y vuelve a intentarlo.");
                }
            }
        }

        /// <summary>
        /// Lo que se entrega a un docente que pide sus datos (derechos ARCO): sus
        /// clases de siempre (con su horario y su vigencia), sus sesiones (cada vez
        /// que se dio una, con cuantas bitacoras se registraron y, con db/22, cuantas
        /// asistencias dio sin bitacora) y, con db/15, las fallas que reporto. Solo
        /// lee; su perfil ya esta en ProfesorInfo.
        /// </summary>
        public DatosDeDocente DatosParaExportar(int idProfesor)
        {
            DatosDeDocente datos = new DatosDeDocente();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                // Solo el numero, y solo las que cuentan (no las que anulo): el detalle de
                // cada una es del estudiante, no del docente.
                string dadas = Esquema.AsistenciaDada(conexion)
                    ? @",
                           (SELECT COUNT(*) FROM asistencias_dadas ad
                             WHERE ad.fk_codigo_accesos = ca.idcodigos_accesos AND ad.fk_profesor = @Id AND ad.activa = 1) AS dadas"
                    : "";
                datos.Clases = Tabla(conexion, @"
                    SELECT c.idclases, COALESCE(m.nombre_materia, '') AS materia, COALESCE(g.nombre_grupo, '') AS grupo,
                           c.vigente_desde, c.vigente_hasta, IF(c.activa = 1, 1, 0) AS activa
                    FROM clases c
                    LEFT JOIN materias m ON m.id_materia = c.materias_id_materia
                    LEFT JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    WHERE c.usuarios_idusuarios = @Id
                    ORDER BY c.vigente_desde DESC, m.nombre_materia, g.nombre_grupo", idProfesor);
                datos.Franjas = Tabla(conexion, @"
                    SELECT h.clases_idclases, h.dia_semana, h.hora_inicio, h.hora_fin, COALESCE(l.nombre_laboratorio, '') AS laboratorio,
                           " + (conVigencia ? "h.vigente_desde, h.vigente_hasta" : "NULL AS vigente_desde, NULL AS vigente_hasta") + @"
                    FROM horarios h
                    INNER JOIN clases c       ON c.idclases = h.clases_idclases
                    LEFT JOIN laboratorios l  ON l.idlaboratorios = h.laboratorios_idlaboratorios
                    WHERE c.usuarios_idusuarios = @Id
                    ORDER BY h.clases_idclases, h.dia_semana, h.hora_inicio", idProfesor);
                datos.Sesiones = Tabla(conexion, @"
                    SELECT ca.idcodigos_accesos, COALESCE(ca.fecha, DATE(ca.hora_registro)) AS fecha, ca.hora_entrada, ca.hora_salida,
                           COALESCE(l.nombre_laboratorio, '') AS laboratorio, COALESCE(m.nombre_materia, '') AS materia,
                           COALESCE(g.nombre_grupo, '') AS grupo, IF(ca.horario_excepciones_id IS NULL, 0, 1) AS extra,
                           (SELECT COUNT(*) FROM registros_bitacoras rb WHERE rb.fk_codigo_accesos = ca.idcodigos_accesos) AS bitacoras" + dadas + @"
                    FROM codigos_accesos ca
                    LEFT JOIN laboratorios l ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                    LEFT JOIN materias m     ON m.id_materia = ca.materias_id_materia
                    LEFT JOIN grupos g       ON g.idgrupos = ca.grupos_idgrupos
                    WHERE ca.usuarios_idusuarios = @Id
                    ORDER BY fecha DESC, ca.hora_entrada DESC, ca.idcodigos_accesos DESC", idProfesor);
                if (Esquema.ReportesDeProfesor(conexion))
                {
                    datos.Reportes = Tabla(conexion, @"
                        SELECT rp.registrado_en, COALESCE(ca.fecha, DATE(ca.hora_registro)) AS fecha,
                               COALESCE(l.nombre_laboratorio, '') AS laboratorio, COALESCE(m.nombre_materia, '') AS materia,
                               COALESCE(g.nombre_grupo, '') AS grupo, COALESCE(rp.numero_computadora, '') AS equipo,
                               rp.area, rp.falla, COALESCE(rp.comentarios, '') AS comentarios
                        FROM reportes_profesor rp
                        INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rp.fk_codigo_accesos
                        LEFT JOIN laboratorios l ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                        LEFT JOIN materias m     ON m.id_materia = ca.materias_id_materia
                        LEFT JOIN grupos g       ON g.idgrupos = ca.grupos_idgrupos
                        WHERE rp.fk_usuario = @Id
                        ORDER BY rp.registrado_en DESC, rp.idreportes_profesor DESC", idProfesor);
                }
            }

            return datos;
        }

        private static DataTable Tabla(MySqlConnection conexion, string consulta, int id)
        {
            DataTable tabla = new DataTable();
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                using (MySqlDataAdapter adaptador = new MySqlDataAdapter(comando))
                {
                    adaptador.Fill(tabla);
                }
            }

            return tabla;
        }

        /// <summary>False si la base aun no tiene la migracion 09: cada clase sigue con su propio codigo.</summary>
        public bool CodigoPorProfesor { get; private set; }

        /// <summary>False si la base aun no tiene la migracion 12: el color sale solo y no se puede elegir.</summary>
        public bool ColorElegible { get; private set; }

        /// <summary>La ultima lista leida (Listar): para decir que profesor usa ya un color.</summary>
        public List<ProfesorInfo> Ultimos { get; private set; } = new List<ProfesorInfo>();

        /// <summary>
        /// Reparte los colores de todos los profesores (ColoresProfesor) con los
        /// datos de la base, para la lista y el dialogo. Si falla, se quedan los de antes.
        /// </summary>
        public void RepartirColores()
        {
            try
            {
                RepartoDeColores reparto = new HorariosDatos().DatosDeColores();
                ColoresProfesor.Repartir(reparto.Orden, reparto.Elegidos);
            }
            catch (Exception ex)
            {
                Log.Error("Profesores: repartir los colores", ex);
            }
        }

        /// <summary>Un codigo libre para ese profesor (0 si aun no existe).</summary>
        public string ProponerCodigo(int idProfesor)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return CodigosDeProfesor.Proponer(conexion, null, idProfesor);
            }
        }

        /// <summary>
        /// El codigo se guarda con el profesor y se copia a todas sus clases; el
        /// color, si el dialogo lo ofrecio. En la misma transaccion que el resto.
        /// </summary>
        protected override void GuardarExtras(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosUsuario datos)
        {
            Permisos.Exigir("Guardar el codigo y el color de un profesor");
            DatosProfesor profesor = datos as DatosProfesor;
            if (profesor == null)
            {
                return;
            }

            if (Esquema.CodigoPorProfesor(conexion, transaccion))
            {
                GuardarCodigo(conexion, transaccion, id, profesor);
            }

            if (profesor.ConColor && Esquema.ColorDeProfesor(conexion, transaccion))
            {
                GuardarColor(conexion, transaccion, id, profesor);
            }
        }

        private void GuardarColor(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosProfesor profesor)
        {
            System.Drawing.Color leido;
            string color = string.IsNullOrWhiteSpace(profesor.Color) ? null : profesor.Color.Trim().ToUpperInvariant();
            if (color != null && !ColoresProfesor.DesdeTexto(color, out leido))
            {
                throw new ErrorDeDatos("El color " + color + " no es válido.");
            }

            string anterior;
            using (MySqlCommand comando = new MySqlCommand("SELECT color_horario FROM usuarios WHERE idusuarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                object valor = comando.ExecuteScalar();
                anterior = valor == null || valor == DBNull.Value ? null : valor.ToString();
            }

            if (string.Equals(anterior, color, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand("UPDATE usuarios SET color_horario = @Color WHERE idusuarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Color", (object)color ?? DBNull.Value);
                comando.Parameters.AddWithValue("@Id", id);
                comando.ExecuteNonQuery();
            }

            Log.Info("Color del profesor " + profesor.Matricula + ": " + (anterior ?? "automatico") + " -> " + (color ?? "automatico")
                + ", por " + Quien() + ".");
        }

        private void GuardarCodigo(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosProfesor profesor)
        {
            string codigo = (profesor.Codigo ?? "").Trim();
            if (codigo.Length == 0)
            {
                // Sin codigo no se toca nada: Asignar lo copiaria vacio a todas sus clases.
                return;
            }

            if (!CodigosDeProfesor.Libre(conexion, transaccion, codigo, id))
            {
                throw new ErrorDeDatos("El código " + codigo + " ya lo usa otra persona docente: escribe otro o pulsa «Proponer otro».", CampoRepetido.Codigo);
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
                ColorElegible = Esquema.ColorDeProfesor(conexion);
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT u.idusuarios, u.numero_identificador, u.nombre, u.apellido_paterno, u.apellido_materno, u.correo,
                           " + (CodigoPorProfesor ? "u.codigo_clase" : "NULL AS codigo_clase") + @",
                           " + (ColorElegible ? "u.color_horario" : "NULL AS color_horario") + @",
                           " + ColumnasEstado(conexion) + @",
                           (SELECT COUNT(*) FROM clases c WHERE c.usuarios_idusuarios = u.idusuarios AND " + ClaseVigente + @") AS clases_activas,
                           (SELECT GROUP_CONCAT(DISTINCT l.nombre_laboratorio ORDER BY l.nombre_laboratorio SEPARATOR ', ')
                              FROM clases c
                              INNER JOIN horarios h     ON h.clases_idclases = c.idclases
                              INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                             WHERE c.usuarios_idusuarios = u.idusuarios AND " + ClaseVigente + @"
                               AND " + VigenciaFranjas.Hasta(conVigencia) + @" >= CURDATE()) AS laboratorios,
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
                                Codigo = Texto(reader, "codigo_clase"),
                                Color = Texto(reader, "color_horario")
                            };
                            LeerComunes(reader, profesor);
                            lista.Add(profesor);
                        }
                    }
                }
            }

            Ultimos = lista;
            return lista;
        }

        /// <summary>Clases que la ultima DarDeBaja termino (tenian sesiones: se les puso fin).</summary>
        public int ClasesTerminadas { get; private set; }

        /// <summary>Clases que la ultima DarDeBaja desactivo (no tenian ninguna sesion).</summary>
        public int ClasesDesactivadas { get; private set; }

        /// <summary>
        /// Da de baja al profesor y, si se pide, quita sus clases vigentes para
        /// que su codigo deje de aceptarse en los laboratorios. Una clase con
        /// sesiones (VigenciaFranjas.Sesiones) no se desactiva, porque eso
        /// esconderia su pasado en Faltas y en la asistencia: se termina, con fin
        /// hoy o el dia de su ultima sesion si es despues (solo si terminaba mas
        /// tarde). Una sin sesiones se desactiva como antes. Una clase con
        /// sesiones no puede cambiar de profesor (HorariosDatos.ActualizarClase):
        /// si un suplente sigue con el grupo, se le crea una clase nueva en
        /// Horarios. Devuelve cuantas se terminaron o desactivaron (el detalle,
        /// en ClasesTerminadas y ClasesDesactivadas). Con db/21 cada una queda en
        /// el historial de cambios, en la misma transaccion que la baja.
        /// </summary>
        public int DarDeBaja(int id, bool desactivarClases)
        {
            Permisos.Exigir("Dar de baja a un profesor");
            int terminadas = 0;
            int desactivadas = 0;
            List<string> fines = new List<string>();
            ClasesTerminadas = 0;
            ClasesDesactivadas = 0;
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    CambiarEstado(conexion, transaccion, id, false);
                    if (desactivarClases)
                    {
                        QuitarClasesVigentes(conexion, transaccion, id, fines, out terminadas, out desactivadas);
                    }

                    transaccion.Commit();
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }

            ClasesTerminadas = terminadas;
            ClasesDesactivadas = desactivadas;
            if (terminadas + desactivadas > 0)
            {
                Log.Info("Baja del profesor id " + id + ": " + terminadas + " clase(s) con sesiones terminadas"
                    + (fines.Count > 0 ? " (" + string.Join("; ", fines) + ")" : "") + " y " + desactivadas + " sin sesiones desactivadas.");
            }

            return terminadas + desactivadas;
        }

        /// <summary>
        /// Las clases vigentes del profesor: con sesiones, fin = el mas tardio de
        /// hoy, su ultima sesion y su inicio (nunca antes); sin sesiones, activa = 0.
        /// La vigencia de la clase la entienden todas las versiones de DigLab, asi
        /// que no depende de db/20. En fines deja "clase N hasta el ..." para el log.
        /// Con db/21, cada clase que cambia deja su renglon en el historial en esta
        /// transaccion ("Clase terminada" o "Clase desactivada", con el porque),
        /// como si se terminara o desactivara en Horarios.
        /// </summary>
        private static void QuitarClasesVigentes(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor,
            List<string> fines, out int terminadas, out int desactivadas)
        {
            terminadas = 0;
            desactivadas = 0;
            DateTime hoy = DateTime.MinValue;
            List<int> ids = new List<int>();
            Dictionary<int, DateTime> desde = new Dictionary<int, DateTime>();
            Dictionary<int, DateTime> hasta = new Dictionary<int, DateTime>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT c.idclases, c.vigente_desde, c.vigente_hasta, CURDATE() FROM clases c WHERE c.usuarios_idusuarios = @Id AND "
                + ClaseVigente + " ORDER BY c.idclases FOR UPDATE", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", idProfesor);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int clase = reader.GetInt32(0);
                        ids.Add(clase);
                        desde[clase] = reader.GetDateTime(1).Date;
                        hasta[clase] = reader.GetDateTime(2).Date;
                        hoy = reader.GetDateTime(3).Date;
                    }
                }
            }

            // Cada clase como era antes de tocarla (solo con db/21): AnotarClase dice que cambio.
            bool conHistorial = HistorialCambios.Activo(conexion, transaccion);
            foreach (int clase in ids)
            {
                SesionesDeClase sesiones = VigenciaFranjas.Sesiones(conexion, clase, transaccion);
                if (sesiones.Cuantas == 0)
                {
                    ClaseInfo previa = conHistorial ? HorariosDatos.LeerClaseEn(conexion, transaccion, clase) : null;
                    using (MySqlCommand comando = new MySqlCommand("UPDATE clases SET activa = 0 WHERE idclases = @Clase", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Clase", clase);
                        desactivadas += comando.ExecuteNonQuery();
                    }

                    if (previa != null)
                    {
                        HorariosDatos.AnotarClase(conexion, transaccion, AccionDeCambio.Desactivar, clase, previa, ": se desactiva" + PorLaBaja);
                    }

                    continue;
                }

                DateTime fin = hoy;
                if (sesiones.Ultima.HasValue && sesiones.Ultima.Value > fin)
                {
                    fin = sesiones.Ultima.Value;
                }

                if (desde[clase] > fin)
                {
                    fin = desde[clase];
                }

                if (hasta[clase] <= fin)
                {
                    // Ya terminaba ese dia o antes: nada que recortar.
                    continue;
                }

                ClaseInfo antes = conHistorial ? HorariosDatos.LeerClaseEn(conexion, transaccion, clase) : null;
                using (MySqlCommand comando = new MySqlCommand("UPDATE clases SET vigente_hasta = @Fin WHERE idclases = @Clase", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Fin", fin);
                    comando.Parameters.AddWithValue("@Clase", clase);
                    terminadas += comando.ExecuteNonQuery();
                }

                // Como TerminarClase en Horarios: "termina el 15/10/2026 (antes, el 31/01/2027)".
                if (antes != null)
                {
                    HorariosDatos.AnotarClase(conexion, transaccion, AccionDeCambio.Fin, clase, antes,
                        ": termina el " + HistorialCambios.Fecha(fin) + " (antes, el " + HistorialCambios.Fecha(hasta[clase]) + ")" + PorLaBaja);
                }

                fines.Add("clase " + clase + " hasta el " + fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + ", antes el " + hasta[clase].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }
    }
}
