using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Un laboratorio tal como lo ve la pantalla de Laboratorios del administrador.</summary>
    public class LaboratorioInfo
    {
        public int Id;
        public string Nombre;
        public bool Activo;
        /// <summary>
        /// Franjas que aun se dan o se daran (clase activa y la vigencia de la
        /// franja, db/20, sin terminar): mientras haya, no se puede quitar.
        /// </summary>
        public int FranjasVigentes;
        /// <summary>Todas sus franjas, de clases vigentes o no.</summary>
        public int Franjas;
        public int Sesiones;
        public DateTime? UltimaSesion;
        /// <summary>Equipos distintos que registraron bitacoras en los ultimos 30 dias.</summary>
        public int EquiposRecientes;
        /// <summary>Como entran los alumnos a sus clases aqui (db/11; sin ella, Fijo).</summary>
        public ModoCodigo Modo;

        /// <summary>Sin franjas ni sesiones: se creo por error y se puede borrar sin perder nada.</summary>
        public bool SinUso
        {
            get { return Franjas == 0 && Sesiones == 0; }
        }
    }

    /// <summary>
    /// Datos de la pantalla de Laboratorios. Un laboratorio con historial no se
    /// borra: se desactiva (db/08_migracion_bajas.sql), porque las claves foraneas
    /// borrarian en cascada su horario y dejarian sus sesiones sin laboratorio.
    /// </summary>
    internal class LaboratoriosDatos
    {
        private const int DiasEquiposRecientes = 30;

        private readonly Conexion mconexion = new Conexion();

        /// <summary>False si la base aun no tiene laboratorios.activo: solo se pueden borrar los que no se usaron.</summary>
        public bool DesactivacionDisponible { get; private set; }

        /// <summary>False si la base aun no tiene laboratorios.modo_codigo (db/11): todos con el codigo fijo.</summary>
        public bool ModosDisponibles { get; private set; }

        /// <summary>False si a codigos_accesos le falta un UNIQUE de sesion: entonces no se ofrece la generacion de codigos.</summary>
        public bool SesionesUnicas { get; private set; }

        /// <summary>True con db/14: sin codigos y sin clase, el alumno registra uso libre.</summary>
        public bool UsoLibre { get; private set; }

        /// <summary>La forma general guardada en configuracion (ModosDeAcceso.LeerGeneral), o null.</summary>
        public ModoCodigo? GeneralGuardada { get; private set; }

        /// <summary>"Los docentes no ven los codigos" (CodigosSoloResponsables); null sin db/13: entonces no se ofrece.</summary>
        public bool? DocentesSinCodigos { get; private set; }

        public List<LaboratorioInfo> Listar()
        {
            List<LaboratorioInfo> lista = new List<LaboratorioInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                DesactivacionDisponible = Esquema.LaboratoriosDesactivables(conexion);
                ModosDisponibles = Esquema.ModoDeAcceso(conexion);
                SesionesUnicas = Esquema.SesionesUnicas(conexion);
                UsoLibre = Esquema.UsoLibre(conexion);
                AjustesDeCodigos ajustes = ModosDeAcceso.LeerAjustes(conexion);
                GeneralGuardada = ModosDisponibles ? ajustes.General : null;
                DocentesSinCodigos = ajustes.SoloResponsables;
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT l.idlaboratorios, l.nombre_laboratorio, " + (DesactivacionDisponible ? "l.activo" : "1 AS activo") + @",
                           " + (ModosDisponibles ? "l.modo_codigo" : "'fijo' AS modo_codigo") + @",
                           (SELECT COUNT(*) FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases
                             WHERE h.laboratorios_idlaboratorios = l.idlaboratorios
                               AND " + FranjaSinTerminar(conVigencia) + @") AS franjas_vigentes,
                           (SELECT COUNT(*) FROM horarios h WHERE h.laboratorios_idlaboratorios = l.idlaboratorios) AS franjas,
                           (SELECT COUNT(*) FROM codigos_accesos ca WHERE ca.laboratorios_idlaboratorios = l.idlaboratorios) AS sesiones,
                           (SELECT MAX(COALESCE(ca.fecha, DATE(ca.hora_registro))) FROM codigos_accesos ca
                             WHERE ca.laboratorios_idlaboratorios = l.idlaboratorios) AS ultima,
                           (SELECT COUNT(DISTINCT rb.numero_computadora) FROM registros_bitacoras rb
                             INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
                             WHERE ca.laboratorios_idlaboratorios = l.idlaboratorios
                               AND COALESCE(ca.fecha, DATE(ca.hora_registro)) >= CURDATE() - INTERVAL @Dias DAY) AS equipos
                    FROM laboratorios l
                    ORDER BY l.nombre_laboratorio", conexion))
                {
                    comando.Parameters.AddWithValue("@Dias", DiasEquiposRecientes);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new LaboratorioInfo
                            {
                                Id = Convert.ToInt32(reader["idlaboratorios"]),
                                Nombre = reader["nombre_laboratorio"].ToString(),
                                Activo = Convert.ToInt32(reader["activo"]) == 1,
                                FranjasVigentes = Convert.ToInt32(reader["franjas_vigentes"]),
                                Franjas = Convert.ToInt32(reader["franjas"]),
                                Sesiones = Convert.ToInt32(reader["sesiones"]),
                                UltimaSesion = reader["ultima"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["ultima"]),
                                EquiposRecientes = Convert.ToInt32(reader["equipos"]),
                                Modo = ModosDeAcceso.Desde(Convert.ToString(reader["modo_codigo"]))
                            });
                        }
                    }
                }
            }

            return lista;
        }

        public void Crear(string nombre)
        {
            Permisos.Exigir("Dar de alta un laboratorio");
            ModoCodigo modo = ModoCodigo.Fijo;
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ComprobarNombreLibre(conexion, transaccion, nombre, null);
                // Nace con la forma general de "Codigos de las clases" (sin ella, la
                // de la mayoria): si toda la escuela va sin codigo, el nuevo tambien.
                bool conModo = Esquema.ModoDeAcceso(conexion, transaccion);
                if (conModo)
                {
                    modo = ModosDeAcceso.General(ModosDeAcceso.LeerGeneral(conexion, transaccion), ModosDeLosActivos(conexion, transaccion));
                }

                int id;
                using (MySqlCommand comando = new MySqlCommand((conModo
                    ? "INSERT INTO laboratorios (nombre_laboratorio, modo_codigo) VALUES (@Nombre, @Modo)"
                    : "INSERT INTO laboratorios (nombre_laboratorio) VALUES (@Nombre)") + "; SELECT LAST_INSERT_ID();", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Nombre", nombre);
                    comando.Parameters.AddWithValue("@Modo", ModosDeAcceso.Valor(modo));
                    id = Convert.ToInt32(comando.ExecuteScalar());
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Laboratorio,
                    Accion = AccionDeCambio.Alta,
                    Tabla = "laboratorios",
                    RegistroId = id,
                    LaboratorioId = id,
                    Descripcion = nombre + (conModo ? " (forma de entrar: " + ModosDeAcceso.Nombre(modo) + ")." : "."),
                    Despues = HistorialCambios.Datos("Nombre", nombre, "Forma de entrar", conModo ? ModosDeAcceso.Nombre(modo) : null)
                });
                transaccion.Commit();
            }

            Log.Info("Laboratorio nuevo '" + nombre + "' (" + ModosDeAcceso.Valor(modo) + ") por " + Quien() + ".");
        }

        /// <summary>
        /// El mismo modo en esos laboratorios o, si no se dan, en todos (tambien
        /// los desactivados), y entonces tambien como forma general. Ver CambiarModos.
        /// </summary>
        public void CambiarModo(IList<int> laboratorios, ModoCodigo modo)
        {
            Permisos.Exigir("Cambiar los codigos de las clases");
            Dictionary<int, ModoCodigo> nuevos = new Dictionary<int, ModoCodigo>();
            if (laboratorios == null)
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    foreach (int id in ModosDeAcceso.LeerTodos(conexion).Keys)
                    {
                        nuevos[id] = modo;
                    }
                }
            }
            else
            {
                foreach (int id in laboratorios)
                {
                    nuevos[id] = modo;
                }
            }

            CambiarModos(laboratorios == null ? modo : (ModoCodigo?)null, nuevos);
        }

        /// <summary>
        /// Cambia como entran los alumnos a sus clases (db/11), todo o nada en una
        /// transaccion: el modo de esos laboratorios y, si se da, la forma general
        /// de la escuela (configuracion, con db/13: la de los laboratorios que no
        /// estan en "con otra forma" y la de los nuevos). Solo con el usuario del
        /// administrador: el de los equipos no puede cambiar laboratorios (db/07).
        /// Queda en el log quien lo cambio y de que a que, y avisa a las ventanas
        /// abiertas (ModosDeAcceso.Cambio).
        ///
        /// Las clases que ya empezaron conservan su codigo hasta que terminan: la
        /// que ya tiene sesion, porque el codigo de la sesion sigue entrando; y al
        /// pasar del codigo fijo a la generacion de codigos, la que aun no la
        /// tiene (nadie ha registrado) la recibe aqui con el codigo fijo, como si
        /// hubiera entrado el primer alumno: sus alumnos ya lo tienen.
        /// </summary>
        public void CambiarModos(ModoCodigo? general, IDictionary<int, ModoCodigo> nuevos)
        {
            CambiarModos(general, nuevos, null);
        }

        /// <summary>
        /// Lo mismo y, si se da docentesSinCodigos (con db/13), tambien "Los docentes
        /// no ven los codigos" (CodigosSoloResponsables) en la misma transaccion.
        /// </summary>
        public void CambiarModos(ModoCodigo? general, IDictionary<int, ModoCodigo> nuevos, bool? docentesSinCodigos)
        {
            Permisos.Exigir("Cambiar los codigos de las clases");
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.ModoDeAcceso(conexion))
                {
                    throw new ErrorDeDatos("La base de datos todavía no guarda el modo de acceso de los laboratorios: falta aplicar db/11_modo_de_acceso.sql. "
                        + "Pide a quien administra el servidor que la aplique.");
                }

                Dictionary<int, ModoCodigo> antes = ModosDeAcceso.LeerTodos(conexion);
                List<KeyValuePair<int, ModoCodigo>> cambian = (nuevos ?? new Dictionary<int, ModoCodigo>())
                    .Where(p => ModosDeAcceso.De(antes, p.Key) != p.Value).ToList();
                bool conAjustes = (general.HasValue || docentesSinCodigos.HasValue) && Esquema.Ajustes(conexion);
                AjustesDeCodigos ajustes = conAjustes ? ModosDeAcceso.LeerAjustes(conexion) : new AjustesDeCodigos();
                ModoCodigo? generalGuardada = ajustes.General;
                bool guardarGeneral = conAjustes && general.HasValue && generalGuardada != general;
                bool guardarDocentes = conAjustes && docentesSinCodigos.HasValue && (ajustes.SoloResponsables ?? false) != docentesSinCodigos.Value;
                if (cambian.Count == 0 && !guardarGeneral && !guardarDocentes)
                {
                    return;
                }

                // Para el historial (db/21): la forma general que valia hasta ahora
                // (sin guardar, la de la mayoria de los activos).
                ModoCodigo generalAntes = guardarGeneral ? ModosDeAcceso.General(generalGuardada, ModosDeLosActivos(conexion, null)) : ModoCodigo.Fijo;
                int conservadas = 0;
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    foreach (IGrouping<ModoCodigo, KeyValuePair<int, ModoCodigo>> grupo in cambian.GroupBy(p => p.Value))
                    {
                        List<int> ids = grupo.Select(p => p.Key).ToList();
                        using (MySqlCommand comando = new MySqlCommand("UPDATE laboratorios SET modo_codigo = @Modo WHERE idlaboratorios IN ("
                            + string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))) + ")", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Modo", ModosDeAcceso.Valor(grupo.Key));
                            comando.ExecuteNonQuery();
                        }

                        List<int> dejanElFijo = grupo.Key == ModoCodigo.Renovable
                            ? ids.Where(id => ModosDeAcceso.De(antes, id) == ModoCodigo.Fijo).ToList()
                            : new List<int>();
                        if (dejanElFijo.Count > 0)
                        {
                            conservadas += ConservarElCodigoFijo(conexion, transaccion, dejanElFijo);
                        }
                    }

                    if (guardarGeneral)
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "INSERT INTO configuracion (clave, valor) VALUES (@Clave, @Valor) ON DUPLICATE KEY UPDATE valor = VALUES(valor)",
                            conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Clave", ModosDeAcceso.ClaveGeneral);
                            comando.Parameters.AddWithValue("@Valor", ModosDeAcceso.Valor(general.Value));
                            comando.ExecuteNonQuery();
                        }
                    }

                    if (guardarDocentes)
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "INSERT INTO configuracion (clave, valor) VALUES (@Clave, @Valor) ON DUPLICATE KEY UPDATE valor = VALUES(valor)",
                            conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Clave", CodigosSoloResponsables.Clave);
                            comando.Parameters.AddWithValue("@Valor", CodigosSoloResponsables.Valor(docentesSinCodigos.Value));
                            comando.ExecuteNonQuery();
                        }
                    }

                    AnotarFormasDeEntrar(conexion, transaccion, antes, cambian, guardarGeneral ? generalAntes : (ModoCodigo?)null, general);
                    if (guardarDocentes)
                    {
                        AnotarQuienVeLosCodigos(conexion, transaccion, docentesSinCodigos.Value);
                    }

                    transaccion.Commit();
                }

                if (guardarDocentes)
                {
                    CodigosSoloResponsables.Recordar(docentesSinCodigos.Value);
                    Log.Info("Codigos de las clases: los docentes " + (docentesSinCodigos.Value ? "ya no ven" : "vuelven a ver")
                        + " los codigos, por " + Quien() + ".");
                }

                if (conservadas > 0)
                {
                    Log.Info(conservadas + " clase(s) en curso sin sesion la recibieron con su codigo fijo al pasar a la generacion de codigos.");
                }

                if (guardarGeneral)
                {
                    Log.Info("Forma general de los codigos de las clases: " + ModosDeAcceso.Valor(general.Value) + ", por " + Quien() + ".");
                }

                foreach (KeyValuePair<int, ModoCodigo> cambio in cambian)
                {
                    Log.Info("Modo de acceso del laboratorio id " + cambio.Key + ": " + ModosDeAcceso.Valor(ModosDeAcceso.De(antes, cambio.Key))
                        + " -> " + ModosDeAcceso.Valor(cambio.Value) + ", por " + Quien() + ".");
                }

                // Generacion de codigos: el secreto, si aun no hay, lo crea ahora el
                // administrador (los equipos no pueden). Fuera de la transaccion: si
                // no se pudiera, el cambio de modo ya quedo y las clases se abren con
                // un codigo al azar (sale en el log).
                if (cambian.Any(p => p.Value == ModoCodigo.Renovable))
                {
                    try
                    {
                        CodigosGenerados.Secreto(conexion);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Crear el secreto de los codigos generados cada semana", ex);
                    }
                }
            }

            ModosDeAcceso.AvisarCambio();
        }

        /// <summary>
        /// Historial (db/21): un renglon por laboratorio que cambia de forma de
        /// entrar y otro si cambia la de toda la escuela (generalAntes: la que
        /// valia; null si no se guarda). Sin db/21, nada.
        /// </summary>
        private static void AnotarFormasDeEntrar(MySqlConnection conexion, MySqlTransaction transaccion, Dictionary<int, ModoCodigo> antes,
            List<KeyValuePair<int, ModoCodigo>> cambian, ModoCodigo? generalAntes, ModoCodigo? general)
        {
            if (!HistorialCambios.Activo(conexion, transaccion))
            {
                return;
            }

            foreach (KeyValuePair<int, ModoCodigo> cambio in cambian)
            {
                string de = ModosDeAcceso.Nombre(ModosDeAcceso.De(antes, cambio.Key));
                string a = ModosDeAcceso.Nombre(cambio.Value);
                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.FormaDeEntrar,
                    Accion = AccionDeCambio.Cambio,
                    Tabla = "laboratorios",
                    RegistroId = cambio.Key,
                    LaboratorioId = cambio.Key,
                    Descripcion = (NombreDe(conexion, transaccion, cambio.Key) ?? "Laboratorio " + cambio.Key) + ": de " + de + " a " + a + ".",
                    Antes = HistorialCambios.Datos("Forma de entrar", de),
                    Despues = HistorialCambios.Datos("Forma de entrar", a)
                });
            }

            // Sin una guardada valia la de la mayoria: si ya era esa, guardarla no cambia nada para nadie.
            if (generalAntes.HasValue && general.HasValue && generalAntes.Value != general.Value)
            {
                string de = ModosDeAcceso.Nombre(generalAntes.Value);
                string a = ModosDeAcceso.Nombre(general.Value);
                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.FormaDeEntrar,
                    Accion = AccionDeCambio.Cambio,
                    Tabla = "configuracion",
                    Descripcion = "Toda la escuela (la forma general y la de los laboratorios nuevos): de " + de + " a " + a + ".",
                    Antes = HistorialCambios.Datos("Forma general", de),
                    Despues = HistorialCambios.Datos("Forma general", a)
                });
            }
        }

        /// <summary>Historial (db/21): "Los docentes no ven los codigos" se encendio o se apago. Sin db/21, nada.</summary>
        private static void AnotarQuienVeLosCodigos(MySqlConnection conexion, MySqlTransaction transaccion, bool docentesSinCodigos)
        {
            if (!HistorialCambios.Activo(conexion, transaccion))
            {
                return;
            }

            string antes = docentesSinCodigos ? "Sí" : "No";
            string despues = docentesSinCodigos ? "No" : "Sí";
            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = TipoDeCambio.FormaDeEntrar,
                Accion = AccionDeCambio.Cambio,
                Tabla = "configuracion",
                Descripcion = docentesSinCodigos
                    ? "Toda la escuela: los docentes ya no ven los códigos; solo la administración y los responsables de laboratorio."
                    : "Toda la escuela: los docentes vuelven a ver los códigos de sus clases.",
                Antes = HistorialCambios.Datos("Los docentes ven los códigos", antes),
                Despues = HistorialCambios.Datos("Los docentes ven los códigos", despues)
            });
        }

        /// <summary>El nombre de un laboratorio, o null si ya no esta.</summary>
        private static string NombreDe(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT nombre_laboratorio FROM laboratorios WHERE idlaboratorios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                object nombre = comando.ExecuteScalar();
                return nombre == null || nombre == DBNull.Value ? null : Convert.ToString(nombre, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// La sesion de hoy, con el codigo fijo de su clase, de cada franja o
        /// sesion extra de esos laboratorios que ya empezo, aun no termina y no
        /// tiene sesion (la misma fila que crearia el primer alumno con ese
        /// codigo). Solo clases activas y vigentes (y franjas que hoy se dan, con
        /// su propia vigencia, db/20), sin cancelar. IGNORE por si un alumno la
        /// crea en ese mismo instante (los UNIQUE de la sesion).
        /// </summary>
        private static int ConservarElCodigoFijo(MySqlConnection conexion, MySqlTransaction transaccion, IList<int> laboratorios)
        {
            string labs = string.Join(",", laboratorios.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            string profesorActivo = Esquema.TieneBajas(conexion, transaccion) ? " AND u.activo = 1" : "";
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            string columnas = @"INSERT IGNORE INTO codigos_accesos
                    (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                     laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)";
            string franjas = columnas + @"
                SELECT c.codigo, NOW(), CURDATE(), h.hora_inicio, h.hora_fin, c.materias_id_materia, c.grupos_idgrupos,
                       h.laboratorios_idlaboratorios, c.usuarios_idusuarios, h.idhorarios, NULL
                FROM horarios h
                INNER JOIN clases c   ON c.idclases = h.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                WHERE h.laboratorios_idlaboratorios IN (" + labs + @") AND h.dia_semana = WEEKDAY(CURDATE()) + 1
                  AND " + VigenciaFranjas.SeDa(conVigencia, "CURDATE()") + profesorActivo + @"
                  AND CURTIME() >= h.hora_inicio AND CURTIME() <= h.hora_fin
                  AND NOT EXISTS (SELECT 1 FROM horario_excepciones xc
                                  WHERE xc.tipo = 'cancelada' AND xc.horarios_idhorarios = h.idhorarios AND xc.fecha = CURDATE())
                  AND NOT EXISTS (SELECT 1 FROM codigos_accesos ca WHERE ca.horarios_idhorarios = h.idhorarios AND ca.fecha = CURDATE())";
            string extras = columnas + @"
                SELECT c.codigo, NOW(), CURDATE(), x.hora_inicio, x.hora_fin, c.materias_id_materia, c.grupos_idgrupos,
                       x.laboratorios_idlaboratorios, c.usuarios_idusuarios, NULL, x.idhorario_excepciones
                FROM horario_excepciones x
                INNER JOIN clases c   ON c.idclases = x.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                WHERE x.tipo = 'extra' AND x.fecha = CURDATE() AND x.laboratorios_idlaboratorios IN (" + labs + @")
                  AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta" + profesorActivo + @"
                  AND CURTIME() >= x.hora_inicio AND CURTIME() <= x.hora_fin
                  AND NOT EXISTS (SELECT 1 FROM codigos_accesos ca WHERE ca.horario_excepciones_id = x.idhorario_excepciones)";

            int creadas = 0;
            foreach (string sql in new[] { franjas, extras })
            {
                using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
                {
                    creadas += comando.ExecuteNonQuery();
                }
            }

            return creadas;
        }

        public void Renombrar(int id, string anterior, string nuevo)
        {
            Permisos.Exigir("Cambiar el nombre de un laboratorio");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ComprobarNombreLibre(conexion, transaccion, nuevo, id);
                // El de la base, por si la pantalla se quedo con uno viejo.
                string antes = NombreDe(conexion, transaccion, id) ?? anterior;
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE laboratorios SET nombre_laboratorio = @Nombre WHERE idlaboratorios = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Nombre", nuevo);
                    comando.Parameters.AddWithValue("@Id", id);
                    comando.ExecuteNonQuery();
                }

                if (!string.Equals(antes, nuevo, StringComparison.Ordinal))
                {
                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.Laboratorio,
                        Accion = AccionDeCambio.Cambio,
                        Tabla = "laboratorios",
                        RegistroId = id,
                        LaboratorioId = id,
                        Descripcion = "«" + antes + "» pasa a llamarse «" + nuevo + "».",
                        Antes = HistorialCambios.Datos("Nombre", antes),
                        Despues = HistorialCambios.Datos("Nombre", nuevo)
                    });
                }

                transaccion.Commit();
            }

            // Queda en el log porque los equipos de ese laboratorio se configuran con
            // el nombre: hasta reconfigurarlos no lo reconocen.
            Log.Info("Laboratorio '" + anterior + "' renombrado a '" + nuevo + "' por " + Quien() + ".");
        }

        /// <summary>Borra un laboratorio que nunca se uso. Lo vuelve a comprobar en la base antes de borrar.</summary>
        public void Borrar(int id)
        {
            Permisos.Exigir("Borrar un laboratorio");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                string nombre = NombreDe(conexion, transaccion, id);
                using (MySqlCommand comando = new MySqlCommand(@"
                    DELETE FROM laboratorios
                     WHERE idlaboratorios = @Id
                       AND NOT EXISTS (SELECT 1 FROM horarios h WHERE h.laboratorios_idlaboratorios = @Id)
                       AND NOT EXISTS (SELECT 1 FROM codigos_accesos ca WHERE ca.laboratorios_idlaboratorios = @Id)
                       AND NOT EXISTS (SELECT 1 FROM horario_excepciones x WHERE x.laboratorios_idlaboratorios = @Id)", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", id);
                    if (comando.ExecuteNonQuery() == 0)
                    {
                        throw new ErrorDeDatos("Ese laboratorio ya tiene horario o sesiones, así que no se puede borrar; deshabilítalo.");
                    }
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Laboratorio,
                    Accion = AccionDeCambio.Quitar,
                    Tabla = "laboratorios",
                    RegistroId = id,
                    LaboratorioId = id,
                    Descripcion = "«" + (nombre ?? "Laboratorio " + id) + "» (no tenía horario ni sesiones).",
                    Antes = HistorialCambios.Datos("Nombre", nombre)
                });
                transaccion.Commit();
            }

            Log.Info("Laboratorio id " + id + " borrado (no tenia uso) por " + Quien() + ".");
        }

        /// <summary>Quitar un laboratorio con historial y sin clases vigentes: queda deshabilitado.</summary>
        public void Desactivar(int id)
        {
            Permisos.Exigir("Deshabilitar un laboratorio");
            EnUnaTransaccion((conexion, transaccion) => EnEstado(conexion, transaccion, id, false, true));
            Log.Info("Deshabilitado el laboratorio id " + id + " por " + Quien() + ".");
        }

        public void Reactivar(int id)
        {
            CambiarHabilitados(new int[0], new[] { id });
        }

        /// <summary>
        /// Deshabilita unos y habilita otros, todo o nada. Deshabilitar aqui SI deja
        /// clases vigentes: su horario se conserva y vuelve tal cual (LaboratoriosDeshabilitados).
        /// </summary>
        public void CambiarHabilitados(ICollection<int> deshabilitar, ICollection<int> habilitar)
        {
            Permisos.Exigir("Habilitar o deshabilitar laboratorios");
            EnUnaTransaccion((conexion, transaccion) =>
            {
                foreach (int id in deshabilitar)
                {
                    EnEstado(conexion, transaccion, id, false, false);
                }

                foreach (int id in habilitar)
                {
                    EnEstado(conexion, transaccion, id, true, false);
                }
            });
            Log.Info("Laboratorios por " + Quien() + ": deshabilitados [" + string.Join(", ", deshabilitar) + "], habilitados [" + string.Join(", ", habilitar) + "].");
        }

        private void EnUnaTransaccion(Action<MySqlConnection, MySqlTransaction> accion)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                if (!Esquema.LaboratoriosDesactivables(conexion, transaccion))
                {
                    throw new ErrorDeDatos("La base de datos todavía no permite deshabilitar laboratorios: falta aplicar db/08_migracion_bajas.sql. "
                        + "Pide a quien administra el servidor que la aplique.");
                }

                accion(conexion, transaccion);
                transaccion.Commit();
            }
        }

        private static void EnEstado(MySqlConnection conexion, MySqlTransaction transaccion, int id, bool activo, bool soloSinClases)
        {
            // Como estaba: solo se anota si cambia.
            string nombre = null;
            bool? estaba = null;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT nombre_laboratorio, activo FROM laboratorios WHERE idlaboratorios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        nombre = Convert.ToString(reader["nombre_laboratorio"], CultureInfo.InvariantCulture);
                        estaba = Convert.ToInt32(reader["activo"], CultureInfo.InvariantCulture) == 1;
                    }
                }
            }

            // Una franja terminada (db/20) de una clase que sigue no lo impide:
            // es historia, como las sesiones.
            using (MySqlCommand comando = new MySqlCommand(!soloSinClases
                ? "UPDATE laboratorios SET activo = @Activo WHERE idlaboratorios = @Id"
                : @"UPDATE laboratorios l SET l.activo = @Activo
                     WHERE l.idlaboratorios = @Id
                       AND NOT EXISTS (SELECT 1 FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases
                                        WHERE h.laboratorios_idlaboratorios = @Id
                                          AND " + FranjaSinTerminar(Esquema.VigenciaDeFranjas(conexion, transaccion)) + ")", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                comando.Parameters.AddWithValue("@Activo", activo ? 1 : 0);
                if (comando.ExecuteNonQuery() == 0 && soloSinClases)
                {
                    throw new ErrorDeDatos("Ese laboratorio tiene clases vigentes en el horario: para quitarlo, muévelas o quítalas en Horarios.");
                }
            }

            if (!estaba.HasValue || estaba.Value == activo)
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion, transaccion))
            {
                LaboratoriosDeshabilitados.Anotar(conexion, transaccion, id, !activo, Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture));
            }

            if (HistorialCambios.Activo(conexion, transaccion))
            {
                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Laboratorio,
                    Accion = activo ? AccionDeCambio.Activar : AccionDeCambio.Desactivar,
                    Tabla = "laboratorios",
                    RegistroId = id,
                    LaboratorioId = id,
                    Descripcion = "«" + nombre + (activo ? "» se habilita: sus clases vuelven a correr."
                        : "» se deshabilita: conserva su horario y su historial, pero sus clases no corren."),
                    Antes = HistorialCambios.Datos("Nombre", nombre, "Habilitado", HistorialCambios.SiNo(estaba.Value)),
                    Despues = HistorialCambios.Datos("Nombre", nombre, "Habilitado", HistorialCambios.SiNo(activo))
                });
            }
        }

        /// <summary>
        /// SQL: la franja (h) de la clase (c) se da hoy o se dara: clase activa y
        /// el ultimo dia de la franja (el de su clase o, con db/20, el suyo si es
        /// antes) no ha pasado.
        /// </summary>
        private static string FranjaSinTerminar(bool conVigencia)
        {
            return "c.activa = 1 AND " + VigenciaFranjas.Hasta(conVigencia) + " >= CURDATE()";
        }

        private static void ComprobarNombreLibre(MySqlConnection conexion, MySqlTransaction transaccion, string nombre, int? excepto)
        {
            // Con el cotejamiento de la tabla, "Cisco" y "CISCO" son el mismo nombre,
            // igual que para el equipo que busca su laboratorio por nombre.
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM laboratorios WHERE nombre_laboratorio = @Nombre" + (excepto.HasValue ? " AND idlaboratorios <> @Id" : ""), conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Nombre", nombre);
                if (excepto.HasValue)
                {
                    comando.Parameters.AddWithValue("@Id", excepto.Value);
                }

                if (Convert.ToInt32(comando.ExecuteScalar()) > 0)
                {
                    throw new ErrorDeDatos("Ya hay un laboratorio llamado " + nombre + ": escribe otro nombre.");
                }
            }
        }

        /// <summary>
        /// Los modos de los laboratorios en uso: la mayoria se cuenta igual que en
        /// el panel, Ajustes y "Codigos de las clases" (los desactivados no cuentan).
        /// </summary>
        private static List<ModoCodigo> ModosDeLosActivos(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            List<ModoCodigo> modos = new List<ModoCodigo>();
            using (MySqlCommand comando = new MySqlCommand("SELECT modo_codigo FROM laboratorios"
                + (Esquema.LaboratoriosDesactivables(conexion, transaccion) ? " WHERE activo = 1" : ""), conexion, transaccion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    modos.Add(ModosDeAcceso.Desde(Convert.ToString(reader["modo_codigo"], CultureInfo.InvariantCulture)));
                }
            }

            return modos;
        }

        private static string Quien()
        {
            return Datos_User.getUser() ?? "-";
        }
    }
}
