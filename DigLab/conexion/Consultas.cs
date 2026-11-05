using DigLab.Infraestructura;
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

namespace DigLab.conexion
{
    internal partial class Consultas
    {
        private Conexion mconexion;

        public Consultas()
        {
            mconexion = new Conexion();
        }
        /// <summary>
        /// Comprueba la cuenta y abre su ventana; true si la abrio (los avisos de
        /// por que no, los ensena aqui). Los docentes y los estudiantes, con el
        /// SIIA encendido para ellos en Ajustes, se comprueban con el SIIA
        /// (Consultas.Siia.cs): es lo unico que espera con await, en segundo plano,
        /// sin congelar la ventana; todo lo demas va como siempre. pedirNip ensena
        /// el paso del NIP del SIIA en la ventana del inicio de sesion y devuelve el
        /// NIP (null si la persona vuelve atras); sin el, quien necesita NIP no entra.
        /// </summary>
        public async Task<bool> RealizarInicioSesionAsync(string numeroIdentificador, string contraseña, Func<string, Task<string>> pedirNip = null)
        {
            mconexion = new Conexion();
            try
            {
                string almacenada = null;
                int tipoUsuarioId = 0;
                bool activo = true;
                bool existe = false;
                // Con db/24: si la administracion ya enlazo esta cuenta de docente con el SIIA.
                bool enlazado = false;
                string cadenaDeConsulta = null;
                // Solo para una cuenta de docente o de estudiante, o un numero que
                // DigLab no tiene, y segun Ajustes: la administracion y la persona
                // responsable del laboratorio entran siempre con su contrasena de DigLab.
                ConfiguracionSiia siia = ConfiguracionSiia.Apagada;
                bool conSiia = false;

                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    if (conexion == null)
                    {
                        MessageBox.Show(MensajeDeFallo.SinConexion("No se pudo iniciar sesión", MensajeDeFallo.AvisaAlLaboratorio),
                            "Iniciar sesión", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    string columnaActivo = Esquema.TieneBajas(conexion) ? "activo" : "1 AS activo";
                    string columnaEnlace = Esquema.EnlacesSiia(conexion) ? "(siia_enlazado_en IS NOT NULL)" : "0";
                    using (MySqlCommand consulta = new MySqlCommand(
                        "SELECT fk_tipo_usuario, password, " + columnaActivo + ", " + columnaEnlace + " AS enlazado FROM usuarios WHERE numero_identificador = @NumeroIdentificador",
                        conexion))
                    {
                        consulta.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador);

                        // El lector se cierra aqui a proposito: la migracion del hash
                        // manda un UPDATE por esta misma conexion, y no se puede con
                        // un lector abierto.
                        using (MySqlDataReader lector = consulta.ExecuteReader())
                        {
                            if (lector.Read())
                            {
                                existe = true;
                                tipoUsuarioId = (int)Convert.ToInt64(lector["fk_tipo_usuario"]);
                                almacenada = lector[1] == DBNull.Value ? null : lector[1].ToString();
                                activo = Convert.ToInt32(lector["activo"]) == 1;
                                enlazado = Convert.ToInt32(lector["enlazado"]) == 1;
                            }
                        }
                    }

                    if (!existe || tipoUsuarioId == TiposUsuario.Profesor || tipoUsuarioId == TiposUsuario.Alumno)
                    {
                        siia = LeerSiiaParaEntrar(conexion);
                        conSiia = siia.ParaElTipo(existe ? tipoUsuarioId : 0);
                    }

                    if (!conSiia)
                    {
                        bool necesitaRehash;
                        if (almacenada == null || !Contrasenas.Verificar(contraseña, almacenada, out necesitaRehash))
                        {
                            // El mismo mensaje exista o no el usuario: decir "ese numero no
                            // existe" le regala a quien lo intente la lista de matriculas
                            // validas.
                            MessageBox.Show("Tu usuario o tu contraseña no son correctos." + Environment.NewLine + Environment.NewLine
                                + "Revisa que estén bien escritos: la contraseña distingue mayúsculas y minúsculas. "
                                + "Si no la recuerdas, usa «¿Olvidaste tu contraseña?».",
                                "Iniciar sesión", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return false;
                        }

                        // Solo despues de comprobar la contrasena: quien la sabe ya sabe
                        // que su cuenta existe, y asi no se delata ninguna matricula.
                        if (!activo)
                        {
                            Log.Info("Inicio de sesion rechazado: '" + numeroIdentificador + "' esta dado de baja.");
                            MessageBox.Show("Tu cuenta está dada de baja. Si crees que es un error, acude a la administración de DigLab.", "Cuenta dada de baja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return false;
                        }

                        if (necesitaRehash)
                        {
                            MigrarContrasena(conexion, numeroIdentificador, contraseña, Contrasenas.EsFormatoAntiguo(almacenada));
                        }

                        // El encargado de laboratorio trabaja, en cualquier equipo, con la
                        // cuenta de solo lectura de la base (db/18). Sin ella no entra:
                        // nunca con la del equipo, que puede escribir.
                        if (tipoUsuarioId == TiposUsuario.Encargado)
                        {
                            string problema = CuentaDeConsulta.Preparar(conexion, out cadenaDeConsulta);
                            if (problema != null)
                            {
                                Log.Aviso("Inicio de sesion del encargado '" + numeroIdentificador + "' rechazado: " + problema);
                                MessageBox.Show(problema, "Sin cuenta de solo consulta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return false;
                            }
                        }
                    }
                }

                if (conSiia)
                {
                    // Sin la conexion abierta mientras se espera al SIIA (hasta unos
                    // segundos sin internet). Decide y, si entra, deja la cuenta al dia.
                    CuentaParaEntrar cuenta = new CuentaParaEntrar
                    {
                        Existe = existe,
                        Tipo = existe ? tipoUsuarioId : 0,
                        Almacenada = almacenada,
                        Activo = activo,
                        Enlazado = enlazado
                    };
                    tipoUsuarioId = await EntrarConSiiaAsync(numeroIdentificador, contraseña, siia, cuenta, pedirNip);
                    if (tipoUsuarioId == 0)
                    {
                        return false;
                    }
                }

                // Su contrasena es la del SIIA: Mi perfil no ofrece cambiarla.
                Datos_User.ContrasenaDelSiia = conSiia;

                // Null para todos menos el encargado: la cuenta del equipo. Si el
                // panel no llega a abrirse, la sesion se limpia y vuelve la del equipo.
                Conexion.UsarCuentaDeSesion(cadenaDeConsulta);
                bool abierto = false;
                try
                {
                    abierto = AbrirPestanaSegunTipoUsuario(tipoUsuarioId, numeroIdentificador);
                }
                finally
                {
                    if (!abierto)
                    {
                        Datos_User.Limpiar();
                    }
                }

                return abierto;
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Iniciar sesion de '" + numeroIdentificador + "'", ex);
                MessageBox.Show(MensajeDeFallo.Explicar("No se pudo iniciar sesión", ex, MensajeDeFallo.AvisaAlLaboratorio),
                    "Iniciar sesión", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }

        /// <summary>
        /// Las consultas viejas de esta clase avisaban con el texto del driver: ahora
        /// la causa y que hacer (MensajeDeFallo), y el detalle tecnico al registro.
        /// </summary>
        private static void AvisarFallo(string contexto, string queNoSePudo, Exception ex)
        {
            Log.Error(contexto, ex);
            MessageBox.Show(MensajeDeFallo.Explicar(queNoSePudo, ex, MensajeDeFallo.AvisaAlLaboratorio),
                "DigLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Si esa es la contrasena del administrador que inicio sesion (de tipo 3 y
        /// activo). Para volver a pedirla antes de un cambio delicado: si dejo la
        /// sesion abierta y no esta, otro no puede hacerlo (DialogoContrasenaAdmin).
        /// Solo comprueba: no migra el hash ni muestra nada.
        /// </summary>
        public bool ConfirmarContrasenaAdministrador(string numeroIdentificador, string contrasena)
        {
            return ConfirmarContrasena(numeroIdentificador, contrasena, TiposUsuario.Administrador);
        }

        /// <summary>
        /// Si esa es la contrasena de quien inicio sesion, con su cuenta activa y del
        /// tipo esperado: el administrador (3) antes de un cambio delicado, o el
        /// profesor (2) antes de cambiar una asistencia desde un equipo del
        /// laboratorio (kiosco). Cualquier otro tipo esperado da false: el alumno y
        /// el encargado de laboratorio (4) nunca confirman cambios. Solo comprueba:
        /// no migra el hash ni muestra nada.
        /// </summary>
        public bool ConfirmarContrasena(string numeroIdentificador, string contrasena, int tipoEsperado)
        {
            if (tipoEsperado != TiposUsuario.Administrador && tipoEsperado != TiposUsuario.Profesor)
            {
                return false;
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string columnaActivo = Esquema.TieneBajas(conexion) ? "activo" : "1 AS activo";
                using (MySqlCommand consulta = new MySqlCommand(
                    "SELECT fk_tipo_usuario, password, " + columnaActivo + " FROM usuarios WHERE numero_identificador = @NumeroIdentificador", conexion))
                {
                    consulta.Parameters.AddWithValue("@NumeroIdentificador", numeroIdentificador ?? "");
                    using (MySqlDataReader lector = consulta.ExecuteReader())
                    {
                        if (!lector.Read())
                        {
                            return false;
                        }

                        int tipo = Convert.ToInt32(lector["fk_tipo_usuario"]);
                        string almacenada = lector["password"] == DBNull.Value ? null : lector["password"].ToString();
                        bool activo = Convert.ToInt32(lector["activo"]) == 1;
                        bool necesitaRehash;
                        // Solo el tipo esperado (2 o 3): el encargado de laboratorio (4) nunca confirma cambios.
                        return tipo == tipoEsperado && activo && almacenada != null
                            && Contrasenas.Verificar(contrasena ?? "", almacenada, out necesitaRehash);
                    }
                }
            }
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



        /// <summary>
        /// Abre la ventana principal de ese tipo de usuario. El administrador (3) y
        /// el encargado de laboratorio (4) van al mismo panel; el encargado, en
        /// solo consulta (Permisos lo saca de Datos_User.TipoUsuario, que se fija
        /// ANTES de construir la ventana). False si esta version no conoce el
        /// tipo: el login se queda a la vista.
        /// </summary>
        public bool AbrirPestanaSegunTipoUsuario(int tipoUsuarioId, String numeroIdentificador)
        {
            Form principal;
            switch (tipoUsuarioId)
            {
                case TiposUsuario.Alumno:
                    Datos_User.TipoUsuario = tipoUsuarioId;
                    Datos_User.SetUser(numeroIdentificador);
                    principal = new Alumno_Principal();
                    break;

                case TiposUsuario.Profesor:
                    Datos_User.TipoUsuario = tipoUsuarioId;
                    Datos_User.SetUser(numeroIdentificador);
                    principal = new Profesor_Principal();
                    break;

                case TiposUsuario.Administrador:
                case TiposUsuario.Encargado:
                    // El encargado solo con la cuenta de solo consulta (RealizarInicioSesion);
                    // nunca con la del equipo, que puede escribir.
                    if (tipoUsuarioId == TiposUsuario.Encargado && !Conexion.ConCuentaDeSesion)
                    {
                        Datos_User.Limpiar();
                        Log.Aviso("Se pidio abrir el panel del encargado '" + numeroIdentificador + "' sin la cuenta de solo consulta: no se abre.");
                        return false;
                    }

                    // Administrador real (fase 6): antes era ADMINISTRADOR/123 en Login.cs.
                    Datos_User.TipoUsuario = tipoUsuarioId;
                    Datos_User.SetUser(numeroIdentificador);
                    principal = new PrincipalAdministrador();
                    break;

                default:
                    Datos_User.Limpiar();
                    Log.Aviso("Inicio de sesion de '" + numeroIdentificador + "' con el tipo de usuario " + tipoUsuarioId
                        + ", que esta version de DigLab no conoce: no se abre nada.");
                    MessageBox.Show("Esta versión de DigLab no reconoce tu tipo de cuenta. Entra desde un equipo con DigLab actualizado o avisa a la administración de DigLab.",
                        "Tipo de cuenta desconocido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
            }

            principal.Show();
            return true;
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
                        MessageBox.Show(MensajeDeFallo.SinConexion("No se pudieron leer tus datos", MensajeDeFallo.AvisaAlLaboratorio),
                            "DigLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                AvisarFallo("Leer el perfil de " + numeroIdentificador, "No se pudieron leer tus datos", ex);
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
                AvisarFallo("Buscar la carrera " + nombreCarrera, "No se pudo leer la carrera", ex);
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
                AvisarFallo("Leer las carreras", "No se pudieron leer las carreras", ex);
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
                AvisarFallo("Leer los grupos", "No se pudieron leer los grupos", ex);
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
                AvisarFallo("Leer los grupos", "No se pudieron leer los grupos", ex);
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
                AvisarFallo("Leer los grupos de la carrera " + idCarrera, "No se pudieron leer los grupos de la carrera", ex);
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
                AvisarFallo("Leer los semestres", "No se pudieron leer los semestres", ex);
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
                AvisarFallo("Leer los semestres", "No se pudieron leer los semestres", ex);
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
                AvisarFallo("Leer los laboratorios", "No se pudieron leer los laboratorios", ex);
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

        /// <summary>
        /// Si ya registro en esa sesion o, con "Ahora mismo", en la de la clase de
        /// antes de su misma terna a esa misma hora ese dia: su clase ya termino ayer
        /// (por eso solo pasa ahi) y la nueva es la misma. Dos clases seguidas del
        /// mismo profesor, grupo y materia siguen siendo dos registros: ninguna de
        /// las dos ha terminado ni sus horas se cruzan.
        /// </summary>
        internal bool AlumnoYaRegistro(int idCodigo, int idUsuario)
        {
            const string consulta = @"
                SELECT (SELECT COUNT(*) FROM registros_bitacoras WHERE fk_codigo_accesos = @IdCodigo AND fk_usuario = @IdUsuario)
                     + (SELECT COUNT(*)
                          FROM codigos_accesos ca
                          INNER JOIN codigos_accesos s
                                  ON s.fecha = ca.fecha AND s.idcodigos_accesos <> ca.idcodigos_accesos
                                 AND s.usuarios_idusuarios = ca.usuarios_idusuarios AND s.materias_id_materia = ca.materias_id_materia
                                 AND s.grupos_idgrupos = ca.grupos_idgrupos
                                 AND TIME(s.hora_entrada) < TIME(ca.hora_salida) AND TIME(s.hora_salida) > TIME(ca.hora_entrada)
                          INNER JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = s.idcodigos_accesos AND rb.fk_usuario = @IdUsuario
                          LEFT JOIN horarios hs            ON hs.idhorarios = s.horarios_idhorarios
                          LEFT JOIN horario_excepciones xs ON xs.idhorario_excepciones = s.horario_excepciones_id
                          INNER JOIN clases cs             ON cs.idclases = COALESCE(hs.clases_idclases, xs.clases_idclases)
                         WHERE ca.idcodigos_accesos = @IdCodigo AND cs.vigente_hasta < ca.fecha)";
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
                AvisarFallo("Buscar el semestre " + nombreSemestre, "No se pudo leer el semestre", ex);
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
                AvisarFallo("Buscar el grupo " + nombreGrupo, "No se pudo leer el grupo", ex);
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
                AvisarFallo("Buscar la carrera " + nombreCarrera, "No se pudo leer la carrera", ex);
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
                AvisarFallo("Leer las materias", "No se pudieron leer las materias", ex);
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
                AvisarFallo("Leer las materias", "No se pudieron leer las materias", ex);
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
                AvisarFallo("Leer las materias de la carrera " + idCarrera, "No se pudieron leer las materias de la carrera", ex);
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
                AvisarFallo("Leer los laboratorios", "No se pudieron leer los laboratorios", ex);
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
                AvisarFallo("Leer los laboratorios", "No se pudieron leer los laboratorios", ex);
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
                AvisarFallo("Buscar la materia " + nombreMateria, "No se pudo leer la materia", ex);
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
                AvisarFallo("Buscar el laboratorio " + nombreLaboratorio, "No se pudo leer el laboratorio", ex);
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
                AvisarFallo("Leer la carrera de " + numeroIdentificador, "No se pudo leer tu carrera", ex);
            }

            return idCarrera;
        }

        /// <summary>
        /// "Nombre Apellidos" de esa cuenta; vacio si no esta (Mi horario dice
        /// entonces solo "Tu horario de la semana.").
        /// </summary>
         public string MostrarNombreProfesor(string numeroIdentificador)
        {
            string nombreProfesor = "";
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
                AvisarFallo("Leer el nombre de " + numeroIdentificador, "No se pudo leer tu nombre", ex);
            }
            return nombreProfesor;
        }




        /// <summary>
        /// "Mi perfil" del alumno, todo o nada (antes, si fallaba la carrera, el
        /// nombre y el correo ya se habian guardado). Sin grupo (null), el que
        /// tuviera se conserva: ya no se le pide. Sin semestre (idSemestre menor
        /// que 1, que es lo que da ObtenerIdSemestre con el combo vacio), ninguno;
        /// o el que tenia, si la base aun no tiene la 10. Un alumno sin fila de
        /// carrera (cuentas viejas) la recibe aqui, si la base ya lo admite.
        /// </summary>
        public bool ActualizarUsuario(string idUsuarios, string nombre, string apellidoPaterno, string apellidoMaterno, string correo, int idCarrera, int? idGrupo, int idSemestre)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        ActualizarDatosBasicos(conexion, transaccion, idUsuarios, nombre, apellidoPaterno, apellidoMaterno, correo);

                        bool sinGrupo = Esquema.CarreraSinGrupo(conexion, transaccion);
                        int filas;
                        using (MySqlCommand contar = new MySqlCommand(
                            "SELECT COUNT(*) FROM carrera_grupo_semestre WHERE usuarios_numero_identificador = @idUsuarios", conexion, transaccion))
                        {
                            contar.Parameters.AddWithValue("@idUsuarios", idUsuarios);
                            filas = Convert.ToInt32(contar.ExecuteScalar());
                        }

                        string consulta = filas > 0
                            ? "UPDATE carrera_grupo_semestre SET carreras_idcarreras = @IdCarrera, grupos_idgrupos = COALESCE(@IdGrupo, grupos_idgrupos), "
                              + "semestres_idsemestres = " + (sinGrupo ? "@IdSemestre" : "COALESCE(@IdSemestre, semestres_idsemestres)")
                              + " WHERE usuarios_numero_identificador = @idUsuarios"
                            : sinGrupo
                                ? "INSERT INTO carrera_grupo_semestre (usuarios_numero_identificador, carreras_idcarreras, grupos_idgrupos, semestres_idsemestres) "
                                  + "VALUES (@idUsuarios, @IdCarrera, @IdGrupo, @IdSemestre)"
                                : null;
                        bool carreraPendiente = consulta == null;
                        if (carreraPendiente)
                        {
                            // Sin fila y sin la 10: la carrera no se puede guardar (pediria grupo).
                            Log.Aviso("Mi perfil de " + idUsuarios + ": no tiene fila de carrera y la base aun no admite alumnos sin grupo (falta db/10); se guarda lo demas.");
                        }
                        else
                        {
                            using (MySqlCommand comando = new MySqlCommand(consulta, conexion, transaccion))
                            {
                                comando.Parameters.AddWithValue("@idUsuarios", idUsuarios);
                                comando.Parameters.AddWithValue("@IdCarrera", idCarrera);
                                comando.Parameters.AddWithValue("@IdGrupo", idGrupo.HasValue ? (object)idGrupo.Value : DBNull.Value);
                                comando.Parameters.AddWithValue("@IdSemestre", idSemestre > 0 ? (object)idSemestre : DBNull.Value);
                                comando.ExecuteNonQuery();
                            }
                        }

                        transaccion.Commit();
                        if (carreraPendiente)
                        {
                            MessageBox.Show("Se guardaron tu nombre y tu correo, pero tu carrera todavía no se puede guardar. "
                                + "Avisa a la persona responsable del laboratorio.", "Mi perfil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }

                        return true;
                    }
                    catch
                    {
                        transaccion.Rollback();
                        throw;
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                MessageBox.Show("Ese correo ya lo usa otra cuenta. Escribe otro o déjalo vacío.", "Correo repetido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Mi perfil de " + idUsuarios, ex);
                MessageBox.Show(MensajeDeFallo.Explicar("No se pudieron guardar tus datos", ex, MensajeDeFallo.AvisaAlLaboratorio),
                    "Mi perfil", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool ActualizarUsuarioBasico(string idUsuarios, string nombre, string apellidoPaterno, string apellidoMaterno, string correo)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    ActualizarDatosBasicos(conexion, null, idUsuarios, nombre, apellidoPaterno, apellidoMaterno, correo);
                    return true;
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                MessageBox.Show("Ese correo ya lo usa otra cuenta. Escribe otro o déjalo vacío.", "Correo repetido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Mi perfil de " + idUsuarios, ex);
                MessageBox.Show(MensajeDeFallo.Explicar("No se pudieron guardar tus datos", ex, MensajeDeFallo.AvisaAlLaboratorio),
                    "Mi perfil", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Nombre, apellidos y correo. El correo vacio va como NULL: en la base de
        /// la escuela es UNICO, y con "" el segundo usuario sin correo chocaba con
        /// el primero (lo mismo que UsuariosDatos).
        /// </summary>
        private static void ActualizarDatosBasicos(MySqlConnection conexion, MySqlTransaction transaccion, string idUsuarios,
            string nombre, string apellidoPaterno, string apellidoMaterno, string correo)
        {
            using (MySqlCommand comando = new MySqlCommand(@"UPDATE usuarios
                SET nombre = @Nombre, apellido_paterno = @ApellidoPaterno, apellido_materno = @ApellidoMaterno, correo = @Correo
                WHERE idusuarios = @idUsuarios", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@idUsuarios", idUsuarios);
                comando.Parameters.AddWithValue("@Nombre", (nombre ?? "").Trim());
                comando.Parameters.AddWithValue("@ApellidoPaterno", (apellidoPaterno ?? "").Trim());
                comando.Parameters.AddWithValue("@ApellidoMaterno", (apellidoMaterno ?? "").Trim());
                comando.Parameters.AddWithValue("@Correo", string.IsNullOrWhiteSpace(correo) ? (object)DBNull.Value : correo.Trim());
                comando.ExecuteNonQuery();
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
                            throw new Exception("No se encontró ninguna cuenta con esa matrícula.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Al log (en un WinExe, Console.WriteLine no llega a ninguna parte); quien llama dice que paso.
                Log.Error("Consultas.ObtenerIdPorMatricula de " + matricula, ex);
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
                // Al log (en un WinExe, Console.WriteLine no llega a ninguna parte); quien llama dice que paso.
                Log.Error("Consultas.ObtenerNombreLaboratorioPorCodigo", ex);
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
                                    HoraEntrada = HorariosDatos.HoraLegible(reader["hora_entrada"]),
                                    HoraSalida = HorariosDatos.HoraLegible(reader["hora_salida"]),
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
                AvisarFallo("Leer los datos del PDF del codigo " + codigoAcceso, "No se pudieron leer los datos de la sesión", e);
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
                AvisarFallo("Leer los codigos de " + numeroIdentificador, "No se pudieron leer tus sesiones", ex);
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
                // Al log (en un WinExe, Console.WriteLine no llega a ninguna parte); quien llama dice que paso.
                Log.Error("Consultas.consultaRegistro", ex);
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
                // Dentro del try: este metodo no lanza (quien lo llama no atrapa nada).
                Permisos.Exigir("Corregir un registro de bitacora");
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
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

                    using (MySqlCommand comando = new MySqlCommand(consulta, conexion, transaccion))
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
                            // Quien, cuando y que decia antes (punto 9, db/21), en la misma
                            // transaccion: sin su renglon no queda la correccion.
                            AnotarCorreccion(conexion, transaccion, fkCodigoAcceso, fkUsuario,
                                new[] { numeroOriginal, fallaRedOriginal, comentarioRedOriginal, fallaHardwareOriginal, comentarioHardwareOriginal,
                                        fallaSoftwareOriginal, comentarioSoftwareOriginal },
                                new[] { nuevoNumeroComputadora, nuevaFallaRed, nuevoComentarioRed, nuevaFallaHardware, nuevoComentarioHardware,
                                        nuevaFallaSoftware, nuevoComentarioSoftware });
                            transaccion.Commit();
                            // Sin mensaje: la pantalla de Bitacoras lo confirma en su linea de estado.
                            return true;
                        }

                        // El UPDATE pide que el registro siga como se leyo: otro lo cambio antes.
                        Log.Aviso("Correccion de bitacora no guardada: el registro del usuario " + fkUsuario + " en la sesion "
                            + fkCodigoAcceso + " cambio desde que se leyo (o ya no existe).");
                        MessageBox.Show("No se guardó la corrección: el registro cambió desde que lo abriste. Actualiza la lista (F5) y vuelve a corregirlo.",
                            "Corregir registro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                }
            }
            catch (AccionNoPermitida ex)
            {
                // Solo consulta: lo mismo que ensena Permisos.Bloquear en las pantallas.
                MessageBox.Show(ex.Message, "Solo consulta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            catch (Exception ex)
            {
                // La causa y que hacer; ex.Message solo va al registro. Con AvisoDeFalloAdmin,
                // como el resto de Bitacoras: la red solo se culpa cuando no se llego al
                // servidor (una cuenta de la base rechazada o un permiso no son el cable).
                Log.Error("Corregir el registro del usuario " + fkUsuario + " en la sesion " + fkCodigoAcceso, ex);
                MessageBox.Show(AvisoDeFalloAdmin.Texto("No se pudo guardar la corrección", ex),
                    "Corregir registro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // Los campos que se corrigen, en el orden de ActualizarRegistroBitacora.
        private static readonly string[] CamposDeCorreccion =
        {
            "Equipo", "Falla de red", "Comentario de red", "Falla de hardware", "Comentario de hardware", "Falla de software", "Comentario de software"
        };

        /// <summary>
        /// El renglon del historial (db/21) de una bitacora corregida: de quien es,
        /// de que sesion y solo los campos que cambiaron, antes y despues. Sin
        /// db/21, nada.
        /// </summary>
        private static void AnotarCorreccion(MySqlConnection conexion, MySqlTransaction transaccion, string fkCodigoAcceso, string fkUsuario,
            string[] antes, string[] despues)
        {
            if (!HistorialCambios.Activo(conexion, transaccion))
            {
                return;
            }

            List<string> previos = new List<string>();
            List<string> nuevos = new List<string>();
            List<string> cambia = new List<string>();
            for (int i = 0; i < CamposDeCorreccion.Length; i++)
            {
                string a = (antes[i] ?? "").Trim();
                string d = (despues[i] ?? "").Trim();
                if (a == d)
                {
                    continue;
                }

                cambia.Add(HistorialCambios.ConArticulo(CamposDeCorreccion[i]));
                previos.AddRange(new[] { CamposDeCorreccion[i], a.Length == 0 ? "(vacío)" : a });
                nuevos.AddRange(new[] { CamposDeCorreccion[i], d.Length == 0 ? "(vacío)" : d });
            }

            if (cambia.Count == 0)
            {
                return;
            }

            int? idRegistro = null;
            int? idLaboratorio = null;
            DateTime? dia = null;
            string alumno = "";
            string sesion = "";
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT rb.idregistros_bitacoras, u.numero_identificador,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS alumno,
                       ca.laboratorios_idlaboratorios AS lab_id, DATE_FORMAT(COALESCE(ca.fecha, DATE(ca.hora_registro)), '%Y-%m-%d') AS dia,
                       ca.hora_entrada, ca.hora_salida, COALESCE(m.nombre_materia, '') AS materia, COALESCE(g.nombre_grupo, '') AS grupo,
                       COALESCE(l.nombre_laboratorio, '') AS laboratorio
                FROM registros_bitacoras rb
                INNER JOIN usuarios u         ON u.idusuarios = rb.fk_usuario
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
                LEFT JOIN materias m          ON m.id_materia = ca.materias_id_materia
                LEFT JOIN grupos g            ON g.idgrupos = ca.grupos_idgrupos
                LEFT JOIN laboratorios l      ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                WHERE rb.fk_codigo_accesos = @Sesion AND rb.fk_usuario = @Usuario
                LIMIT 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Sesion", fkCodigoAcceso);
                comando.Parameters.AddWithValue("@Usuario", fkUsuario);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        idRegistro = Convert.ToInt32(reader["idregistros_bitacoras"]);
                        idLaboratorio = reader["lab_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["lab_id"]);
                        DateTime fecha;
                        if (DateTime.TryParseExact(Convert.ToString(reader["dia"]), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out fecha))
                        {
                            dia = fecha;
                        }

                        alumno = (Convert.ToString(reader["alumno"]) ?? "").Trim() + " (" + Convert.ToString(reader["numero_identificador"]) + ")";
                        string entrada = HorariosDatos.HoraLegible(reader["hora_entrada"]);
                        string salida = HorariosDatos.HoraLegible(reader["hora_salida"]);
                        string materia = Convert.ToString(reader["materia"]);
                        string laboratorio = Convert.ToString(reader["laboratorio"]);
                        sesion = " en la sesión" + (dia.HasValue ? " del " + HistorialCambios.DiaYFecha(dia.Value) : "")
                            + (entrada.Length > 0 && salida.Length > 0 ? " de " + entrada + " a " + salida : "")
                            + (materia.Length > 0 ? ", " + materia + " - " + Convert.ToString(reader["grupo"]) : ", uso libre")
                            + (laboratorio.Length > 0 ? ", " + laboratorio : "");
                    }
                }
            }

            HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
            {
                Tipo = TipoDeCambio.Bitacora,
                Accion = AccionDeCambio.Cambio,
                Tabla = "registros_bitacoras",
                RegistroId = idRegistro,
                LaboratorioId = idLaboratorio,
                Fecha = dia,
                Descripcion = (alumno.Length > 0 ? alumno : "Estudiante " + fkUsuario) + sesion + ": cambia " + HistorialCambios.Enumerar(cambia) + ".",
                Antes = HistorialCambios.Datos(previos.ToArray()),
                Despues = HistorialCambios.Datos(nuevos.ToArray())
            });
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
                    MessageBox.Show("El código ya no está vigente (válido hasta las " + ventana.Fin.ToString("HH:mm") + "). Pídele uno nuevo a tu docente.", "Código expirado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    // alumno no tiene ya una con este codigo. Una base con
                    // duplicados viejos (la base vieja de la escuela) no lleva el
                    // UNIQUE (fk_codigo_accesos, fk_usuario) de la 03. Asi que la
                    // regla la hace cumplir la propia insercion, y 0 filas significa
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
                            MessageBox.Show("Tu bitácora quedó registrada.", "Bitácora registrada", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                AvisarFallo("Guardar la bitacora (consultaFinal) de " + matricula, "No se pudo guardar tu bitácora", ex);
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
                            MessageBox.Show("Tu matrícula no está registrada en el servidor. Avisa a la persona responsable del laboratorio.",
                                "Matrícula no encontrada", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AvisarFallo("Buscar la matricula " + matricula, "No se pudo buscar la matrícula", e);
                return null;
            }
        }



    }

}

//SELECT nombre_carrera FROM carreras;
