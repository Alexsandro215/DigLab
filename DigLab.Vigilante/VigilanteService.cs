using System.ServiceProcess;

namespace DigLab.Vigilante
{
    internal sealed class VigilanteService : ServiceBase
    {
        public const string Nombre = "DigLabVigilante";

        private Vigilante vigilante;

        public VigilanteService()
        {
            ServiceName = Nombre;
            CanStop = true;
            CanPauseAndContinue = false;
            CanHandleSessionChangeEvent = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            vigilante = new Vigilante(false);
            vigilante.Iniciar();
        }

        protected override void OnStop()
        {
            if (vigilante != null)
            {
                vigilante.Detener();
                vigilante = null;
            }
        }

        /// <summary>
        /// Cierre o inicio de sesion real de Windows: la sesion recordada con ese
        /// id ya no aplica (asi llega el siguiente alumno, incluso si Windows
        /// reutiliza el numero de sesion).
        /// </summary>
        protected override void OnSessionChange(SessionChangeDescription cambio)
        {
            Vigilante actual = vigilante;
            if (actual != null
                && (cambio.Reason == SessionChangeReason.SessionLogoff || cambio.Reason == SessionChangeReason.SessionLogon))
            {
                actual.SesionCambio(cambio.SessionId, cambio.Reason.ToString());
            }
        }
    }
}
