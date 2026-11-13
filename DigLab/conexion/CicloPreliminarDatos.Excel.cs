using System;
using System.Collections.Generic;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// Lo registrado en DigLab con lo que se lee un horario de Excel (HorarioEnExcel):
    /// solo esto vale en sus casillas. Pedido del 2026-11-12: "en base a los
    /// maestros, clases y grupos registrados".
    /// </summary>
    internal sealed class CatalogosDeHorario
    {
        internal sealed class Docente
        {
            public int Id;
            public string Nombre = "";
            public string ApellidoPaterno = "";
            public string ApellidoMaterno = "";
            /// <summary>El numero con el que entra (numero_identificador).</summary>
            public string Numero = "";
            public bool Activo = true;

            /// <summary>"Laura Cárdenas Ruiz": nombre y apellidos.</summary>
            public string Completo
            {
                get { return string.Join(" ", new[] { Nombre, ApellidoPaterno, ApellidoMaterno }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())); }
            }
        }

        internal sealed class Grupo
        {
            public int Id;
            public string Nombre = "";
            public int? CarreraId;
            public string Carrera = "";
        }

        internal sealed class Materia
        {
            public int Id;
            public string Nombre = "";
            public int? CarreraId;
        }

        internal sealed class Laboratorio
        {
            public int Id;
            public string Nombre = "";
            public bool Activo = true;
        }

        /// <summary>Una clase registrada: docente, grupo y materia (sin horas).</summary>
        internal sealed class Clase
        {
            public int ProfesorId;
            public int GrupoId;
            public int MateriaId;
        }

        public List<Docente> Docentes = new List<Docente>();
        public List<Grupo> Grupos = new List<Grupo>();
        public List<Materia> Materias = new List<Materia>();
        public List<Laboratorio> Laboratorios = new List<Laboratorio>();
        /// <summary>Las clases de Horarios que siguen vigentes (o empiezan despues): para la lista desplegable del Excel.</summary>
        public List<Clase> Clases = new List<Clase>();
    }

    /// <summary>Una clase de un horario de Excel ya entendida: docente (null: "Sin docente"), grupo, materia y sus horas.</summary>
    internal sealed class ClaseDelExcel
    {
        public int? ProfesorId;
        public int GrupoId;
        public int MateriaId;
        /// <summary>"Base de datos con 5ISC11 (Laura Cárdenas)": para los avisos.</summary>
        public string Nombre = "";
        public List<FranjaCiclo> Franjas = new List<FranjaCiclo>();
    }

    /// <summary>Lo que hizo CargarDesdeExcel.</summary>
    internal sealed class ResultadoCargaExcel
    {
        /// <summary>Clases que no estaban en el horario.</summary>
        public int Nuevas;
        /// <summary>Clases que ya estaban (mismo docente, grupo y materia): se les pusieron las horas del Excel.</summary>
        public int YaEstaban;
        /// <summary>Clases que solo tenian horas en esos laboratorios y no venian en el Excel: se quitaron.</summary>
        public int Quitadas;
        public int Franjas;
    }

    internal partial class CicloPreliminarDatos
    {
        /// <summary>Docentes (con los de baja, para decir que lo estan), grupos, materias y laboratorios.</summary>
        public CatalogosDeHorario LeerCatalogosDeHorario()
        {
            CatalogosDeHorario catalogos = new CatalogosDeHorario();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                bool bajas = Esquema.TieneBajas(conexion);
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT idusuarios, nombre, apellido_paterno, apellido_materno, numero_identificador" + (bajas ? ", activo" : ", 1 AS activo")
                    + " FROM usuarios WHERE fk_tipo_usuario = 2 ORDER BY apellido_paterno, apellido_materno, nombre", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        catalogos.Docentes.Add(new CatalogosDeHorario.Docente
                        {
                            Id = reader.GetInt32("idusuarios"),
                            Nombre = Texto(reader, "nombre"),
                            ApellidoPaterno = Texto(reader, "apellido_paterno"),
                            ApellidoMaterno = Texto(reader, "apellido_materno"),
                            Numero = Texto(reader, "numero_identificador"),
                            Activo = Convert.ToInt32(reader["activo"]) != 0
                        });
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT g.idgrupos, g.nombre_grupo, g.Carrera_id, c.nombre_carrera
                    FROM grupos g LEFT JOIN carreras c ON c.idcarreras = g.Carrera_id
                    ORDER BY g.nombre_grupo", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        catalogos.Grupos.Add(new CatalogosDeHorario.Grupo
                        {
                            Id = reader.GetInt32("idgrupos"),
                            Nombre = Texto(reader, "nombre_grupo"),
                            CarreraId = Entero(reader, "Carrera_id"),
                            Carrera = Texto(reader, "nombre_carrera")
                        });
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT id_materia, nombre_materia, idcarreras FROM materias ORDER BY nombre_materia, id_materia", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        catalogos.Materias.Add(new CatalogosDeHorario.Materia
                        {
                            Id = reader.GetInt32("id_materia"),
                            Nombre = Texto(reader, "nombre_materia"),
                            CarreraId = Entero(reader, "idcarreras")
                        });
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT DISTINCT usuarios_idusuarios, grupos_idgrupos, materias_id_materia
                    FROM clases WHERE activa = 1 AND vigente_hasta >= CURDATE()", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        catalogos.Clases.Add(new CatalogosDeHorario.Clase
                        {
                            ProfesorId = reader.GetInt32(0),
                            GrupoId = reader.GetInt32(1),
                            MateriaId = reader.GetInt32(2)
                        });
                    }
                }

                bool desactivables = Esquema.LaboratoriosDesactivables(conexion);
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT idlaboratorios, nombre_laboratorio" + (desactivables ? ", activo" : ", 1 AS activo")
                    + " FROM laboratorios ORDER BY nombre_laboratorio", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        catalogos.Laboratorios.Add(new CatalogosDeHorario.Laboratorio
                        {
                            Id = Convert.ToInt32(reader["idlaboratorios"]),
                            Nombre = Texto(reader, "nombre_laboratorio"),
                            Activo = Convert.ToInt32(reader["activo"]) != 0
                        });
                    }
                }
            }

            return catalogos;
        }

        /// <summary>
        /// "Cargar un Excel": lo que el horario en preparacion tiene en esos laboratorios
        /// se reemplaza por las clases del Excel, todo o nada (una transaccion). Una clase
        /// que ya estaba (mismo docente, grupo y materia) conserva sus horas de los otros
        /// laboratorios y gana las del Excel; las que solo tenian horas en esos
        /// laboratorios y no vienen en el Excel se quitan (con lo que les faltaba asignar).
        /// Los demas laboratorios no cambian. HorarioEnExcel ya reviso todo; aqui se
        /// vuelve a mirar lo que la base pudo cambiar mientras tanto.
        /// </summary>
        public ResultadoCargaExcel CargarDesdeExcel(int idCiclo, ICollection<int> laboratorios, IList<ClaseDelExcel> clases, int idAdministrador)
        {
            Permisos.Exigir("Cargar un Excel en el horario en preparacion");
            if (laboratorios == null || laboratorios.Count == 0)
            {
                throw new ErrorDeDatos("El Excel no tiene ningún laboratorio.");
            }

            clases = clases ?? new List<ClaseDelExcel>();
            HashSet<int> labs = new HashSet<int>(laboratorios);
            foreach (ClaseDelExcel clase in clases)
            {
                if (clase.Franjas.Any(f => !labs.Contains(f.LaboratorioId)))
                {
                    throw new ErrorDeDatos(clase.Nombre + ": tiene horas en un laboratorio que no viene en el Excel.");
                }

                Validar(clase.Nombre, clase.Franjas);
            }

            ResultadoCargaExcel resultado = new ResultadoCargaExcel();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idCiclo, EstadoConfiguracion.EnPreparacion);
                ExigirCatalogosDelExcel(conexion, transaccion, labs, clases);

                // Lo que tiene ahora: cada clase con sus horas.
                Dictionary<int, CargaCiclo> cargas = new Dictionary<int, CargaCiclo>();
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT cc.idcarga, cc.profesor, cc.grupo, cc.materia, f.laboratorio, f.dia_semana, f.hora_inicio, f.hora_fin
                    FROM ciclo_cargas cc LEFT JOIN ciclo_franjas f ON f.idcarga = cc.idcarga
                    WHERE cc.idciclo = @C AND cc.quitada = 0
                    ORDER BY cc.idcarga
                    FOR UPDATE", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int id = reader.GetInt32("idcarga");
                            CargaCiclo carga;
                            if (!cargas.TryGetValue(id, out carga))
                            {
                                carga = new CargaCiclo
                                {
                                    Id = id,
                                    ProfesorId = Entero(reader, "profesor"),
                                    GrupoId = reader.GetInt32("grupo"),
                                    MateriaId = reader.GetInt32("materia")
                                };
                                cargas[id] = carga;
                            }

                            int? laboratorio = Entero(reader, "laboratorio");
                            if (laboratorio.HasValue)
                            {
                                carga.Franjas.Add(new FranjaCiclo
                                {
                                    CargaId = id,
                                    LaboratorioId = laboratorio.Value,
                                    DiaSemana = Convert.ToInt32(reader["dia_semana"]),
                                    Inicio = reader.GetTimeSpan("hora_inicio"),
                                    Fin = reader.GetTimeSpan("hora_fin")
                                });
                            }
                        }
                    }
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.CommandText = @"
                        DELETE f FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                        WHERE cc.idciclo = @C AND cc.quitada = 0 AND f.laboratorio IN (" + Lista(comando, "l", labs) + ")";
                    comando.ExecuteNonQuery();
                }

                HashSet<int> usadas = new HashSet<int>();
                foreach (ClaseDelExcel clase in clases)
                {
                    // La que ya estaba: la misma persona docente (o sin docente), grupo y materia.
                    CargaCiclo ya = cargas.Values
                        .Where(c => !usadas.Contains(c.Id) && c.ProfesorId == clase.ProfesorId && c.GrupoId == clase.GrupoId && c.MateriaId == clase.MateriaId)
                        .OrderByDescending(c => c.Franjas.Any(f => labs.Contains(f.LaboratorioId)))
                        .ThenBy(c => c.Id)
                        .FirstOrDefault();
                    if (ya != null)
                    {
                        usadas.Add(ya.Id);
                        List<FranjaCiclo> quedan = ya.Franjas.Where(f => !labs.Contains(f.LaboratorioId)).ToList();
                        Validar(clase.Nombre, quedan.Concat(clase.Franjas).ToList());
                        InsertarFranjas(conexion, transaccion, ya.Id, clase.Franjas);
                        // Si gano tiempo, lo que le faltaba asignar baja (como al ponerle horas).
                        AsignarPorAsignar(conexion, transaccion, idCiclo, ya.Id, MinutosDe(quedan) + MinutosDe(clase.Franjas) - MinutosDe(ya.Franjas));
                        resultado.YaEstaban++;
                    }
                    else
                    {
                        int idCarga;
                        using (MySqlCommand comando = new MySqlCommand(@"
                            INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada) VALUES (@C, @P, @G, @M, 0);
                            SELECT LAST_INSERT_ID();", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@C", idCiclo);
                            comando.Parameters.AddWithValue("@P", clase.ProfesorId.HasValue ? (object)clase.ProfesorId.Value : DBNull.Value);
                            comando.Parameters.AddWithValue("@G", clase.GrupoId);
                            comando.Parameters.AddWithValue("@M", clase.MateriaId);
                            idCarga = Convert.ToInt32(comando.ExecuteScalar());
                        }

                        InsertarFranjas(conexion, transaccion, idCarga, clase.Franjas);
                        resultado.Nuevas++;
                    }

                    resultado.Franjas += clase.Franjas.Count;
                }

                // Las que solo tenian horas en esos laboratorios y no vienen en el Excel: fuera
                // (con lo que les faltaba asignar). Las que ya estaban sin horas se quedan.
                List<int> quitar = cargas.Values
                    .Where(c => !usadas.Contains(c.Id) && c.Franjas.Count > 0 && c.Franjas.All(f => labs.Contains(f.LaboratorioId)))
                    .Select(c => c.Id)
                    .ToList();
                if (quitar.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@C", idCiclo);
                        comando.Parameters.AddWithValue("@PorAsignar", QuitadaPorAsignar);
                        string ids = Lista(comando, "k", quitar);
                        comando.CommandText = "DELETE FROM ciclo_cargas WHERE idciclo = @C AND quitada = @PorAsignar AND carga_origen IN (" + ids + ")";
                        comando.ExecuteNonQuery();
                        comando.CommandText = "DELETE FROM ciclo_cargas WHERE idciclo = @C AND quitada <> @PorAsignar AND idcarga IN (" + ids + ")";
                        resultado.Quitadas = comando.ExecuteNonQuery();
                    }
                }

                Tocar(conexion, transaccion, idCiclo, idAdministrador);
                transaccion.Commit();
                Log.Info("Horario en preparacion " + idCiclo + ": Excel cargado en los laboratorios " + string.Join(",", labs.OrderBy(l => l)) + " ("
                    + resultado.Nuevas + " clases nuevas, " + resultado.YaEstaban + " que ya estaban, " + resultado.Quitadas + " quitadas, "
                    + resultado.Franjas + " horarios).");
                return resultado;
            }
        }

        /// <summary>ValidarFranjas, con el nombre de la clase en el mensaje.</summary>
        private static void Validar(string clase, IList<FranjaCiclo> franjas)
        {
            try
            {
                ValidarFranjas(franjas);
            }
            catch (ErrorDeDatos ex)
            {
                throw new ErrorDeDatos(clase + ": " + ex.Message);
            }
        }

        /// <summary>Que sigan en DigLab los docentes, grupos, materias y laboratorios del Excel (de una vez).</summary>
        private static void ExigirCatalogosDelExcel(MySqlConnection conexion, MySqlTransaction transaccion, ICollection<int> laboratorios, IList<ClaseDelExcel> clases)
        {
            Comprobar(conexion, transaccion, "SELECT idusuarios FROM usuarios WHERE idusuarios IN ",
                clases.Where(c => c.ProfesorId.HasValue).Select(c => c.ProfesorId.Value), "Una de las personas docentes del Excel ya no está en DigLab");
            Comprobar(conexion, transaccion, "SELECT idgrupos FROM grupos WHERE idgrupos IN ",
                clases.Select(c => c.GrupoId), "Uno de los grupos del Excel ya no está en DigLab");
            Comprobar(conexion, transaccion, "SELECT id_materia FROM materias WHERE id_materia IN ",
                clases.Select(c => c.MateriaId), "Una de las materias del Excel ya no está en DigLab");
            Comprobar(conexion, transaccion, "SELECT idlaboratorios FROM laboratorios WHERE idlaboratorios IN ",
                laboratorios, "Uno de los laboratorios del Excel ya no está en DigLab");
        }

        private static void Comprobar(MySqlConnection conexion, MySqlTransaction transaccion, string consulta, IEnumerable<int> ids, string siFalta)
        {
            HashSet<int> buscados = new HashSet<int>(ids);
            if (buscados.Count == 0)
            {
                return;
            }

            HashSet<int> hay = new HashSet<int>();
            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.CommandText = consulta + "(" + Lista(comando, "x", buscados) + ")";
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        hay.Add(Convert.ToInt32(reader.GetValue(0)));
                    }
                }
            }

            if (buscados.Any(id => !hay.Contains(id)))
            {
                throw new ErrorDeDatos(siFalta + ": vuelve a cargarlo para ver cuál.");
            }
        }
    }
}
