using DigBit.Infraestructura;
using Google.Protobuf;
using MySql.Data.MySqlClient;
using MySqlX.XDevAPI.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DigBit.conexion
{
    internal partial class Consultas
    {
        private Conexion mconexion;

        public Consultas()
        {
            mconexion = new Conexion();
        }
        public bool RealizarInicioSesion(string numeroIdentificador, string contraseña)
        {
            mconexion = new Conexion();
            try
            {
                string almacenada = null;
                int tipoUsuarioId = 0;
                bool activo = true;

                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    if (conexion == null)
                    {
                        MessageBox.Show("Error al conectar a la base de datos", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    string columnaActivo = Esquema.TieneBajas(conexion) ? "activo" : "1 AS activo";
                    using (MySqlCommand consulta = new MySqlCommand(
                        "SELECT fk_tipo_usuario, password, " + columnaActivo + " FROM usuarios WHERE numero_identificador = @NumeroIdentificador", conexion))
                    {
                        consulta.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador);

                        // El lector se cierra aqui a proposito: la migracion del hash
                        // manda un UPDATE por esta misma conexion, y no se puede con
                        // un lector abierto.
                        using (MySqlDataReader lector = consulta.ExecuteReader())
                        {
                            if (lector.Read())
                            {
                                tipoUsuarioId = (int)Convert.ToInt64(lector["fk_tipo_usuario"]);
                                almacenada = lector[1] == DBNull.Value ? null : lector[1].ToString();
                                activo = Convert.ToInt32(lector["activo"]) == 1;
                            }
                        }
                    }

                    bool necesitaRehash;
                    if (almacenada == null || !Contrasenas.Verificar(contraseña, almacenada, out necesitaRehash))
                    {
                        // El mismo mensaje exista o no el usuario: decir "ese numero no
                        // existe" le regala a quien lo intente la lista de matriculas
                        // validas.
                        MessageBox.Show("Número de identificador o contraseña incorrectos. Por favor, verifica tus credenciales.", "Error de inicio de sesión", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    // Solo despues de comprobar la contrasena: quien la sabe ya sabe
                    // que su cuenta existe, y asi no se delata ninguna matricula.
                    if (!activo)
                    {
                        Log.Info("Inicio de sesion rechazado: '" + numeroIdentificador + "' esta dado de baja.");
                        MessageBox.Show("Tu cuenta está dada de baja. Si crees que es un error, acude con el encargado del laboratorio.", "Cuenta dada de baja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }

                    if (necesitaRehash)
                    {
                        MigrarContrasena(conexion, numeroIdentificador, contraseña, Contrasenas.EsFormatoAntiguo(almacenada));
                    }
                }

                AbrirPestanaSegunTipoUsuario(tipoUsuarioId, numeroIdentificador);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al intentar realizar la consulta: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }

        /// <summary>
        /// Migracion perezosa a PBKDF2. No se pueden recuperar las contrasenas
        /// guardadas como MD5 para volver a procesarlas, asi que se aprovecha el
        /// unico instante en que la aplicacion tiene la contrasena en claro: un
        /// inicio de sesion correcto. Cuando en la base no queden hashes antiguos
        /// se puede quitar el camino de MD5 de Contrasenas.
        ///
        /// Sirve tambien para subir las iteraciones mas adelante sin tocar a nadie.
        /// </summary>
        private static void MigrarContrasena(MySqlConnection conexion, string numeroIdentificador, string contraseña, bool desdeMd5)
        {
            try
            {
                using (MySqlCommand actualizar = new MySqlCommand(
                    "UPDATE usuarios SET password = @Password WHERE numero_identificador = @NumeroIdentificador", conexion))
                {
                    actualizar.Parameters.AddWithValue("@Password", Contrasenas.Hash(contraseña));
                    actualizar.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador);
                    actualizar.ExecuteNonQuery();
                }

                Log.Info("Contrasena de '" + numeroIdentificador + "' vuelta a guardar con PBKDF2"
                    + (desdeMd5 ? " (venia de MD5)." : " (mas iteraciones)."));
            }
            catch (Exception ex)
            {
                // Que falle la migracion no debe impedir entrar: la contrasena ya se
                // comprobo bien. Se reintentara en el proximo inicio de sesion.
                Log.Error("Migrar a PBKDF2 la contrasena de '" + numeroIdentificador + "'", ex);
            }
        }



        public void AbrirPestanaSegunTipoUsuario(int tipoUsuarioId, String numeroIdentificador)
        {
            // Queda en memoria para que cada pantalla sepa a quien atiende (por
            // ejemplo, el alta de profesores solo se abre para el administrador).
            Datos_User.TipoUsuario = tipoUsuarioId;

            switch (tipoUsuarioId)
            {
                case 1:
                    Datos_User.SetUser(numeroIdentificador);
                    Alumno_Principal registroAlumnos = new Alumno_Principal();
                    registroAlumnos.Show();
                    ocultarVentana();
                    break;

                case 2:
                    Datos_User.SetUser(numeroIdentificador);
                    Profesor_Principal profPrincipal = new Profesor_Principal();
                    profPrincipal.Show();
                    ocultarVentana();
                    break;

                case 3:
                    // Administrador real (fase 6): antes era ADMINISTRADOR/123 en Login.cs.
                    Datos_User.SetUser(numeroIdentificador);
                    PrincipalAdministrador principalAdministrador = new PrincipalAdministrador();
                    principalAdministrador.Show();
                    ocultarVentana();
                    break;

                default:
                    MessageBox.Show($"Tipo de usuario no manejado: {tipoUsuarioId}");
                    break;
            }
        }

        public void ocultarVentana()
        {
            Form ventanaActiva = Form.ActiveForm;

        }

        public UsuarioConInformacionAdicional ConsultaEditar(string numeroIdentificador)
        {
            UsuarioConInformacionAdicional usuarioConInfoAdicional = null;
            mconexion = new Conexion();
            try
            {
                // Conectar a la base de datos
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    if (conexion != null)
                    {
                        // Consulta SQL para obtener la información del usuario con información adicional
                        string consulta = @"SELECT
                    u.idusuarios,
                    u.fk_tipo_usuario,
                    u.nombre,
                    u.apellido_paterno,
                    u.apellido_materno,
                    u.correo,
                    carreras.nombre_carrera AS NombreCarrera,
                    semestres.numero_semestre AS NombreSemestre,
                    grupos.nombre_grupo AS NombreGrupo
                FROM teschi_otru.usuarios AS u
                LEFT JOIN teschi_otru.carrera_grupo_semestre AS cgs ON u.idusuarios = cgs.usuarios_numero_identificador
                LEFT JOIN teschi_otru.carreras AS carreras ON cgs.carreras_idcarreras = carreras.idcarreras
                LEFT JOIN teschi_otru.semestres AS semestres ON cgs.semestres_idsemestres = semestres.idsemestres
                LEFT JOIN teschi_otru.grupos AS grupos ON cgs.grupos_idgrupos = grupos.idgrupos
                WHERE u.numero_identificador = @NumeroIdentificador";

                        using (MySqlCommand mysqlcomand = new MySqlCommand(consulta, conexion))
                        {
                            mysqlcomand.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador);

                            using (MySqlDataReader mySqldatareader = mysqlcomand.ExecuteReader())
                            {
                                if (mySqldatareader.Read())
                                {
                                    // Aquí puedes procesar los resultados como lo necesites
                                    usuarioConInfoAdicional = new UsuarioConInformacionAdicional
                                    {
                                        id = mySqldatareader["idusuarios"].ToString(),
                                        TipoUsuarioId = Convert.ToInt32(mySqldatareader["fk_tipo_usuario"]),
                                        Nombre = mySqldatareader["nombre"].ToString(),
                                        ApellidoPaterno = mySqldatareader["apellido_paterno"].ToString(),
                                        ApellidoMaterno = mySqldatareader["apellido_materno"].ToString(),
                                        Correo = mySqldatareader["correo"].ToString(),
                                        NombreCarrera = mySqldatareader["NombreCarrera"].ToString(),
                                        NombreSemestre = mySqldatareader["NombreSemestre"].ToString(),
                                        NombreGrupo = mySqldatareader["NombreGrupo"].ToString(),
                                        // Asigna otros campos si es necesario
                                    };

                                }
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Error al conectar a la base de datos", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al intentar realizar la consulta: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return usuarioConInfoAdicional;
        }


        private int NombreCarrera(string nombreCarrera)
        {
            int idCarrera = -1; // Valor predeterminado en caso de que no se encuentre la carrera

            try
            {
                // Realizar la consulta para obtener el ID de la carrera a partir de su nombre
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT idcarreras FROM carreras WHERE nombre_carrera = @NombreCarrera";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NombreCarrera", nombreCarrera);
                        var resultado = comando.ExecuteScalar();
                        if (resultado != null)
                        {
                            idCarrera = Convert.ToInt32(resultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener el ID de la carrera: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return idCarrera;
        }

        public List<OpcionCombo> ObtenerCarreras()
        {
            List<OpcionCombo> carreras = new List<OpcionCombo>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT idcarreras, nombre_carrera FROM carreras ORDER BY nombre_carrera";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            carreras.Add(new OpcionCombo
                            {
                                Id = Convert.ToInt32(reader["idcarreras"]),
                                Texto = reader["nombre_carrera"].ToString()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener las carreras: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return carreras;
        }

        public List<string> ObtenerNombresGrupos()
        {
            List<string> nombreGrupos = new List<string>();

            try
            {
                foreach (OpcionCombo grupo in ObtenerGrupos())
                {
                    nombreGrupos.Add(grupo.Texto);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los nombres de los grupos: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return nombreGrupos;
        }

        public List<OpcionCombo> ObtenerGrupos()
        {
            List<OpcionCombo> grupos = new List<OpcionCombo>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT idgrupos, nombre_grupo FROM grupos ORDER BY nombre_grupo";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            grupos.Add(new OpcionCombo
                            {
                                Id = Convert.ToInt32(reader["idgrupos"]),
                                Texto = reader["nombre_grupo"].ToString()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los grupos: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return grupos;
        }

        public List<OpcionCombo> ObtenerGruposPorCarrera(int idCarrera)
        {
            List<OpcionCombo> grupos = new List<OpcionCombo>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT idgrupos, nombre_grupo FROM grupos WHERE Carrera_id = @IdCarrera ORDER BY nombre_grupo";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@IdCarrera", idCarrera);
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                grupos.Add(new OpcionCombo
                                {
                                    Id = Convert.ToInt32(reader["idgrupos"]),
                                    Texto = reader["nombre_grupo"].ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los grupos de la carrera: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return grupos;
        }


        public List<string> obtenerNombreSemestres()
        {
            List<string> nombreSemestre = new List<string>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT numero_semestre FROM semestres"; // Ajusta esto según el nombre de tu tabla
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                nombreSemestre.Add(reader["numero_semestre"].ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los nombres de los grupos: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return nombreSemestre;
        }

       public List<string> ObtenerNombresSemestre()
        {
            List<string> nombreSemestre = new List<string>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT numero_semestre FROM semestres"; // Ajusta esto según el nombre de tu tabla
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                nombreSemestre.Add(reader["numero_semestre"].ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los nombres de los semestres: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return nombreSemestre;
        }

        public List<string> ObtenerNombresLaboratorios()
        {
            List<string> nombresLaboratorios = new List<string>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT nombre_laboratorio FROM laboratorios"; // Ajusta esto según el nombre de tu tabla
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                nombresLaboratorios.Add(reader["nombre_laboratorio"].ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los nombres de las carreras: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return nombresLaboratorios;
        }

        /// <summary>
        /// Reloj del servidor. generarCodigo valida la hora de salida contra el, que
        /// es la misma referencia con la que despues se validara al alumno.
        /// </summary>
        public DateTime AhoraServidor()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
            {
                return Convert.ToDateTime(comando.ExecuteScalar());
            }
        }

        // Minutos antes de la hora de entrada en que ya se acepta el codigo: los
        // alumnos llegan y encienden el equipo antes de que empiece la clase.
        // Despues de la hora de salida no se acepta nunca.
        private const int MargenAntesDelInicioMinutos = 15;

        /// <summary>
        /// Ventana de validez de un codigo calculada con el reloj del SERVIDOR
        /// (NOW()), no con el del equipo: cambiar la hora de la PC no sirve de nada.
        /// Devuelve null si el codigo no existe. Lanza si la consulta falla o si el
        /// codigo no tiene fecha y horas validas.
        /// </summary>
        public VentanaCodigo ObtenerVentanaCodigo(string codigo)
        {
            const string consulta = @"SELECT idcodigos_accesos,
                                             codigo,
                                             TIMESTAMP(fecha, hora_entrada) AS inicio,
                                             TIMESTAMP(fecha, hora_salida)  AS fin,
                                             NOW() AS ahora
                                      FROM codigos_accesos
                                      WHERE codigo = @Codigo
                                      ORDER BY fecha DESC, hora_entrada DESC, idcodigos_accesos DESC
                                      LIMIT 1";

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    if (reader.IsDBNull(reader.GetOrdinal("inicio")) || reader.IsDBNull(reader.GetOrdinal("fin")))
                    {
                        throw new InvalidOperationException("El codigo '" + codigo + "' no tiene fecha u horas de entrada y salida.");
                    }

                    VentanaCodigo ventana = new VentanaCodigo
                    {
                        IdCodigo = reader.GetInt32("idcodigos_accesos"),
                        Codigo = reader.GetString("codigo"),
                        Inicio = reader.GetDateTime("inicio"),
                        Fin = reader.GetDateTime("fin"),
                        AhoraServidor = reader.GetDateTime("ahora"),
                        LeidoEnRelojLocal = DateTime.Now
                    };
                    ventana.Estado = CalcularEstado(ventana);
                    return ventana;
                }
            }
        }

        internal static EstadoCodigo CalcularEstado(VentanaCodigo ventana)
        {
            if (ventana.AhoraServidor < ventana.Inicio.AddMinutes(-MargenAntesDelInicioMinutos))
            {
                return EstadoCodigo.AunNoEmpieza;
            }

            if (ventana.AhoraServidor > ventana.Fin)
            {
                return EstadoCodigo.Expirado;
            }

            return EstadoCodigo.Vigente;
        }

        /// <summary>
        /// Lo que necesita la pantalla del alumno: el codigo existe, esta dentro de
        /// su ventana y este alumno todavia no lo ha usado. Sustituye a
        /// buscarCodigoRegistro, que comparaba con el reloj local con un umbral de
        /// 15 anos y comprobaba el "ya registrado" contra la tabla de usuarios con
        /// un id fijo.
        /// </summary>
        public VentanaCodigo ValidarCodigoParaAlumno(string codigo, int idUsuario)
        {
            VentanaCodigo ventana = ObtenerVentanaCodigo(codigo);
            if (ventana != null && ventana.Estado == EstadoCodigo.Vigente && AlumnoYaRegistro(ventana.IdCodigo, idUsuario))
            {
                ventana.Estado = EstadoCodigo.YaRegistrado;
            }

            return ventana;
        }

        internal bool AlumnoYaRegistro(int idCodigo, int idUsuario)
        {
            const string consulta = "SELECT COUNT(*) FROM registros_bitacoras WHERE fk_codigo_accesos = @IdCodigo AND fk_usuario = @IdUsuario";
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                comando.Parameters.AddWithValue("@IdCodigo", idCodigo);
                comando.Parameters.AddWithValue("@IdUsuario", idUsuario);
                return Convert.ToInt32(comando.ExecuteScalar()) > 0;
            }
        }


        public int ObtenerIdSemestre(string nombreSemestre)
        {
            int idSemestre = -1;
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT idsemestres FROM semestres WHERE numero_semestre = @NombreSemestre";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NombreSemestre", nombreSemestre);
                        var resultado = comando.ExecuteScalar();
                        if (resultado != null)
                        {
                            idSemestre = Convert.ToInt32(resultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener el ID del semestre: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return idSemestre;
        }
         public int ObtenerIdGrupo(string nombreGrupo)
        {
            int idGrupo = -1;
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT idgrupos FROM grupos WHERE nombre_grupo = @NombreGrupo";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NombreGrupo", nombreGrupo);
                        var resultado = comando.ExecuteScalar();
                        if (resultado != null)
                        {
                            idGrupo = Convert.ToInt32(resultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener el ID del grupo: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return idGrupo;
        }
         public int ObtenerIdCarrera(string nombreCarrera)
        {
            int idCarrera = -1;
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT idcarreras FROM carreras WHERE nombre_carrera = @NombreCarrera";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NombreCarrera", nombreCarrera);
                        var resultado = comando.ExecuteScalar();
                        if (resultado != null)
                        {
                            idCarrera = Convert.ToInt32(resultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener el ID de la carrera: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return idCarrera;
     
        }
        public List<string> ObtenerNombresMaterias()
        {
            List<string> nombresMaterias = new List<string>();

            try
            {
                foreach (OpcionCombo materia in ObtenerMaterias())
                {
                    nombresMaterias.Add(materia.Texto);
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error al obtener los nombres de las materia: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return nombresMaterias;
        }

        public List<OpcionCombo> ObtenerMaterias()
        {
            List<OpcionCombo> materias = new List<OpcionCombo>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT id_materia, nombre_materia FROM materias ORDER BY nombre_materia";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            materias.Add(new OpcionCombo
                            {
                                Id = Convert.ToInt32(reader["id_materia"]),
                                Texto = reader["nombre_materia"].ToString()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener las materias: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return materias;
        }

        public List<OpcionCombo> ObtenerMateriasPorCarrera(int idCarrera)
        {
            List<OpcionCombo> materias = new List<OpcionCombo>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT id_materia, nombre_materia FROM materias WHERE idcarreras = @IdCarrera ORDER BY nombre_materia";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@IdCarrera", idCarrera);
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                materias.Add(new OpcionCombo
                                {
                                    Id = Convert.ToInt32(reader["id_materia"]),
                                    Texto = reader["nombre_materia"].ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener las materias de la carrera: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return materias;
        }

         public List<string> ObtenerNombreLaboratorio()
        {
            List<string> nombresLaboratorio = new List<string>();

            try
            {
                foreach (OpcionCombo laboratorio in ObtenerLaboratorios())
                {
                    nombresLaboratorio.Add(laboratorio.Texto);
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error al obtener el nombre de los laboratorios: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return nombresLaboratorio;
        }

        public List<OpcionCombo> ObtenerLaboratorios()
        {
            List<OpcionCombo> laboratorios = new List<OpcionCombo>();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT idlaboratorios, nombre_laboratorio FROM laboratorios ORDER BY nombre_laboratorio";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            laboratorios.Add(new OpcionCombo
                            {
                                Id = Convert.ToInt32(reader["idlaboratorios"]),
                                Texto = reader["nombre_laboratorio"].ToString()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los laboratorios: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return laboratorios;
        }

        public int ObtenerIdMateria(string nombreMateria)
        {
            int idMateria = -1;
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT id_materia FROM materias WHERE nombre_materia = @NombreMateria";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NombreMateria", nombreMateria);
                        object resultado = comando.ExecuteScalar();
                        if (resultado != null)
                        {
                            idMateria = Convert.ToInt32(resultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener el ID de la materia: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return idMateria;
        }

        public int ObtenerIdLaboratorio(string nombreLaboratorio)
        {
            int idLaboratorio = -1;
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = @NombreLaboratorio";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NombreLaboratorio", nombreLaboratorio);
                        object resultado = comando.ExecuteScalar();
                        if (resultado != null)
                        {
                            idLaboratorio = Convert.ToInt32(resultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener el ID del laboratorio: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return idLaboratorio;
        }

        public int ObtenerCarreraIdDelUsuario(string numeroIdentificador)
        {
            int idCarrera = -1;

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    const string consulta = @"SELECT cgs.carreras_idcarreras
                                              FROM usuarios u
                                              INNER JOIN carrera_grupo_semestre cgs ON cgs.usuarios_numero_identificador = u.idusuarios
                                              WHERE u.numero_identificador = @NumeroIdentificador
                                              LIMIT 1";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador);
                        object resultado = comando.ExecuteScalar();
                        if (resultado != null)
                        {
                            idCarrera = Convert.ToInt32(resultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener la carrera del usuario: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return idCarrera;
        }

         public string MostrarNombreProfesor(string numeroIdentificador)
        {
            string nombreProfesor = "No se encontró el profesor.";
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
            

                    string consulta = "SELECT nombre, apellido_paterno, apellido_materno FROM teschi_otru.usuarios WHERE numero_identificador = @NumeroIdentificador";
                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador);
                        
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                // Concatenar los nombres y apellidos
                                nombreProfesor = $"{reader["nombre"]} {reader["apellido_paterno"]} {reader["apellido_materno"]}";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener el nombre del profesor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return nombreProfesor;
        }




        /// <summary>Sin grupo (null), el que tuviera el alumno se conserva: ya no se le pide.</summary>
        public bool ActualizarUsuario(string idUsuarios, string nombre, string apellidoPaterno, string apellidoMaterno, string correo, int idCarrera, int? idGrupo, int idSemestre)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = @"UPDATE usuarios
                                    SET nombre = @Nombre, apellido_paterno = @ApellidoPaterno, apellido_materno = @ApellidoMaterno, correo = @Correo
                                    WHERE idusuarios = @idUsuarios;
                                    UPDATE carrera_grupo_semestre
                                    SET carreras_idcarreras = @IdCarrera, grupos_idgrupos = COALESCE(@IdGrupo, grupos_idgrupos), semestres_idsemestres = @IdSemestre
                                    WHERE usuarios_numero_identificador = @idUsuarios";

                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@idUsuarios", idUsuarios);
                        comando.Parameters.AddWithValue("@Nombre", nombre);
                        comando.Parameters.AddWithValue("@ApellidoPaterno", apellidoPaterno);
                        comando.Parameters.AddWithValue("@ApellidoMaterno", apellidoMaterno);
                        comando.Parameters.AddWithValue("@Correo", correo);
                        comando.Parameters.AddWithValue("@IdCarrera", idCarrera);
                        comando.Parameters.AddWithValue("@IdGrupo", idGrupo.HasValue ? (object)idGrupo.Value : DBNull.Value);
                        comando.Parameters.AddWithValue("@IdSemestre", idSemestre);

                        int filasAfectadas = comando.ExecuteNonQuery();
                        return filasAfectadas > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al actualizar los datos del usuario: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ActualizarUsuarioBasico(string idUsuarios, string nombre, string apellidoPaterno, string apellidoMaterno, string correo)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = @"UPDATE usuarios
                                    SET nombre = @Nombre, apellido_paterno = @ApellidoPaterno, apellido_materno = @ApellidoMaterno, correo = @Correo
                                    WHERE idusuarios = @idUsuarios";

                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@idUsuarios", idUsuarios);
                        comando.Parameters.AddWithValue("@Nombre", nombre);
                        comando.Parameters.AddWithValue("@ApellidoPaterno", apellidoPaterno);
                        comando.Parameters.AddWithValue("@ApellidoMaterno", apellidoMaterno);
                        comando.Parameters.AddWithValue("@Correo", correo);

                        int filasAfectadas = comando.ExecuteNonQuery();
                        return filasAfectadas > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al actualizar los datos del usuario: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
 
// numero_identificador es VARCHAR: un numero de empleado como "EMP001" es valido.
public int ObtenerIdPorMatricula(string matricula)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = "SELECT idusuarios FROM usuarios WHERE numero_identificador = @Matricula";
                    using (MySqlCommand cmd = new MySqlCommand(consulta, conexion))
                    {
                        cmd.Parameters.AddWithValue("@Matricula", matricula);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            return Convert.ToInt32(result);
                        }
                        else
                        {
                            throw new Exception("No se encontró el usuario con la matrícula especificada.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Manejo de excepciones, puedes registrar el error o mostrar un mensaje al usuario
                Console.WriteLine("Error al obtener el ID por matrícula: " + ex.Message);
                throw;
            }
        }
public string ObtenerNombreLaboratorioPorCodigo(string codigoLaboratorio)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    // Primero obtenemos el ID del laboratorio usando el código guardado
                    string consultaId = "SELECT laboratorios_idlaboratorios FROM codigos_accesos WHERE codigo = @Codigo";
                    int idLaboratorio;
                    using (MySqlCommand cmdId = new MySqlCommand(consultaId, conexion))
                    {
                        cmdId.Parameters.AddWithValue("@Codigo", codigoLaboratorio);
                        object resultId = cmdId.ExecuteScalar();
                        if (resultId != null)
                        {
                            idLaboratorio = Convert.ToInt32(resultId);
                        }
                        else
                        {
                            throw new Exception("No se encontró el código especificado en la tabla.");
                        }
                    }

                    // Luego obtenemos el nombre del laboratorio usando el ID obtenido
                    string consultaNombre = "SELECT nombre_laboratorio FROM laboratorios WHERE idLaboratorios = @IdLaboratorio";
                    string nombreLaboratorio;
                    using (MySqlCommand cmdNombre = new MySqlCommand(consultaNombre, conexion))
                    {
                        cmdNombre.Parameters.AddWithValue("@IdLaboratorio", idLaboratorio);
                        object resultNombre = cmdNombre.ExecuteScalar();
                        if (resultNombre != null)
                        {
                            nombreLaboratorio = resultNombre.ToString();
                        }
                        else
                        {
                            throw new Exception("No se encontró el laboratorio con el ID especificado.");
                        }
                    }

                    return nombreLaboratorio;
                }
            }
            catch (Exception ex)
            {
                // Manejo de excepciones, puedes registrar el error o mostrar un mensaje al usuario
                Console.WriteLine("Error al obtener el nombre del laboratorio por código: " + ex.Message);
                throw;
            }
        }
        public Usuario ConsultarDatosPdf(string codigoAcceso)
        {
            try
            {
                using (MySqlConnection con = mconexion.GetConexion())
                {
                    string consulta = @"
                SELECT 
                    ca.hora_entrada,
                    ca.hora_salida,
                    ca.usuarios_idusuarios,
                    g.nombre_grupo AS GrupoNombre,
                    m.nombre_materia AS MateriaNombre,
                    ca.hora_registro
                FROM codigos_accesos ca
                INNER JOIN grupos g ON ca.grupos_idgrupos = g.idgrupos
                INNER JOIN materias m ON ca.materias_id_materia = m.id_materia
                WHERE ca.codigo = @Codigo";

                    using (MySqlCommand cmd = new MySqlCommand(consulta, con))
                    {
                        cmd.Parameters.AddWithValue("@Codigo", codigoAcceso);
                         using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var datosPdf = new Usuario
                                {
                                    HoraEntrada = reader["hora_entrada"].ToString(),
                                    HoraSalida = reader["hora_salida"].ToString(),
                                    GruposId = reader["GrupoNombre"].ToString(), // Nombre del grupo
                                    MateriasId = reader["MateriaNombre"].ToString(), // Nombre de la materia
                                    HoraRegistro = DateTime.Parse(reader["hora_registro"].ToString()).ToString("yyyy-MM-dd")
                                };

                                return datosPdf;
                            }
                            else
                            {
                                return null;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Error al encontrar los datos: {e.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        public DataTable ObtenerCodigosProfesor(string numeroIdentificador)
        {
            DataTable tablaResultado = new DataTable();

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = @"
                SELECT 
                    ca.idcodigos_accesos,
                    ca.codigo,
                    DATE_FORMAT(ca.fecha, '%Y-%m-%d') AS fecha,
                    DATE_FORMAT(ca.hora_registro, '%Y-%m-%d %H:%i:%s') AS fecha_generacion,
                    ca.hora_entrada,
                    ca.hora_salida,
                    COALESCE(m.nombre_materia, '') AS materia,
                    COALESCE(g.nombre_grupo, '') AS grupo,
                    COALESCE(l.nombre_laboratorio, '') AS laboratorio,
                    (SELECT COUNT(*) FROM registros_bitacoras rb WHERE rb.fk_codigo_accesos = ca.idcodigos_accesos) AS alumnos
                FROM codigos_accesos ca
                INNER JOIN usuarios u ON ca.usuarios_idusuarios = u.idusuarios
                LEFT JOIN materias m ON ca.materias_id_materia = m.id_materia
                LEFT JOIN grupos g ON ca.grupos_idgrupos = g.idgrupos
                LEFT JOIN laboratorios l ON ca.laboratorios_idlaboratorios = l.idlaboratorios
                WHERE u.numero_identificador = @NumeroIdentificador
                ORDER BY ca.fecha DESC, ca.hora_entrada DESC, ca.hora_registro DESC";

                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador);
                        using (MySqlDataAdapter adapter = new MySqlDataAdapter(comando))
                        {
                            adapter.Fill(tablaResultado);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al obtener los codigos del profesor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return tablaResultado;
        }



        // ConsultaFecha (la hora de generacion del codigo) se sustituyo por
        // ObtenerVentanaCodigo: lo que importa es la ventana de la clase, no
        // cuando se genero el codigo.

        public DataTable consultaRegistro(string codigo)
        {
            DataTable tablaResultado = new DataTable();

            try
            {
                using (MySqlConnection con = mconexion.GetConexion()) // Asumiendo que mconexion.GetConexion() devuelve la conexión
                {
                    string consultaIdCodigo = "SELECT idcodigos_accesos FROM codigos_accesos WHERE codigo = @Codigo";
                    string idcodigoAccess;

                    using (MySqlCommand cmdCodigo = new MySqlCommand(consultaIdCodigo, con))
                    {
                        cmdCodigo.Parameters.AddWithValue("@Codigo", codigo);
                        object resultCodigo = cmdCodigo.ExecuteScalar();

                        if (resultCodigo != null)
                        {
                            idcodigoAccess = resultCodigo.ToString();
                        }
                        else
                        {
                            throw new Exception("No se encontró el código especificado.");
                        }
                    }

                    // Consulta adicional para obtener los detalles del registro
                    string consultaDetalles = @"
                SELECT rb.fk_usuario, u.nombre, u.apellido_paterno, u.apellido_materno, 
                       rb.numero_computadora, rb.comentarios_red, rb.comentarios_hardware, rb.comentarios_software 
                FROM registros_bitacoras rb
                INNER JOIN usuarios u ON rb.fk_usuario = u.idusuarios
                WHERE rb.fk_codigo_accesos = @IdcodigoAccess";

                    using (MySqlCommand cmdDetalles = new MySqlCommand(consultaDetalles, con))
                    {
                        cmdDetalles.Parameters.AddWithValue("@IdcodigoAccess", idcodigoAccess);
                        MySqlDataAdapter adapter = new MySqlDataAdapter(cmdDetalles);
                        adapter.Fill(tablaResultado); // Llena el DataTable con los resultados de la consulta

                        // Agrega una columna para el nombre completo
                        tablaResultado.Columns.Add("nombre_completo", typeof(string));

                        foreach (DataRow row in tablaResultado.Rows)
                        {
                            string nombreCompleto = $"{row["nombre"]} {row["apellido_paterno"]} {row["apellido_materno"]}";
                            row["nombre_completo"] = nombreCompleto;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Manejo de excepciones, puedes registrar el error o mostrar un mensaje al usuario
                Console.WriteLine("Error al ejecutar la consulta: " + ex.Message);
                throw;
            }

            return tablaResultado;
        }

        public bool ActualizarRegistroBitacora(
            string fkCodigoAcceso,
            string fkUsuario,
            string numeroOriginal,
            string fallaRedOriginal,
            string comentarioRedOriginal,
            string fallaHardwareOriginal,
            string comentarioHardwareOriginal,
            string fallaSoftwareOriginal,
            string comentarioSoftwareOriginal,
            string nuevoNumeroComputadora,
            string nuevaFallaRed,
            string nuevoComentarioRed,
            string nuevaFallaHardware,
            string nuevoComentarioHardware,
            string nuevaFallaSoftware,
            string nuevoComentarioSoftware)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    string consulta = @"
                UPDATE registros_bitacoras
                SET
                    numero_computadora = @NuevoNumeroComputadora,
                    falla_red = @NuevaFallaRed,
                    comentarios_red = @NuevoComentarioRed,
                    falla_hardware = @NuevaFallaHardware,
                    comentarios_hardware = @NuevoComentarioHardware,
                    falla_software = @NuevaFallaSoftware,
                    comentarios_software = @NuevoComentarioSoftware
                WHERE fk_codigo_accesos = @FkCodigoAcceso
                    AND fk_usuario = @FkUsuario
                    AND IFNULL(numero_computadora, '') = @NumeroOriginal
                    AND IFNULL(falla_red, '') = @FallaRedOriginal
                    AND IFNULL(comentarios_red, '') = @ComentarioRedOriginal
                    AND IFNULL(falla_hardware, '') = @FallaHardwareOriginal
                    AND IFNULL(comentarios_hardware, '') = @ComentarioHardwareOriginal
                    AND IFNULL(falla_software, '') = @FallaSoftwareOriginal
                    AND IFNULL(comentarios_software, '') = @ComentarioSoftwareOriginal
                LIMIT 1";

                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@NuevoNumeroComputadora", nuevoNumeroComputadora ?? string.Empty);
                        comando.Parameters.AddWithValue("@NuevaFallaRed", nuevaFallaRed ?? string.Empty);
                        comando.Parameters.AddWithValue("@NuevoComentarioRed", nuevoComentarioRed ?? string.Empty);
                        comando.Parameters.AddWithValue("@NuevaFallaHardware", nuevaFallaHardware ?? string.Empty);
                        comando.Parameters.AddWithValue("@NuevoComentarioHardware", nuevoComentarioHardware ?? string.Empty);
                        comando.Parameters.AddWithValue("@NuevaFallaSoftware", nuevaFallaSoftware ?? string.Empty);
                        comando.Parameters.AddWithValue("@NuevoComentarioSoftware", nuevoComentarioSoftware ?? string.Empty);
                        comando.Parameters.AddWithValue("@FkCodigoAcceso", fkCodigoAcceso);
                        comando.Parameters.AddWithValue("@FkUsuario", fkUsuario);
                        comando.Parameters.AddWithValue("@NumeroOriginal", numeroOriginal ?? string.Empty);
                        comando.Parameters.AddWithValue("@FallaRedOriginal", fallaRedOriginal ?? string.Empty);
                        comando.Parameters.AddWithValue("@ComentarioRedOriginal", comentarioRedOriginal ?? string.Empty);
                        comando.Parameters.AddWithValue("@FallaHardwareOriginal", fallaHardwareOriginal ?? string.Empty);
                        comando.Parameters.AddWithValue("@ComentarioHardwareOriginal", comentarioHardwareOriginal ?? string.Empty);
                        comando.Parameters.AddWithValue("@FallaSoftwareOriginal", fallaSoftwareOriginal ?? string.Empty);
                        comando.Parameters.AddWithValue("@ComentarioSoftwareOriginal", comentarioSoftwareOriginal ?? string.Empty);

                        int filasAfectadas = comando.ExecuteNonQuery();
                        if (filasAfectadas > 0)
                        {
                            // Sin mensaje: la pantalla de Bitacoras lo confirma en su linea de estado.
                            return true;
                        }

                        MessageBox.Show("No se pudo actualizar la bitacora. Intenta recargar la lista y vuelve a intentarlo.", "Informacion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al actualizar la bitacora: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }


        public bool consultaFinal(string codigoAcceso,string matricula, string computadora, string fallared, string comentariored, string fallahardware, string comentariohardware, string fallasotware, string comentariosoftware)
        {
            try
            {
                // Se vuelve a comprobar la ventana al guardar: el alumno puede haber
                // dejado el formulario abierto hasta despues de la hora de salida.
                VentanaCodigo ventana = ObtenerVentanaCodigo(codigoAcceso);
                if (ventana == null)
                {
                    MessageBox.Show("El código de acceso ya no existe.", "Código no válido", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                if (ventana.Estado != EstadoCodigo.Vigente)
                {
                    MessageBox.Show("El código ya no está vigente (válido hasta las " + ventana.Fin.ToString("HH:mm") + "). Pídele uno nuevo a tu profesor.", "Código expirado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    // Obtener el ID del código de acceso
                    string consultaIdCodigo = "SELECT idcodigos_accesos FROM codigos_accesos WHERE codigo = @Codigo";
                    int idCodigoAcceso;
                    using (MySqlCommand cmdIdCodigo = new MySqlCommand(consultaIdCodigo, conexion))
                    {
                        cmdIdCodigo.Parameters.AddWithValue("@Codigo", codigoAcceso);
                        object resultIdCodigo = cmdIdCodigo.ExecuteScalar();
                        if (resultIdCodigo != null)
                        {
                            idCodigoAcceso = Convert.ToInt32(resultIdCodigo);
                        }
                        else
                        {
                            throw new Exception("No se encontró el código de acceso especificado en la tabla.");
                        }
                    }


                    // Obtener el ID del laboratorio usando el código de acceso
                    string consultaIdusuario = "SELECT idusuarios FROM usuarios WHERE numero_identificador = @matricula";
                    int idusuario;
                    using (MySqlCommand cmdIdLaboratorio = new MySqlCommand(consultaIdusuario, conexion))
                    {
                        cmdIdLaboratorio.Parameters.AddWithValue("@matricula", matricula);
                        object resultIdusuario = cmdIdLaboratorio.ExecuteScalar();
                        if (resultIdusuario != null)
                        {
                            idusuario = Convert.ToInt32(resultIdusuario);
                        }
                        else
                        {
                            throw new Exception("No se encontró el laboratorio para el código de acceso especificado.");
                        }
                    }


                    // Insertar en la tabla registros_bitacoras, pero solo si ese
                    // alumno no tiene ya una con este codigo. La base del TESCHI no
                    // puede llevar el UNIQUE (fk_codigo_accesos, fk_usuario): trae
                    // duplicados historicos que no se borran. Asi que la regla la
                    // hace cumplir la propia insercion, y 0 filas significa
                    // "ya estaba". El catch del 1062 se queda para las bases que si
                    // tienen el UNIQUE.
                    string consultaInsertar = @"
                        INSERT INTO registros_bitacoras (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware, falla_software, comentarios_software)
                        SELECT @IdCodigoAcceso, @codigo_usuario, @computadora, @fallared, @comentariored, @fallahardware, @comentariohardware, @fallasoftware, @comentariosoftware
                        FROM DUAL
                        WHERE NOT EXISTS (SELECT 1 FROM registros_bitacoras WHERE fk_codigo_accesos = @IdCodigoAcceso AND fk_usuario = @codigo_usuario)";
                    using (MySqlCommand cmdInsertar = new MySqlCommand(consultaInsertar, conexion))
                    {
                        cmdInsertar.Parameters.AddWithValue("@IdCodigoAcceso", idCodigoAcceso);
                        cmdInsertar.Parameters.AddWithValue("@codigo_usuario", idusuario);
                        cmdInsertar.Parameters.AddWithValue("@computadora", computadora);
                        cmdInsertar.Parameters.AddWithValue("@fallared", fallared);
                        cmdInsertar.Parameters.AddWithValue("@comentariored", comentariored);
                        cmdInsertar.Parameters.AddWithValue("@fallahardware", fallahardware);
                        cmdInsertar.Parameters.AddWithValue("@comentariohardware", comentariohardware);
                        cmdInsertar.Parameters.AddWithValue("@fallasoftware", fallasotware);
                        cmdInsertar.Parameters.AddWithValue("@comentariosoftware", comentariosoftware);

                        int filasAfectadas = cmdInsertar.ExecuteNonQuery();


                        if (filasAfectadas <= 0)
                        {
                            // El WHERE NOT EXISTS encontro la suya: es un duplicado, no
                            // un fallo. Mismo mensaje que el 1062 de mas abajo.
                            MessageBox.Show("Ya registraste tu bitácora con este código.", "Registro duplicado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return false;
                        }
                        else {
                            MessageBox.Show($" Se inserto de manera exitosa el registro", "Exito", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return true;

                        }
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                // UNIQUE (fk_codigo_accesos, fk_usuario) en registros_bitacoras:
                // doble clic en Guardar o segundo intento con el mismo codigo.
                MessageBox.Show("Ya registraste tu bitácora con este código.", "Registro duplicado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            catch (Exception ex)
            {
                // Manejo de excepciones, puedes registrar el error o mostrar un mensaje al usuario
                MessageBox.Show($"Error al insertar el registro en la bitacora: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
        //Consulta para obtener el ID del usuario
        public string idusuario(string matricula)
        {
            try
            {
                string consulta = "SELECT idusuarios FROM usuarios WHERE numero_identificador = @matricula;";
                using (MySqlConnection conexion = mconexion.GetConexion())
                {   
                    using (MySqlCommand cmd = new MySqlCommand(consulta, conexion))
                    {
                        cmd.Parameters.AddWithValue("@matricula", matricula);
                        object resultado = cmd.ExecuteScalar();
                        if (resultado != null)
                        {
                            return resultado.ToString();
                        }
                        else
                        {
                            MessageBox.Show("Matrícula no encontrada", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Error al buscar la matrícula: {e.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }



    }

}

//SELECT nombre_carrera FROM carreras;
