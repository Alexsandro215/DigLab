using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>
    /// La ultima sesion (o asistencia dada, db/22) de una clase de ahora despues
    /// del ultimo dia que se le quiere dar: con ella no se aplica, porque la clase
    /// no puede terminar antes de su ultima sesion.
    /// </summary>
    public class SesionPosterior
    {
        public DateTime Fecha;
        public string Materia = "";
        public string Grupo = "";
        /// <summary>Lo de ese dia es una asistencia que dio su docente (db/22), no una sesion.</summary>
        public bool EsAsistenciaDada;

        /// <summary>"Bases de datos con ISC-5002".</summary>
        public string Clase
        {
            get { return Materia + " con " + Grupo; }
        }
    }

    /// <summary>
    /// Una clase de la configuracion cuya misma persona docente, grupo y materia
    /// ya esta en Clases y tambien vale en esas fechas (regla R6, la de
    /// HorariosDatos.ExigirSinCruceDeTerna): no deja aplicar.
    /// </summary>
    public class TernaCruzada
    {
        public int CargaId;
        public int ClaseId;
        public string Materia = "";
        public string Grupo = "";
        /// <summary>"Laura Cardenas".</summary>
        public string Profesor = "";
        public DateTime Desde;
        public DateTime Hasta;
    }

    /// <summary>
    /// Un cambio "desde una fecha" (db/20) de una clase de ahora que empieza
    /// despues del ultimo dia que se le da: ya no llegara a darse.
    /// </summary>
    public class CambioQueNoLlega
    {
        public int FranjaId;
        public string Materia = "";
        public string Grupo = "";
        public DateTime Desde;
    }

    /// <summary>Una sesion extra de una clase de ahora despues del ultimo dia que se le da: para el estudiante ya no vale.</summary>
    public class ExtraFuera
    {
        public int ExcepcionId;
        public string Materia = "";
        public string Grupo = "";
        public DateTime Fecha;
    }

    /// <summary>
    /// Todo lo que revisa la pagina de Aplicacion (y otra vez Aplicar, dentro de
    /// su transaccion) para unas fechas: CicloPreliminarDatos.LeerParaAplicar.
    /// Los renglones de "Antes de aplicar" salen de aqui con RevisionAplicacion.
    /// </summary>
    public class DatosDeAplicacion
    {
        /// <summary>El horario en preparacion leido con fechas (con YaProgramadas).</summary>
        public CicloLeido Leido;
        /// <summary>RevisionCiclo.Revisar(Leido): choques (tambien con las YaProgramadas), sin docente, de baja...</summary>
        public RevisionDelCiclo Revision;
        /// <summary>Las fechas con que se leyo.</summary>
        public DatosDeFechas Fechas;
        /// <summary>Hoy segun el servidor.</summary>
        public DateTime Hoy;

        /// <summary>Las clases que valen el ultimo dia del horario de ahora y siguen despues: se recortan (tambien las desactivadas).</summary>
        public int Terminan;
        /// <summary>De ellas, las activas.</summary>
        public int TerminanActivas;
        /// <summary>La fecha de fin mas comun de las activas que se recortan.</summary>
        public DateTime? FinMasComun;
        /// <summary>El vigente_desde mas temprano de las activas que valen ese dia: las que se guardan "como quedo" (para su nombre).</summary>
        public DateTime? DesdeMasTemprano;
        /// <summary>Las clases activas que valen el ultimo dia del horario de ahora: las que se guardan "como quedo".</summary>
        public int ValenElFin;

        /// <summary>La ultima sesion despues del ultimo dia de alguna clase que se recorta, o null.</summary>
        public SesionPosterior SesionPosterior;
        public List<TernaCruzada> TernasCruzadas = new List<TernaCruzada>();
        public List<CambioQueNoLlega> CambiosQueNoLlegan = new List<CambioQueNoLlega>();
        public List<ExtraFuera> ExtrasFuera = new List<ExtraFuera>();
        /// <summary>Las personas docentes de la configuracion sin codigo (usuarios.codigo_clase vacio): lo reciben al aplicar.</summary>
        public List<string> DocentesSinCodigo = new List<string>();
        /// <summary>Los nombres de las otras configuraciones aplicadas (el nombre en el registro no se repite).</summary>
        public List<string> NombresAplicadas = new List<string>();
        /// <summary>El nombre que se propone para guardar el horario de ahora "como quedo" (ya libre entre las guardadas).</summary>
        public string PropuestaGuardar = "";

        /// <summary>Los cambios de horario desde una fecha estan encendidos (db/20 y la casilla de Ajustes).</summary>
        public bool ConFecha;
        /// <summary>La base tiene la vigencia de las franjas (db/20).</summary>
        public bool ConVigencia;

        /// <summary>Las clases que se crean (las de la configuracion).</summary>
        public int Clases
        {
            get { return Leido == null ? 0 : Leido.Cargas.Count(c => !c.Quitada); }
        }

        /// <summary>Los horarios (franjas) que se crean.</summary>
        public int Horarios
        {
            get { return Leido == null ? 0 : Leido.Cargas.Where(c => !c.Quitada).Sum(c => c.Franjas.Count); }
        }
    }

    /// <summary>Lo que hizo Aplicar.</summary>
    public class ResultadoAplicar
    {
        public int CicloId;
        /// <summary>El nombre con que quedo en el registro.</summary>
        public string Nombre = "";
        public DateTime Desde;
        public DateTime Hasta;
        public DateTime Fin;
        public int Clases;
        public int Horarios;
        /// <summary>Las clases de antes que ahora terminan en Fin.</summary>
        public int Terminan;
        /// <summary>La guardada "como quedo", si se pidio y ese dia habia clases.</summary>
        public int? GuardadaId;
        public string GuardadaNombre;
        /// <summary>Se pidio guardar el horario de ahora, pero ese dia no tenia clases: no se guardo.</summary>
        public bool SinClasesQueGuardar;
        /// <summary>Las personas docentes que recibieron su codigo al aplicar.</summary>
        public List<string> RecibieronCodigo = new List<string>();
    }

    /// <summary>Lo que hizo Deshacer.</summary>
    public class ResultadoDeshacer
    {
        public string Nombre = "";
        public int ClasesBorradas;
        /// <summary>Las clases de antes que recuperaron su fecha de fin.</summary>
        public int Devueltas;
        /// <summary>Las que no se tocaron porque se les cambio la fecha de fin despues de aplicar.</summary>
        public int NoDevueltas;
        /// <summary>Las que no se tocaron porque ya hay otra igual (misma docente, grupo y materia) en esas fechas (regla R6).</summary>
        public int PorOtraIgual;
    }

    /// <summary>Lo que ensena la pestana Aplicacion de una aplicada que todavia no empieza.</summary>
    public class InfoDeAplicada
    {
        /// <summary>Las clases de antes que se recortaron al aplicarla.</summary>
        public int Recortadas;
        /// <summary>Las clases que creo y siguen en Clases.</summary>
        public int Creadas;
        /// <summary>Por que no se puede deshacer, o null si se puede.</summary>
        public string Motivo;
    }

    /// <summary>
    /// Aplicar y Deshacer una configuracion de horario, y lo que lee la pestana
    /// Aplicacion (especificacion v2, 3.5). Aplicar solo hace INSERT de clases y
    /// de franjas nuevas (sin fecha propia: su vigencia es la de su clase, asi un
    /// DigBit anterior a la 20 no ve dos veces la misma hora) y UPDATE de
    /// vigente_hasta de las clases que valen el ultimo dia del horario de ahora,
    /// nunca a una fecha anterior a su ultima sesion. Nunca pone activa = 0,
    /// nunca borra, nunca cambia franjas, excepciones ni sesiones, y anota un solo
    /// renglon en el historial. Deshacer es lo contrario: borra solo las clases
    /// que creo, si no tienen sesiones ni asistencias dadas, y devuelve su fecha
    /// de fin a las que recorto, si nadie la cambio despues. Todo en una
    /// transaccion; solo la administracion escribe (Permisos.Exigir).
    /// </summary>
    internal partial class CicloPreliminarDatos
    {
        /// <summary>SQL: la clase (alias c) vale el ultimo dia del horario de ahora (@Fin) y sigue despues: se recorta.</summary>
        private const string SeRecorta = "c.vigente_desde <= @Fin AND c.vigente_hasta > @Fin";

        // ---------------------------------------------------------------------
        // Lo que lee la pagina de aplicar
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lo que revisa "Antes de aplicar" para esas fechas: el horario en
        /// preparacion con las clases que ya estan en Clases en esas fechas, las
        /// que terminan, sus sesiones posteriores, las ternas cruzadas, los
        /// cambios y las extras que quedan fuera, quien recibe codigo y si los
        /// cambios desde una fecha estan encendidos. Solo lee; la pantalla lo
        /// pide en segundo plano.
        /// </summary>
        public DatosDeAplicacion LeerParaAplicar(int idCiclo, DateTime desde, DateTime hasta, DateTime fin)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerParaAplicar(conexion, null, idCiclo, new DatosDeFechas { Desde = desde.Date, Hasta = hasta.Date, Fin = fin.Date });
            }
        }

        /// <summary>
        /// La fecha de fin mas comun de las clases activas que valen hoy (para
        /// proponer el primer dia del horario nuevo: el dia siguiente), o null.
        /// </summary>
        public DateTime? FinMasComunDeHoy()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.vigente_hasta FROM clases c
                 WHERE c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta
                 GROUP BY c.vigente_hasta ORDER BY COUNT(*) DESC, c.vigente_hasta DESC LIMIT 1", conexion))
            {
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor).Date;
            }
        }

        internal static DatosDeAplicacion LeerParaAplicar(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, DatosDeFechas fechas)
        {
            DatosDeAplicacion datos = new DatosDeAplicacion { Fechas = fechas };
            datos.Leido = Leer(conexion, transaccion, idCiclo, fechas);
            datos.Hoy = datos.Leido.Hoy;
            datos.Revision = RevisionCiclo.Revisar(datos.Leido);
            datos.ConVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            datos.ConFecha = VigenciaFranjas.ConFecha(conexion, transaccion);
            bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
            Action<MySqlCommand> conFechas = c =>
            {
                c.Parameters.AddWithValue("@C", idCiclo);
                c.Parameters.AddWithValue("@Desde", fechas.Desde.Date);
                c.Parameters.AddWithValue("@Hasta", fechas.Hasta.Date);
                c.Parameters.AddWithValue("@Fin", fechas.Fin.Date);
            };

            // 1) Las que terminan: todas las que valen ese dia y siguen despues
            //    (tambien una futura que empiece antes y las desactivadas).
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT COUNT(*) AS n, COALESCE(SUM(c.activa = 1), 0) AS activas,
                       (SELECT MIN(v.vigente_desde) FROM clases v WHERE v.activa = 1 AND @Fin BETWEEN v.vigente_desde AND v.vigente_hasta) AS desde_min,
                       (SELECT COUNT(*) FROM clases v WHERE v.activa = 1 AND @Fin BETWEEN v.vigente_desde AND v.vigente_hasta) AS valen
                FROM clases c WHERE " + SeRecorta, conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        datos.Terminan = Convert.ToInt32(reader["n"]);
                        datos.TerminanActivas = Convert.ToInt32(reader["activas"]);
                        datos.DesdeMasTemprano = Fecha(reader, "desde_min");
                        datos.ValenElFin = Convert.ToInt32(reader["valen"]);
                    }
                }
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.vigente_hasta FROM clases c WHERE c.activa = 1 AND " + SeRecorta + @"
                 GROUP BY c.vigente_hasta ORDER BY COUNT(*) DESC, c.vigente_hasta DESC LIMIT 1", conexion, transaccion))
            {
                conFechas(comando);
                object valor = comando.ExecuteScalar();
                datos.FinMasComun = valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor).Date;
            }

            // 2) La ultima sesion posterior al ultimo dia de las que se recortan.
            datos.SesionPosterior = UltimaSesionDespues(conexion, transaccion, conFechas, conDadas);

            // 3) R6: la misma terna ya esta en Clases en esas fechas. Las que se
            //    recortan no hace falta mirarlas: terminan en @Fin, antes de @Desde.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT cc.idcarga, c.idclases, c.vigente_desde, c.vigente_hasta, m.nombre_materia, g.nombre_grupo,
                       u.nombre AS p_nombre, u.apellido_paterno AS p_paterno
                FROM ciclo_cargas cc
                INNER JOIN clases c   ON c.usuarios_idusuarios = cc.profesor AND c.grupos_idgrupos = cc.grupo AND c.materias_id_materia = cc.materia
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                WHERE cc.idciclo = @C AND cc.quitada = 0 AND c.activa = 1
                  AND c.vigente_desde > @Fin AND c.vigente_desde <= @Hasta AND c.vigente_hasta >= @Desde
                ORDER BY c.vigente_desde, m.nombre_materia, g.nombre_grupo", conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        datos.TernasCruzadas.Add(new TernaCruzada
                        {
                            CargaId = reader.GetInt32("idcarga"),
                            ClaseId = reader.GetInt32("idclases"),
                            Desde = reader.GetDateTime("vigente_desde").Date,
                            Hasta = reader.GetDateTime("vigente_hasta").Date,
                            Materia = Texto(reader, "nombre_materia"),
                            Grupo = Texto(reader, "nombre_grupo"),
                            Profesor = HorariosDatos.NombreCorto(Texto(reader, "p_nombre"), Texto(reader, "p_paterno"))
                        });
                    }
                }
            }

            // 4) Cambios "desde una fecha" (db/20) de las que se recortan que ya no llegan.
            if (datos.ConVigencia)
            {
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT h.idhorarios, h.vigente_desde, m.nombre_materia, g.nombre_grupo
                    FROM horarios h
                    INNER JOIN clases c   ON c.idclases = h.clases_idclases
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    WHERE c.activa = 1 AND " + SeRecorta + @" AND h.vigente_desde > @Fin AND h.vigente_desde <= c.vigente_hasta
                    ORDER BY h.vigente_desde, m.nombre_materia, g.nombre_grupo", conexion, transaccion))
                {
                    conFechas(comando);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            datos.CambiosQueNoLlegan.Add(new CambioQueNoLlega
                            {
                                FranjaId = reader.GetInt32("idhorarios"),
                                Desde = reader.GetDateTime("vigente_desde").Date,
                                Materia = Texto(reader, "nombre_materia"),
                                Grupo = Texto(reader, "nombre_grupo")
                            });
                        }
                    }
                }
            }

            // 5) Sesiones extra de las que se recortan despues de su ultimo dia.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.idhorario_excepciones, x.fecha, m.nombre_materia, g.nombre_grupo
                FROM horario_excepciones x
                INNER JOIN clases c   ON c.idclases = x.clases_idclases
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                WHERE x.tipo = 'extra' AND x.fecha > @Fin AND x.fecha <= c.vigente_hasta AND c.activa = 1 AND " + SeRecorta + @"
                ORDER BY x.fecha, m.nombre_materia, g.nombre_grupo", conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        datos.ExtrasFuera.Add(new ExtraFuera
                        {
                            ExcepcionId = reader.GetInt32("idhorario_excepciones"),
                            Fecha = reader.GetDateTime("fecha").Date,
                            Materia = Texto(reader, "nombre_materia"),
                            Grupo = Texto(reader, "nombre_grupo")
                        });
                    }
                }
            }

            // 6) Quien recibe codigo (db/09: sin ella Aplicar se niega).
            if (Esquema.CodigoPorProfesor(conexion, transaccion))
            {
                foreach (KeyValuePair<int, string> docente in DocentesSinCodigo(conexion, transaccion, idCiclo))
                {
                    datos.DocentesSinCodigo.Add(docente.Value);
                }
            }

            // 7) Los nombres de las otras aplicadas y la propuesta para la guardada "como quedo".
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT nombre FROM ciclos_preliminares WHERE estado = 'publicado' AND idciclo <> @C ORDER BY idciclo", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        datos.NombresAplicadas.Add(reader.GetString(0));
                    }
                }
            }

            datos.PropuestaGuardar = NombreLibre(conexion, transaccion,
                NombrePropuesto(datos.DesdeMasTemprano ?? fechas.Fin.Date, fechas.Fin.Date), EstadoConfiguracion.Guardada);
            return datos;
        }

        /// <summary>
        /// La ultima sesion (con db/22, tambien una asistencia dada) despues de
        /// @Fin de las clases que se recortan, o null. Son las de
        /// VigenciaFranjas.SesionDeLaClase y DiaDeSesion, pero por partes que
        /// usan los indices (enlazadas a una de sus franjas, a una de sus extras,
        /// o sin enlace de su terna dentro de su vigencia), como
        /// HorariosDatos.AnotarSesionesDeClases: con el OR de SesionDeLaClase,
        /// MariaDB 5.5 recorreria codigos_accesos entera por cada clase.
        /// </summary>
        private static SesionPosterior UltimaSesionDespues(MySqlConnection conexion, MySqlTransaction transaccion, Action<MySqlCommand> conFechas, bool conDadas)
        {
            string dia = VigenciaFranjas.DiaDeSesion("s");
            string nombres = @"
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                WHERE " + SeRecorta;
            string sql = @"
                (SELECT " + dia + @" AS dia, 0 AS dada, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN horarios hh       ON hh.clases_idclases = c.idclases
                   INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios" + nombres + " AND " + dia + @" > @Fin
                  ORDER BY 1 DESC LIMIT 1)
                UNION ALL
                (SELECT " + dia + @", 0, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN horario_excepciones xx ON xx.clases_idclases = c.idclases
                   INNER JOIN codigos_accesos s      ON s.horario_excepciones_id = xx.idhorario_excepciones" + nombres + " AND " + dia + @" > @Fin
                  ORDER BY 1 DESC LIMIT 1)
                UNION ALL
                (SELECT " + dia + @", 0, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN codigos_accesos s
                           ON s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
                          AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                          AND s.grupos_idgrupos = c.grupos_idgrupos AND " + dia + " BETWEEN c.vigente_desde AND c.vigente_hasta" + nombres + " AND " + dia + @" > @Fin
                  ORDER BY 1 DESC LIMIT 1)"
                + (conDadas ? @"
                UNION ALL
                (SELECT ad.fecha, 1, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN asistencias_dadas ad ON ad.fk_clase = c.idclases" + nombres + @" AND ad.fecha > @Fin
                  ORDER BY 1 DESC LIMIT 1)" : "");
            SesionPosterior ultima = null;
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0))
                        {
                            continue;
                        }

                        DateTime fecha = Convert.ToDateTime(reader.GetValue(0)).Date;
                        if (ultima == null || fecha > ultima.Fecha)
                        {
                            ultima = new SesionPosterior
                            {
                                Fecha = fecha,
                                EsAsistenciaDada = Convert.ToInt32(reader.GetValue(1)) == 1,
                                Materia = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Grupo = reader.IsDBNull(3) ? "" : reader.GetString(3)
                            };
                        }
                    }
                }
            }

            return ultima;
        }

        /// <summary>Las personas docentes de la configuracion sin codigo (usuarios.codigo_clase vacio), por id, con su nombre corto. Necesita db/09.</summary>
        private static List<KeyValuePair<int, string>> DocentesSinCodigo(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo)
        {
            List<KeyValuePair<int, string>> docentes = new List<KeyValuePair<int, string>>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT DISTINCT u.idusuarios, u.nombre, u.apellido_paterno
                FROM ciclo_cargas cc
                INNER JOIN usuarios u ON u.idusuarios = cc.profesor
                WHERE cc.idciclo = @C AND cc.quitada = 0 AND (u.codigo_clase IS NULL OR u.codigo_clase = '')
                ORDER BY u.apellido_paterno, u.nombre", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        docentes.Add(new KeyValuePair<int, string>(reader.GetInt32("idusuarios"),
                            HorariosDatos.NombreCorto(Texto(reader, "nombre"), Texto(reader, "apellido_paterno"))));
                    }
                }
            }

            return docentes;
        }

        // ---------------------------------------------------------------------
        // Aplicar
        // ---------------------------------------------------------------------

        /// <summary>
        /// Aplica el horario en preparacion: sus clases quedan en Clases del
        /// primer al ultimo dia (desde, hasta) con sus franjas sin fecha propia, y
        /// las que valen el ultimo dia del horario de ahora (fin) terminan ese dia.
        /// Con guardarAhoraComo (null = no), antes guarda el horario de ese dia
        /// como quedo. Se revisa otra vez todo dentro de la transaccion
        /// (RevisionAplicacion): con algo en rojo, ErrorDeDatos con su texto y no
        /// cambia nada. Un solo renglon en el historial.
        /// </summary>
        public ResultadoAplicar Aplicar(int idCiclo, string nombre, DateTime desde, DateTime hasta, DateTime fin, string guardarAhoraComo, int idAdministrador)
        {
            Permisos.Exigir("Aplicar un horario");
            DatosDeFechas fechas = new DatosDeFechas { Desde = desde.Date, Hasta = hasta.Date, Fin = fin.Date };
            string guardar = string.IsNullOrWhiteSpace(guardarAhoraComo) ? null : guardarAhoraComo.Trim();
            if (guardar != null && guardar.Length > LargoNombre)
            {
                throw new ErrorDeDatos("El nombre con que se guarda el horario de ahora puede tener hasta " + LargoNombre + " letras.");
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ExigirBaseParaAplicar(conexion, transaccion);

                // a) Que siga en preparacion, que no haya otra aplicada por empezar y el nombre.
                Bloquear(conexion, transaccion, idCiclo, EstadoConfiguracion.EnPreparacion);
                string porEmpezar = Convert.ToString(ValorEnLaBase(conexion, transaccion,
                    "SELECT nombre FROM ciclos_preliminares WHERE estado = 'publicado' AND desde > CURDATE() ORDER BY desde LIMIT 1 FOR UPDATE", null));
                if (!string.IsNullOrEmpty(porEmpezar))
                {
                    throw new ErrorDeDatos("«" + porEmpezar + "» ya está aplicado y todavía no empieza: deshaz su aplicación para aplicar otro.");
                }

                nombre = ValidarNombre(conexion, transaccion, nombre, EstadoConfiguracion.Aplicada, idCiclo);

                // Las clases que se recortan, bloqueadas antes de leer: nadie las
                // cambia entre la revision y el recorte.
                ValorEnLaBase(conexion, transaccion, "SELECT COUNT(*) FROM clases c WHERE " + SeRecorta + " FOR UPDATE",
                    c => c.Parameters.AddWithValue("@Fin", fechas.Fin));

                // b) La misma revision que la pantalla, aqui dentro.
                DatosDeAplicacion datos = LeerParaAplicar(conexion, transaccion, idCiclo, fechas);
                RenglonRevision rojo = RevisionAplicacion.Revisar(datos, fechas.Desde, fechas.Hasta, fechas.Fin, nombre).FirstOrDefault(r => r.Bloquea);
                if (rojo != null)
                {
                    throw new ErrorDeDatos(rojo.Texto);
                }

                ResultadoAplicar resultado = new ResultadoAplicar
                {
                    CicloId = idCiclo,
                    Nombre = nombre,
                    Desde = fechas.Desde,
                    Hasta = fechas.Hasta,
                    Fin = fechas.Fin
                };
                Action<MySqlCommand> parametros = c =>
                {
                    c.Parameters.AddWithValue("@C", idCiclo);
                    c.Parameters.AddWithValue("@Desde", fechas.Desde);
                    c.Parameters.AddWithValue("@Hasta", fechas.Hasta);
                    c.Parameters.AddWithValue("@Fin", fechas.Fin);
                    c.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                };

                // c) El horario de ahora como quedo, antes de tocar nada.
                if (guardar != null)
                {
                    string nombreGuardada = NombreLibre(conexion, transaccion, guardar, EstadoConfiguracion.Guardada);
                    int idGuardada = InsertarCiclo(conexion, transaccion, nombreGuardada, EstadoConfiguracion.Guardada, fechas.Fin, null, idAdministrador);
                    if (CopiarClases(conexion, transaccion, idGuardada, fechas.Fin, null, null, true) == 0)
                    {
                        CambiarEnLaBase(conexion, transaccion, "DELETE FROM ciclos_preliminares WHERE idciclo = @G AND estado = 'guardado'",
                            c => c.Parameters.AddWithValue("@G", idGuardada));
                        resultado.SinClasesQueGuardar = true;
                    }
                    else
                    {
                        resultado.GuardadaId = idGuardada;
                        resultado.GuardadaNombre = nombreGuardada;
                    }
                }

                // d) Recortes: todas las que valen @Fin y siguen despues terminan ese
                //    dia. activa nunca se toca. Lo de antes queda en ciclo_recortes.
                resultado.Terminan = CambiarEnLaBase(conexion, transaccion, @"
                    INSERT INTO ciclo_recortes (idciclo, clase, hasta_antes)
                    SELECT @C, c.idclases, c.vigente_hasta FROM clases c WHERE " + SeRecorta + " ORDER BY c.idclases", parametros);
                CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE clases c INNER JOIN ciclo_recortes r ON r.clase = c.idclases AND r.idciclo = @C
                       SET c.vigente_hasta = @Fin", parametros);

                // e) Codigos: cada clase nueva lleva el de su docente; quien no tiene, lo recibe.
                foreach (KeyValuePair<int, string> docente in DocentesSinCodigo(conexion, transaccion, idCiclo))
                {
                    CodigosDeProfesor.ObtenerOCrear(conexion, transaccion, docente.Key);
                    resultado.RecibieronCodigo.Add(docente.Value);
                }

                // f) Las clases, con un INSERT ... SELECT, y cada clase de la
                //    configuracion enlazada a la suya (la terna es unica entre las nuevas).
                int esperadas = Convert.ToInt32(ValorEnLaBase(conexion, transaccion,
                    "SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = @C AND quitada = 0", parametros));
                int antes = Convert.ToInt32(ValorEnLaBase(conexion, transaccion, "SELECT COALESCE(MAX(idclases), 0) FROM clases", null));
                CambiarEnLaBase(conexion, transaccion, "UPDATE ciclo_cargas SET clase_publicada = NULL WHERE idciclo = @C", parametros);
                resultado.Clases = CambiarEnLaBase(conexion, transaccion, @"
                    INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta,
                                        activa, creada_por, creada_en)
                    SELECT cc.profesor, cc.grupo, cc.materia, u.codigo_clase, @Desde, @Hasta, 1, @Admin, NOW()
                    FROM ciclo_cargas cc
                    INNER JOIN usuarios u ON u.idusuarios = cc.profesor
                    WHERE cc.idciclo = @C AND cc.quitada = 0
                    ORDER BY cc.idcarga", parametros);
                if (resultado.Clases != esperadas)
                {
                    throw new ErrorDeDatos("No se pudieron crear todas las clases (" + resultado.Clases + " de " + esperadas
                        + "): alguna persona docente ya no está en DigBit. Vuelve a entrar a la pestaña y revisa el horario.");
                }

                CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE ciclo_cargas cc
                    INNER JOIN clases c ON c.idclases > @Antes AND c.usuarios_idusuarios = cc.profesor AND c.grupos_idgrupos = cc.grupo
                                       AND c.materias_id_materia = cc.materia AND c.vigente_desde = @Desde AND c.vigente_hasta = @Hasta
                                       AND c.creada_por <=> @Admin
                       SET cc.clase_publicada = c.idclases
                     WHERE cc.idciclo = @C AND cc.quitada = 0", c =>
                    {
                        parametros(c);
                        c.Parameters.AddWithValue("@Antes", antes);
                    });
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT COALESCE(SUM(clase_publicada IS NULL), 0), COUNT(DISTINCT clase_publicada), COALESCE(SUM(clase_publicada <= @Antes), 0)
                    FROM ciclo_cargas WHERE idciclo = @C AND quitada = 0", conexion, transaccion))
                {
                    parametros(comando);
                    comando.Parameters.AddWithValue("@Antes", antes);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        reader.Read();
                        if (Convert.ToInt32(reader.GetValue(0)) != 0 || Convert.ToInt32(reader.GetValue(1)) != esperadas || Convert.ToInt32(reader.GetValue(2)) != 0)
                        {
                            throw new ErrorDeDatos("No se pudo enlazar cada clase nueva con la suya: vuelve a intentarlo. No cambió nada.");
                        }
                    }
                }

                // g) Las franjas, sin fecha propia (vigente_desde y vigente_hasta en
                //    NULL): su vigencia es la de su clase (R3).
                resultado.Horarios = CambiarEnLaBase(conexion, transaccion, @"
                    INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin)
                    SELECT cc.clase_publicada, f.laboratorio, f.dia_semana, f.hora_inicio, f.hora_fin
                    FROM ciclo_franjas f
                    INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                    WHERE cc.idciclo = @C AND cc.quitada = 0
                    ORDER BY cc.idcarga, f.idfranja", parametros);

                // h) Queda aplicada, con su vigencia y el ultimo dia del horario de antes.
                int aplicada = CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE ciclos_preliminares
                       SET estado = 'publicado', nombre = @Nombre, desde = @Desde, hasta = @Hasta, fin_anterior = @Fin,
                           publicado_por = @Admin, publicado_en = NOW(), cambiado_en = NOW(), cambiado_por = @Admin
                     WHERE idciclo = @C AND estado = 'borrador'", c =>
                    {
                        parametros(c);
                        c.Parameters.AddWithValue("@Nombre", nombre);
                    });
                if (aplicada != 1)
                {
                    throw new ErrorDeDatos("Esta plantilla ya se aplicó, se guardó o se borró: vuelve a entrar a la pestaña.");
                }

                // i) Un solo renglon para todo el horario (no uno por clase).
                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Horario,
                    Accion = AccionDeCambio.Aplicar,
                    Tabla = "ciclos_preliminares",
                    RegistroId = idCiclo,
                    Fecha = fechas.Desde,
                    Descripcion = "Horario «" + nombre + "» aplicado: " + RevisionCiclo.TextoClases(resultado.Clases) + " "
                        + HistorialCambios.Vigencia(fechas.Desde, fechas.Hasta) + "; "
                        + (resultado.Terminan == 0
                            ? "ninguna clase de antes se recorta."
                            : (resultado.Terminan == 1 ? "la de antes termina" : "las " + resultado.Terminan + " de antes terminan")
                              + " el " + HistorialCambios.Fecha(fechas.Fin) + "."),
                    Antes = null,
                    Despues = HistorialCambios.Datos(
                        "Vigencia", HistorialCambios.Vigencia(fechas.Desde, fechas.Hasta),
                        "Clases nuevas", resultado.Clases.ToString(CultureInfo.InvariantCulture),
                        "Horarios", resultado.Horarios.ToString(CultureInfo.InvariantCulture),
                        "Terminan", RevisionCiclo.TextoClases(resultado.Terminan) + " el " + HistorialCambios.Fecha(fechas.Fin))
                });

                transaccion.Commit();
                Log.Info("Horario " + idCiclo + " (" + nombre + ") aplicado del " + fechas.Desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + " al " + fechas.Hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + resultado.Clases + " clases con "
                    + resultado.Horarios + " horarios; " + resultado.Terminan + " clases de antes terminan el "
                    + fechas.Fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + (resultado.GuardadaId.HasValue ? "; guardado como quedo en " + resultado.GuardadaId.Value : "")
                    + (resultado.RecibieronCodigo.Count > 0 ? "; " + resultado.RecibieronCodigo.Count + " docentes recibieron codigo" : "") + ".");
                return resultado;
            }
        }

        /// <summary>Sin db/16, db/23 o db/09 no se aplica (las pestanas ya lo explican).</summary>
        private static void ExigirBaseParaAplicar(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            if (!Esquema.CicloPreliminar(conexion, transaccion) || !Esquema.HorariosGuardados(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base no tiene lo de las plantillas de horario (db/16_ciclo_preliminar.sql y db/23_horarios_guardados.sql): pide a quien administra el servidor que las aplique.");
            }

            if (!Esquema.CodigoPorProfesor(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base no tiene el código por docente (db/09_codigo_por_profesor.sql): sin él no se sabe qué código dar a las clases nuevas. Pide a quien administra el servidor que la aplique.");
            }
        }

        // ---------------------------------------------------------------------
        // Deshacer
        // ---------------------------------------------------------------------

        /// <summary>
        /// Deshace una aplicada que todavia no empieza: borra las clases que creo
        /// (si no tienen sesiones ni asistencias dadas), devuelve su fecha de fin a
        /// las que recorto (solo si nadie la cambio despues y si no se cruzan con
        /// otra igual) y la deja otra vez en preparacion, con sus fechas como
        /// propuesta. La guardada "como quedo" se queda. Un renglon en el historial.
        /// </summary>
        public ResultadoDeshacer Deshacer(int idCiclo, int idAdministrador)
        {
            Permisos.Exigir("Deshacer la aplicacion de un horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                AplicadaPorDeshacer aplicada = LeerPorDeshacer(conexion, transaccion, idCiclo, true);
                if (aplicada.Motivo != null)
                {
                    throw new ErrorDeDatos(aplicada.Motivo);
                }

                ResultadoDeshacer resultado = new ResultadoDeshacer { Nombre = aplicada.Nombre };
                Action<MySqlCommand> parametros = c =>
                {
                    c.Parameters.AddWithValue("@C", idCiclo);
                    c.Parameters.AddWithValue("@FinAnterior", aplicada.FinAnterior.Date);
                    c.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                };

                // Las que recupera su fecha: las que siguen terminando en el fin que se les
                // puso, menos las que se cruzarian con otra igual que no es de esta aplicada (R6).
                List<int> recortadas = new List<int>();
                List<int> sinCambios = new List<int>();
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT r.clase, c.vigente_hasta FROM ciclo_recortes r INNER JOIN clases c ON c.idclases = r.clase
                     WHERE r.idciclo = @C ORDER BY r.clase FOR UPDATE", conexion, transaccion))
                {
                    parametros(comando);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            recortadas.Add(reader.GetInt32(0));
                            if (reader.GetDateTime(1).Date == aplicada.FinAnterior.Date)
                            {
                                sinCambios.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }

                HashSet<int> conOtraIgual = new HashSet<int>();
                if (sinCambios.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        parametros(comando);
                        comando.CommandText = @"
                            SELECT DISTINCT r.clase
                            FROM ciclo_recortes r
                            INNER JOIN clases c ON c.idclases = r.clase
                            INNER JOIN clases o ON o.usuarios_idusuarios = c.usuarios_idusuarios AND o.grupos_idgrupos = c.grupos_idgrupos
                                               AND o.materias_id_materia = c.materias_id_materia AND o.idclases <> c.idclases AND o.activa = 1
                                               AND o.vigente_desde <= r.hasta_antes AND o.vigente_hasta > @FinAnterior
                            WHERE r.idciclo = @C AND c.activa = 1 AND o.idclases NOT IN (" + Lista(comando, "n", aplicada.Clases) + ")";
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                conOtraIgual.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }

                List<int> devolver = sinCambios.Where(id => !conOtraIgual.Contains(id)).ToList();
                if (devolver.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        parametros(comando);
                        comando.CommandText = @"
                            UPDATE clases c INNER JOIN ciclo_recortes r ON r.clase = c.idclases AND r.idciclo = @C
                               SET c.vigente_hasta = r.hasta_antes
                             WHERE c.vigente_hasta = @FinAnterior AND c.idclases IN (" + Lista(comando, "d", devolver) + ")";
                        resultado.Devueltas = comando.ExecuteNonQuery();
                    }
                }

                resultado.PorOtraIgual = sinCambios.Count(id => conOtraIgual.Contains(id));
                resultado.NoDevueltas = recortadas.Count - sinCambios.Count;

                // Las clases que creo, por lista (no multitabla: la clave SET NULL de
                // ciclo_cargas). Sus franjas y excepciones se van con ellas.
                if (aplicada.Clases.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        comando.CommandText = "DELETE FROM clases WHERE idclases IN (" + Lista(comando, "k", aplicada.Clases) + ")";
                        resultado.ClasesBorradas = comando.ExecuteNonQuery();
                    }
                }

                CambiarEnLaBase(conexion, transaccion, "DELETE FROM ciclo_recortes WHERE idciclo = @C", parametros);
                CambiarEnLaBase(conexion, transaccion, "UPDATE ciclo_cargas SET clase_publicada = NULL WHERE idciclo = @C", parametros);
                int vuelta = CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE ciclos_preliminares
                       SET estado = 'borrador', fin_anterior = NULL, publicado_por = NULL, publicado_en = NULL,
                           cambiado_en = NOW(), cambiado_por = @Admin
                     WHERE idciclo = @C AND estado = 'publicado'", parametros);
                if (vuelta != 1)
                {
                    throw new ErrorDeDatos("Este horario ya no está aplicado: vuelve a entrar a la pestaña.");
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Horario,
                    Accion = AccionDeCambio.Deshacer,
                    Tabla = "ciclos_preliminares",
                    RegistroId = idCiclo,
                    Fecha = aplicada.Desde,
                    Descripcion = "Aplicación de «" + aplicada.Nombre + "» deshecha: se quitaron sus " + RevisionCiclo.TextoClases(resultado.ClasesBorradas)
                        + (resultado.Devueltas == 0 ? " y ninguna recuperó su fecha de fin."
                            : resultado.Devueltas == 1 ? " y 1 recuperó su fecha de fin."
                            : " y " + resultado.Devueltas + " recuperaron su fecha de fin."),
                    Antes = HistorialCambios.Datos(
                        "Vigencia", HistorialCambios.Vigencia(aplicada.Desde, aplicada.Hasta),
                        "Clases nuevas", resultado.ClasesBorradas.ToString(CultureInfo.InvariantCulture),
                        "Terminaban", RevisionCiclo.TextoClases(recortadas.Count) + " el " + HistorialCambios.Fecha(aplicada.FinAnterior)),
                    Despues = HistorialCambios.Datos(
                        "Recuperaron su fecha de fin", RevisionCiclo.TextoClases(resultado.Devueltas),
                        "No se tocaron", resultado.NoDevueltas + resultado.PorOtraIgual == 0 ? null
                            : (resultado.NoDevueltas + resultado.PorOtraIgual).ToString(CultureInfo.InvariantCulture))
                });

                transaccion.Commit();
                Log.Info("Aplicacion del horario " + idCiclo + " (" + aplicada.Nombre + ") deshecha: " + resultado.ClasesBorradas + " clases borradas, "
                    + resultado.Devueltas + " recuperaron su fecha de fin, " + resultado.NoDevueltas + " cambiadas despues y "
                    + resultado.PorOtraIgual + " con otra igual no se tocaron.");
                return resultado;
            }
        }

        /// <summary>Por que no se puede deshacer esa aplicada, o null si se puede. Solo lee.</summary>
        public string PuedeDeshacer(int idCiclo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerPorDeshacer(conexion, null, idCiclo, false).Motivo;
            }
        }

        /// <summary>Lo que ensena la pestana de una aplicada por empezar: cuantas recorto, cuantas creo y si se puede deshacer. Solo lee.</summary>
        public InfoDeAplicada LeerAplicada(int idCiclo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                AplicadaPorDeshacer aplicada = LeerPorDeshacer(conexion, null, idCiclo, false);
                return new InfoDeAplicada
                {
                    Recortadas = Convert.ToInt32(ValorEnLaBase(conexion, null, "SELECT COUNT(*) FROM ciclo_recortes WHERE idciclo = @C",
                        c => c.Parameters.AddWithValue("@C", idCiclo))),
                    Creadas = aplicada.Clases.Count,
                    Motivo = aplicada.Motivo
                };
            }
        }

        /// <summary>Lo que Deshacer necesita saber de una aplicada, y por que no se puede (Motivo), o null.</summary>
        private sealed class AplicadaPorDeshacer
        {
            public string Nombre = "";
            public DateTime Desde;
            public DateTime Hasta;
            public DateTime FinAnterior;
            public List<int> Clases = new List<int>();
            public string Motivo;
        }

        /// <summary>
        /// Las reglas de Deshacer, con o sin bloquear: aplicada y por empezar, sin
        /// otra en preparacion, y sin sesiones ni asistencias dadas en las clases
        /// que creo (VigenciaFranjas.SesionDeLaClase, por partes con indice).
        /// </summary>
        private static AplicadaPorDeshacer LeerPorDeshacer(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, bool bloquear)
        {
            AplicadaPorDeshacer aplicada = new AplicadaPorDeshacer();
            string bloqueo = bloquear ? " FOR UPDATE" : "";
            string estado = null;
            bool empezo = false;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT nombre, estado, desde, hasta, fin_anterior, (desde <= CURDATE()) AS empezo FROM ciclos_preliminares WHERE idciclo = @C" + bloqueo,
                conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        aplicada.Nombre = Texto(reader, "nombre");
                        estado = Texto(reader, "estado");
                        aplicada.Desde = Fecha(reader, "desde") ?? DateTime.MinValue;
                        aplicada.Hasta = Fecha(reader, "hasta") ?? DateTime.MinValue;
                        aplicada.FinAnterior = Fecha(reader, "fin_anterior") ?? DateTime.MinValue;
                        empezo = !reader.IsDBNull(reader.GetOrdinal("empezo")) && Convert.ToInt32(reader["empezo"]) == 1;
                    }
                }
            }

            if (estado != "publicado")
            {
                aplicada.Motivo = "Este horario ya no está aplicado: vuelve a entrar a la pestaña.";
                return aplicada;
            }

            if (empezo)
            {
                aplicada.Motivo = "Ya empezó: los cambios se hacen en Horario semanal y Clases.";
                return aplicada;
            }

            if (aplicada.FinAnterior == DateTime.MinValue)
            {
                aplicada.Motivo = "Esta aplicada no guarda el último día del horario de antes: no se puede deshacer. Los cambios se hacen en Horario semanal y Clases.";
                return aplicada;
            }

            if (Convert.ToInt32(ValorEnLaBase(conexion, transaccion, "SELECT COUNT(*) FROM ciclos_preliminares WHERE estado = 'borrador'" + bloqueo, null)) > 0)
            {
                aplicada.Motivo = "Ya hay otro horario en preparación: guárdalo o descártalo antes de deshacer.";
                return aplicada;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT clase_publicada FROM ciclo_cargas WHERE idciclo = @C AND clase_publicada IS NOT NULL ORDER BY clase_publicada", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        aplicada.Clases.Add(reader.GetInt32(0));
                    }
                }
            }

            if (aplicada.Clases.Count == 0)
            {
                return aplicada;
            }

            if (bloquear)
            {
                // Tambien sus franjas y sus extras: una sesion nueva que se enlace a
                // una de ellas espera a que esto termine (su clave foranea necesita
                // la fila) y, si se borra, falla en vez de quedar sin enlace (SET NULL).
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM clases WHERE idclases IN (" + Lista(comando, "k", aplicada.Clases) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM horarios WHERE clases_idclases IN (" + Lista(comando, "h", aplicada.Clases) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM horario_excepciones WHERE clases_idclases IN (" + Lista(comando, "x", aplicada.Clases) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }
            }

            string dia = VigenciaFranjas.DiaDeSesion("s");
            bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                string a = Lista(comando, "a", aplicada.Clases);
                string b = Lista(comando, "b", aplicada.Clases);
                string c3 = Lista(comando, "c", aplicada.Clases);
                comando.CommandText = @"
                    SELECT (SELECT COUNT(*) FROM horarios hh INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios
                             WHERE hh.clases_idclases IN (" + a + @"))
                         + (SELECT COUNT(*) FROM horario_excepciones xx INNER JOIN codigos_accesos s ON s.horario_excepciones_id = xx.idhorario_excepciones
                             WHERE xx.clases_idclases IN (" + b + @"))
                         + (SELECT COUNT(*) FROM clases c
                             INNER JOIN codigos_accesos s
                                     ON s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
                                    AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                                    AND s.grupos_idgrupos = c.grupos_idgrupos AND " + dia + @" BETWEEN c.vigente_desde AND c.vigente_hasta
                             WHERE c.idclases IN (" + c3 + "))"
                    + (conDadas ? " + (SELECT COUNT(*) FROM asistencias_dadas ad WHERE ad.fk_clase IN (" + Lista(comando, "d", aplicada.Clases) + "))" : "");
                if (Convert.ToInt32(comando.ExecuteScalar()) > 0)
                {
                    aplicada.Motivo = "Ya hay sesiones de sus clases: los cambios se hacen en Horario semanal y Clases.";
                }
            }

            return aplicada;
        }

        // ---------------------------------------------------------------------
        // Utilidades de este archivo
        // ---------------------------------------------------------------------

        private static object ValorEnLaBase(MySqlConnection conexion, MySqlTransaction transaccion, string sql, Action<MySqlCommand> parametros)
        {
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                object valor = comando.ExecuteScalar();
                return valor == DBNull.Value ? null : valor;
            }
        }

        /// <summary>Ejecuta y devuelve las filas tocadas (las encontradas, con MySql.Data).</summary>
        private static int CambiarEnLaBase(MySqlConnection conexion, MySqlTransaction transaccion, string sql, Action<MySqlCommand> parametros)
        {
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                return comando.ExecuteNonQuery();
            }
        }
    }
}
