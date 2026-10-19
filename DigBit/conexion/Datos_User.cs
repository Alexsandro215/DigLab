using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DigBit.conexion

{
    internal class Datos_User
    {
            public static String snumeroIdentificador;
            public static String snumero;

        /// <summary>
        /// Tipo del usuario con la sesion abierta: 1 alumno, 2 profesor,
        /// 3 administrador, 4 encargado de laboratorio (solo consulta, ver
        /// Infraestructura.Permisos), 0 nadie. Lo fija el login y sirve para que
        /// una pantalla no se abra a quien no le toca.
        /// </summary>
        public static int TipoUsuario { get; set; }

        public static bool EsAdministrador
        {
            get { return TipoUsuario == 3; }
        }
            public static String getUser()
            {
                return snumeroIdentificador;
            }
            public static void SetUser(string matricula)
            {
                snumeroIdentificador = matricula;
            }

        public static String getcodigo() {
            return snumero;
        }

        public static void Setcodigo(string codigo) {
            snumero = codigo;
        }

        // Ventana del codigo con el que entro el alumno. La fase 3 la usa para
        // saber cuando termina su sesion en el equipo.
        public static VentanaCodigo VentanaActual { get; set; }

        // Fase 4: si el codigo se resolvio SIN servidor, aqui queda lo necesario
        // para encolar la bitacora (VentanaActual.IdCodigo es 0 en ese caso).
        public static SesionLocal SesionSinConexion { get; set; }

        // Contrasenas equivocadas al volver a pedirla antes de un cambio delicado
        // (DialogoContrasenaAdmin) en esta sesion: cuentan aunque se cierre el
        // dialogo y se vuelva a abrir, y vuelven a cero al acertar.
        public static int ConfirmacionesFallidas { get; set; }

        // Al cerrar sesion: sin esto el siguiente usuario del mismo equipo hereda
        // la matricula y el codigo del anterior (todo es estatico).
        public static void Limpiar()
        {
            snumeroIdentificador = null;
            snumero = null;
            TipoUsuario = 0;
            VentanaActual = null;
            SesionSinConexion = null;
            ConfirmacionesFallidas = 0;
            // La del encargado era de solo consulta: el siguiente vuelve a la del equipo.
            Conexion.UsarCuentaDelEquipo();
        }
    }
}
