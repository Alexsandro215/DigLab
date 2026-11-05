using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DigLab.conexion;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// La sesion que se queda sola en un equipo compartido: si nadie toca el
    /// teclado ni el raton durante un rato, un aviso modal "Sigues ahi?" con
    /// cuenta atras y "Seguir aqui"; si nadie lo pulsa, se cierra la sesion de
    /// DigLab (AppContexto.CerrarSesionPorInactividad) y queda en el log. Asi
    /// nadie registra la bitacora de otro ni le cambia el correo en Mi perfil.
    /// La usan la pantalla del alumno (3 min, mientras no registra) y el panel
    /// del profesor en el kiosco. No abre nada de Windows.
    /// </summary>
    internal static class Inactividad
    {
        /// <summary>
        /// Cuanto hace que nadie toca el teclado ni el raton en esta sesion de
        /// Windows (GetLastInputInfo). Las pruebas la sustituyen para no esperar
        /// minutos.
        /// </summary>
        internal static Func<TimeSpan> MedirInactividad = SinTecladoNiRaton;

        /// <summary>
        /// Vigila la ventana mientras este abierta. Cada segundo mira cuanto hace
        /// que no se toca el teclado ni el raton (cualquier tecla o movimiento en
        /// esta sesion de Windows, tambien fuera de DigLab). Con la ventana a la
        /// vista (ni escondida ni minimizada), sePuedeCerrar() en true y la espera
        /// cumplida, muestra el aviso encima de todo: "Sigues ahi?", "Tu sesion
        /// se cerrara en 30 s." (cuenta atras) y textoAviso debajo, con el boton
        /// "Seguir aqui" (tambien Enter y Esc). Si nadie lo pulsa, cierra la
        /// sesion y lo apunta en el log. Si sePuedeCerrar() pasa a false con el
        /// aviso abierto, el aviso se quita solo. Se detiene sola al cerrarse la
        /// ventana; el IDisposable la detiene antes.
        /// </summary>
        /// <param name="ventana">La pantalla de la sesion: el aviso sale sobre ella.</param>
        /// <param name="espera">Tiempo sin teclado ni raton antes del aviso (alumno: 3 min; profesor: 10 min).</param>
        /// <param name="aviso">La cuenta atras del aviso (30 s).</param>
        /// <param name="sePuedeCerrar">
        /// Se pregunta cada segundo, tambien con el aviso abierto: false = ahora no
        /// se cierra (ya registro, esta en clase). Null = siempre se puede.
        /// </param>
        /// <param name="textoAviso">
        /// Una o dos frases debajo de la cuenta atras con el porque, sin "Sigues
        /// ahi?" ni los segundos (los pone el aviso). Null: uno general.
        /// </param>
        public static IDisposable Vigilar(Form ventana, TimeSpan espera, TimeSpan aviso, Func<bool> sePuedeCerrar, string textoAviso)
        {
            if (ventana == null)
            {
                throw new ArgumentNullException("ventana");
            }

            return new Vigilancia(ventana, espera, aviso, sePuedeCerrar, textoAviso);
        }

        /// <summary>"3 minutos", "1 minuto", "45 segundos".</summary>
        internal static string Duracion(TimeSpan tiempo)
        {
            int minutos = (int)Math.Round(tiempo.TotalMinutes);
            if (tiempo.TotalSeconds >= 60 && Math.Abs(tiempo.TotalMinutes - minutos) < 0.01)
            {
                return minutos + (minutos == 1 ? " minuto" : " minutos");
            }

            int segundos = Math.Max(1, (int)Math.Round(tiempo.TotalSeconds));
            return segundos + (segundos == 1 ? " segundo" : " segundos");
        }

        /// <summary>La explicacion del aviso si quien vigila no da la suya.</summary>
        private static string TextoGeneral(TimeSpan espera)
        {
            return "No se ha tocado el teclado ni el ratón en " + Duracion(espera)
                + ". Si ya no estás aquí, tu sesión se cierra para que nadie más use tu cuenta en este equipo.";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        private static TimeSpan SinTecladoNiRaton()
        {
            LASTINPUTINFO info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            // Los dos en milisegundos desde que arranco Windows; restados sin signo,
            // la vuelta del contador (cada 49,7 dias) no estorba.
            uint ahora = unchecked((uint)Environment.TickCount);
            return TimeSpan.FromMilliseconds(unchecked(ahora - info.dwTime));
        }

        /// <summary>La vigilancia de una ventana: un reloj de un segundo y, cuando toca, el aviso.</summary>
        private sealed class Vigilancia : IDisposable
        {
            private readonly Form ventana;
            private readonly TimeSpan espera;
            private readonly TimeSpan aviso;
            private readonly Func<bool> sePuedeCerrar;
            private readonly string textoAviso;
            private Timer reloj;
            // Con el aviso abierto el reloj sigue (ShowDialog atiende los mensajes): no se repite.
            private bool avisando;

            public Vigilancia(Form ventana, TimeSpan espera, TimeSpan aviso, Func<bool> sePuedeCerrar, string textoAviso)
            {
                this.ventana = ventana;
                this.espera = espera;
                this.aviso = aviso;
                this.sePuedeCerrar = sePuedeCerrar;
                this.textoAviso = textoAviso;
                reloj = new Timer { Interval = 1000 };
                reloj.Tick += (s, e) => Revisar();
                reloj.Start();
                ventana.FormClosed += AlCerrarse;
                ventana.Disposed += AlCerrarse;
            }

            private void AlCerrarse(object sender, EventArgs e)
            {
                Dispose();
            }

            /// <summary>Lo que diga quien vigila; si falla, no se cierra nada.</summary>
            private bool SePuede()
            {
                try
                {
                    return sePuedeCerrar == null || sePuedeCerrar();
                }
                catch (Exception ex)
                {
                    Log.Error("Inactividad: no se pudo saber si se puede cerrar la sesion", ex);
                    return false;
                }
            }

            private void Revisar()
            {
                if (reloj == null || avisando)
                {
                    return;
                }

                if (ventana.IsDisposed)
                {
                    Dispose();
                    return;
                }

                // Escondida (el equipo ya se libero) o minimizada, no es la que se usa.
                if (!ventana.Visible || ventana.WindowState == FormWindowState.Minimized || !SePuede())
                {
                    return;
                }

                if (MedirInactividad() >= espera)
                {
                    Avisar();
                }
            }

            private void Avisar()
            {
                avisando = true;
                DialogResult respuesta = DialogResult.None;
                try
                {
                    using (AvisoInactividad dialogo = new AvisoInactividad(aviso, textoAviso ?? TextoGeneral(espera), SePuede))
                    {
                        respuesta = dialogo.ShowDialog(ventana);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Inactividad: no se pudo mostrar el aviso", ex);
                }
                finally
                {
                    avisando = false;
                }

                // Abort: se acabo la cuenta atras. Cualquier otra respuesta (Seguir
                // aqui, Esc, ya no se puede cerrar) deja la sesion como esta.
                if (reloj == null || ventana.IsDisposed || respuesta != DialogResult.Abort)
                {
                    return;
                }

                TimeSpan sinUso = MedirInactividad();
                Log.Info("Sesion de '" + (Datos_User.getUser() ?? "-") + "' cerrada por inactividad: " + (int)sinUso.TotalMinutes + " min "
                    + sinUso.Seconds + " s sin teclado ni raton en " + ventana.GetType().Name + " y nadie pulso Seguir aqui.");
                Dispose();
                if (AppContexto.Actual != null)
                {
                    AppContexto.Actual.CerrarSesionPorInactividad();
                }
            }

            public void Dispose()
            {
                if (reloj == null)
                {
                    return;
                }

                reloj.Stop();
                reloj.Dispose();
                reloj = null;
                ventana.FormClosed -= AlCerrarse;
                ventana.Disposed -= AlCerrarse;
            }
        }

        /// <summary>
        /// "Sigues ahi?" con la cuenta atras. Devuelve OK con "Seguir aqui" (o
        /// Enter, o Esc), Abort al acabarse el tiempo e Ignore si mientras tanto
        /// ya no se puede cerrar la sesion.
        /// </summary>
        private sealed class AvisoInactividad : Form
        {
            private const int Ancho = 480;
            private readonly Func<bool> sePuede;
            private readonly Label lblCuenta;
            private readonly Timer cuenta;
            private int segundos;

            public AvisoInactividad(TimeSpan aviso, string texto, Func<bool> sePuede)
            {
                this.sePuede = sePuede;
                segundos = Math.Max(1, (int)Math.Ceiling(aviso.TotalSeconds));

                DrawingFont letraTexto = new DrawingFont("Century Gothic", 10F);
                int altoTexto = TextRenderer.MeasureText(texto, letraTexto, new Size(Ancho - 48, int.MaxValue), TextFormatFlags.WordBreak).Height;
                EstiloHorarios.PrepararDialogo(this, "¿Sigues ahí?", new Size(Ancho, 92 + altoTexto + 84));
                // Solo se cierra con el boton (o Enter, o Esc), y encima de todo.
                ControlBox = false;
                TopMost = true;

                Controls.Add(EstiloAdmin.TituloDialogo("¿Sigues ahí?", Ancho - 48));
                lblCuenta = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(190, 85, 0),
                    Location = new Point(24, 54),
                    Size = new Size(Ancho - 48, 26)
                };
                Controls.Add(lblCuenta);
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = letraTexto,
                    ForeColor = Color.FromArgb(70, 70, 70),
                    Location = new Point(24, 88),
                    Size = new Size(Ancho - 48, altoTexto + 4),
                    Text = texto
                });

                Button seguir = EstiloAdmin.BotonPrincipal("Seguir aquí", new Point(Ancho - 24 - 170, ClientSize.Height - 60), new Size(170, 44));
                seguir.Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
                seguir.DialogResult = DialogResult.OK;
                Controls.Add(seguir);
                AcceptButton = seguir;
                CancelButton = seguir;
                ActiveControl = seguir;

                ActualizarCuenta();
                cuenta = new Timer { Interval = 1000 };
                cuenta.Tick += (s, e) => Contar();
                Shown += (s, e) => cuenta.Start();
            }

            private void Contar()
            {
                if (!sePuede())
                {
                    cuenta.Stop();
                    DialogResult = DialogResult.Ignore;
                    return;
                }

                segundos--;
                if (segundos <= 0)
                {
                    cuenta.Stop();
                    DialogResult = DialogResult.Abort;
                    return;
                }

                ActualizarCuenta();
            }

            private void ActualizarCuenta()
            {
                lblCuenta.Text = "Tu sesión se cerrará en " + segundos + " s.";
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    cuenta.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
