using System;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// Modo kiosco: los formularios principales ocupan toda la pantalla, no se
    /// pueden cerrar (Alt+F4) ni minimizar, y "Salir" solo cierra la sesion de
    /// DigLab. Es como corre la app cuando es el shell de Windows en los equipos
    /// del laboratorio. Apagado (el valor por defecto) la app se comporta como una
    /// ventana normal, que es lo comodo para desarrollar.
    ///
    /// Se activa con ModoKiosco=true en appSettings o con la variable de entorno
    /// DIGLAB_MODO_KIOSCO=1; la variable tiene prioridad, igual que ocurre con la
    /// cadena de conexion.
    /// </summary>
    internal static class Kiosco
    {
        private const string ClaveAppSettings = "ModoKiosco";
        private const string VariableEntorno = "DIGLAB_MODO_KIOSCO";

        private static readonly bool activo = LeerConfiguracion();

        public static bool Activo
        {
            get { return activo; }
        }

        /// <summary>
        /// Adapta un formulario principal al modo kiosco. Fuera del modo kiosco no
        /// hace nada. Los formularios estan disenados a tamano fijo con controles
        /// en posiciones absolutas, asi que en vez de reescalar (que rompe los
        /// fondos) se agranda el formulario a la pantalla y se centra el contenido.
        /// </summary>
        public static void Aplicar(Form formulario)
        {
            if (!activo || formulario == null)
            {
                return;
            }

            Rectangle pantalla = Screen.PrimaryScreen.Bounds;
            Size disenado = formulario.ClientSize;

            formulario.SuspendLayout();
            formulario.FormBorderStyle = FormBorderStyle.None;
            formulario.MaximizeBox = false;
            formulario.MinimizeBox = false;
            formulario.StartPosition = FormStartPosition.Manual;
            formulario.Bounds = pantalla;

            int desplazamientoX = Math.Max(0, (pantalla.Width - disenado.Width) / 2);
            int desplazamientoY = Math.Max(0, (pantalla.Height - disenado.Height) / 2);
            if (desplazamientoX > 0 || desplazamientoY > 0)
            {
                foreach (Control control in formulario.Controls)
                {
                    control.Location = new Point(control.Left + desplazamientoX, control.Top + desplazamientoY);
                }
            }

            formulario.ResumeLayout();
            formulario.FormClosing += BloquearCierreDelUsuario;
        }

        /// <summary>
        /// Cierra un formulario desde codigo saltandose el bloqueo de Alt+F4
        /// (Form.Close() tambien se reporta como CloseReason.UserClosing).
        /// </summary>
        public static void CerrarAutorizado(Form formulario)
        {
            if (formulario == null || formulario.IsDisposed)
            {
                return;
            }

            formulario.FormClosing -= BloquearCierreDelUsuario;
            formulario.Close();
        }

        private static void BloquearCierreDelUsuario(object sender, FormClosingEventArgs e)
        {
            // UserClosing = Alt+F4 / boton X. TaskManagerClosing = WM_CLOSE directo,
            // como el de "Finalizar tarea". El apagado de Windows (WindowsShutDown)
            // se deja pasar.
            if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                Log.Aviso("Intento de cerrar la ventana en modo kiosco (" + e.CloseReason + "); ignorado.");
            }
        }

        private static bool LeerConfiguracion()
        {
            string desdeEntorno = Environment.GetEnvironmentVariable(VariableEntorno);
            if (!string.IsNullOrEmpty(desdeEntorno))
            {
                return EsVerdadero(desdeEntorno);
            }

            try
            {
                return EsVerdadero(ConfigurationManager.AppSettings[ClaveAppSettings]);
            }
            catch (ConfigurationErrorsException)
            {
                // Un App.config roto ya lo reporta la comprobacion de conexion con
                // un mensaje claro; aqui basta con no activar el kiosco.
                return false;
            }
        }

        internal static bool EsVerdadero(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return false;
            }

            switch (valor.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "si":
                case "sí":
                case "yes":
                    return true;
                default:
                    return false;
            }
        }
    }
}
