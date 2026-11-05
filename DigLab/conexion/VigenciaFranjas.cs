using System;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Las sesiones de una clase (VigenciaFranjas.Sesiones).</summary>
    internal sealed class SesionesDeClase
    {
        public int Cuantas;
        public DateTime? Primera;
        public DateTime? Ultima;
    }

    /// <summary>
    /// La vigencia de una franja (db/20): la de su clase recortada a la suya
    /// propia (horarios.vigente_desde/vigente_hasta; NULL = la de la clase).
    /// Todo lo que decide si una franja "se da" en una fecha usa estos
    /// fragmentos de SQL, con y sin db/20, para que una base sin la migracion
    /// siga funcionando como antes.
    ///
    /// Partir franjas (terminar la vieja y crear la nueva desde una fecha)
    /// exige ademas que el administrador lo encienda en Ajustes
    /// (configuracion.franjas_con_fecha = '1'): un DigLab viejo no conoce las
    /// columnas y veria a la vez la franja terminada y la nueva.
    /// </summary>
    internal static class VigenciaFranjas
    {
        /// <summary>La clave en configuracion que enciende los cambios de horario desde una fecha.</summary>
        public const string ClaveConFecha = "franjas_con_fecha";

        /// <summary>
        /// SQL: la franja (alias h) de la clase (alias c) se da en esa fecha (una
        /// expresion SQL: "@Fecha", "CURDATE()", "d.fecha"...): clase activa, la
        /// fecha dentro de la vigencia de la clase y, con db/20, dentro de la de
        /// la franja. No mira el dia de la semana ni las horas.
        /// </summary>
        public static string SeDa(bool conVigencia, string fecha, string h = "h", string c = "c")
        {
            return c + ".activa = 1 AND " + fecha + " BETWEEN " + c + ".vigente_desde AND " + c + ".vigente_hasta"
                + (conVigencia
                    ? " AND (" + h + ".vigente_desde IS NULL OR " + fecha + " >= " + h + ".vigente_desde)"
                      + " AND (" + h + ".vigente_hasta IS NULL OR " + fecha + " <= " + h + ".vigente_hasta)"
                    : "");
        }

        /// <summary>SQL: el primer dia en que se da la franja (el mas tardio de los dos inicios).</summary>
        public static string Desde(bool conVigencia, string h = "h", string c = "c")
        {
            return conVigencia
                ? "GREATEST(" + c + ".vigente_desde, COALESCE(" + h + ".vigente_desde, " + c + ".vigente_desde))"
                : c + ".vigente_desde";
        }

        /// <summary>SQL: el ultimo dia en que se da la franja (el mas temprano de los dos fines).</summary>
        public static string Hasta(bool conVigencia, string h = "h", string c = "c")
        {
            return conVigencia
                ? "LEAST(" + c + ".vigente_hasta, COALESCE(" + h + ".vigente_hasta, " + c + ".vigente_hasta))"
                : c + ".vigente_hasta";
        }

        /// <summary>
        /// SQL: la vigencia de la franja toca el rango [desde, hasta] (expresiones
        /// SQL). Sin "activa": quien la usa decide si cuenta la clase inactiva.
        /// </summary>
        public static string SeCruzaCon(bool conVigencia, string desde, string hasta, string h = "h", string c = "c")
        {
            return Desde(conVigencia, h, c) + " <= " + hasta + " AND " + Hasta(conVigencia, h, c) + " >= " + desde;
        }

        /// <summary>SQL: las columnas propias de la franja para un SELECT (NULL sin db/20).</summary>
        public static string Columnas(bool conVigencia, string h = "h")
        {
            return conVigencia
                ? h + ".vigente_desde AS franja_desde, " + h + ".vigente_hasta AS franja_hasta"
                : "NULL AS franja_desde, NULL AS franja_hasta";
        }

        /// <summary>
        /// SQL: el dia de la sesion (alias s, codigos_accesos). Las de antes de los
        /// horarios pueden no tener fecha: entonces, el dia en que se creo (como
        /// las cuenta la asistencia, AsistenciaDatos.DeLaClase).
        /// </summary>
        public static string DiaDeSesion(string s = "s")
        {
            return "COALESCE(" + s + ".fecha, DATE(" + s + ".hora_registro))";
        }

        /// <summary>
        /// SQL: la condicion "esta sesion (alias s, codigos_accesos) es de la clase
        /// (alias c)": enlazada a una de sus franjas o a una de sus sesiones
        /// extra, o sin enlace (las de antes de los horarios, o de una franja
        /// borrada) del mismo profesor, materia y grupo dentro de su vigencia
        /// (por DiaDeSesion), como las cuenta la asistencia. Es lo que decide si
        /// una clase "tiene historia": con ella no se desactiva ni se borra, solo
        /// se termina.
        /// </summary>
        public static string SesionDeLaClase(string s = "s", string c = "c")
        {
            return "(" + s + ".horarios_idhorarios IN (SELECT hh.idhorarios FROM horarios hh WHERE hh.clases_idclases = " + c + ".idclases)"
                + " OR " + s + ".horario_excepciones_id IN (SELECT xx.idhorario_excepciones FROM horario_excepciones xx WHERE xx.clases_idclases = " + c + ".idclases)"
                + " OR (" + s + ".horarios_idhorarios IS NULL AND " + s + ".horario_excepciones_id IS NULL"
                + " AND " + s + ".usuarios_idusuarios = " + c + ".usuarios_idusuarios AND " + s + ".materias_id_materia = " + c + ".materias_id_materia"
                + " AND " + s + ".grupos_idgrupos = " + c + ".grupos_idgrupos AND " + DiaDeSesion(s) + " BETWEEN " + c + ".vigente_desde AND " + c + ".vigente_hasta))";
        }

        /// <summary>
        /// Cuantas sesiones tiene la clase y la primera y la ultima fecha (con
        /// SesionDeLaClase y DiaDeSesion). Para las guardas: con sesiones no se
        /// desactiva ni se borra, y su vigencia no puede dejarlas fuera.
        /// </summary>
        public static SesionesDeClase Sesiones(MySqlConnection conexion, int idClase, MySqlTransaction transaccion = null)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*), MIN(" + DiaDeSesion() + "), MAX(" + DiaDeSesion() + ") FROM clases c JOIN codigos_accesos s ON " + SesionDeLaClase()
                + " WHERE c.idclases = @Clase", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Clase", idClase);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    SesionesDeClase resultado = new SesionesDeClase();
                    if (lector.Read())
                    {
                        resultado.Cuantas = Convert.ToInt32(lector.GetValue(0));
                        resultado.Primera = lector.IsDBNull(1) ? (DateTime?)null : lector.GetDateTime(1).Date;
                        resultado.Ultima = lector.IsDBNull(2) ? (DateTime?)null : lector.GetDateTime(2).Date;
                    }

                    return resultado;
                }
            }
        }

        /// <summary>En memoria: la franja se da en esa fecha con la vigencia de su clase y la suya propia (null = la de la clase).</summary>
        public static bool SeDaEn(DateTime fecha, bool claseActiva, DateTime claseDesde, DateTime claseHasta, DateTime? franjaDesde, DateTime? franjaHasta)
        {
            DateTime dia = fecha.Date;
            return claseActiva
                && dia >= claseDesde.Date && dia <= claseHasta.Date
                && (!franjaDesde.HasValue || dia >= franjaDesde.Value.Date)
                && (!franjaHasta.HasValue || dia <= franjaHasta.Value.Date);
        }

        /// <summary>
        /// True si los cambios de una franja con historia la parten (terminar la
        /// vieja y crear la nueva desde una fecha): db/20 aplicada y encendido
        /// en Ajustes. Si no, editar y borrar hacen lo de antes (con aviso).
        /// </summary>
        /// <remarks>
        /// Desde 2026-10-31 SIEMPRE con db/20 (pedido del usuario: "incluso si hay
        /// faltas se deben guardar el historial y estadisticas"): un cambio nunca
        /// reescribe las semanas pasadas. Ya no hay casilla en Ajustes; solo un '0'
        /// escrito a mano en configuracion lo apaga (las pruebas lo usan).
        /// </remarks>
        public static bool ConFecha(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            if (!Esquema.VigenciaDeFranjas(conexion, transaccion))
            {
                return false;
            }

            if (!Esquema.Ajustes(conexion, transaccion))
            {
                return true;
            }

            using (MySqlCommand comando = new MySqlCommand("SELECT valor FROM configuracion WHERE clave = @Clave", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Clave", ClaveConFecha);
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value || Convert.ToString(valor).Trim() != "0";
            }
        }
    }
}
