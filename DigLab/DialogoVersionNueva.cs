using System;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Al abrir DigLab en un equipo de administracion o de docente, si la administracion
    /// publico una version nueva (db/29): la ofrece y, si se acepta, la descarga aqui
    /// mismo, con su barra, y comprueba la huella. OK con Ruta: el instalador listo para
    /// correr (lo corre Actualizador).
    /// </summary>
    internal sealed class DialogoVersionNueva : Form
    {
        private const int Ancho = 600;
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);

        private readonly VersionPublicada version;
        private readonly string destino;
        private readonly Button btnActualizar;
        private readonly Button btnDespues;
        private readonly ProgressBar barra;
        private readonly Label lblEstado;
        private readonly Label lblError;
        private bool descargando;

        /// <summary>El instalador descargado y comprobado, con OK.</summary>
        public string Ruta { get; private set; }

        public DialogoVersionNueva(VersionPublicada version, string destino)
        {
            this.version = version;
            this.destino = destino;
            EstiloHorarios.PrepararDialogo(this, "DigLab: versión nueva", new Size(Ancho, 540));
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;

            Controls.Add(EstiloAdmin.TituloDialogo("Hay una versión nueva de DigLab", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = EstiloAdmin.TextoOscuro,
                Location = new Point(24, 58),
                Size = new Size(Ancho - 48, 24),
                Text = "Esta computadora tiene DigLab " + ActualizacionesDatos.Mostrar(ActualizacionesDatos.VersionInstalada)
                    + "; la administración publicó la " + ActualizacionesDatos.Mostrar(version.Numero) + "."
            });

            // Lo que cambia, como en Laboratorios > "Version de DigLab...": en lista y, al
            // final, quien la hizo y cuando.
            PanelDetalleVersion cambios = new PanelDetalleVersion
            {
                Location = new Point(24, 88),
                Size = new Size(Ancho - 48, 220)
            };
            cambios.Mostrar(version, "La versión nueva");
            Controls.Add(cambios);

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 318),
                Size = new Size(Ancho - 48, 44),
                Text = "Al actualizar se descarga (" + ActualizacionesDatos.Peso(version.Tamano) + "), Windows pide permiso de administrador, "
                    + "se instala en un minuto y DigLab se vuelve a abrir solo. Tu configuración y tus datos se conservan."
            });

            barra = new ProgressBar { Location = new Point(24, 370), Size = new Size(Ancho - 48, 18), Visible = false, Maximum = 100 };
            Controls.Add(barra);
            lblEstado = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 392),
                Size = new Size(Ancho - 48, 20)
            };
            Controls.Add(lblEstado);
            lblError = EstiloAdmin.LineaError(new Point(24, 370), Ancho - 48, 40);
            Controls.Add(lblError);

            ClientSize = new Size(Ancho, ClientSize.Height + EstiloAdmin.AireBajoBotones);
            int y = ClientSize.Height - 56 - EstiloAdmin.AireBajoBotones;
            btnDespues = EstiloHorarios.CrearBoton("Más tarde", new Point(Ancho - 24 - 130, y), new Size(130, 40));
            btnDespues.DialogResult = DialogResult.Cancel;
            CancelButton = btnDespues;
            btnActualizar = EstiloAdmin.BotonPrincipal("Actualizar ahora", new Point(btnDespues.Left - 10 - 190, y), new Size(190, 40));
            btnActualizar.Click += (s, e) => Descargar();
            Controls.AddRange(new Control[] { btnActualizar, btnDespues });
            AcceptButton = btnActualizar;
            ActiveControl = btnActualizar;

            FormClosing += (s, e) =>
            {
                // A media descarga no se cierra: quedaria un archivo a medias.
                if (descargando)
                {
                    e.Cancel = true;
                }
            };
        }

        private void Descargar()
        {
            descargando = true;
            btnActualizar.Enabled = false;
            btnDespues.Enabled = false;
            lblError.Text = "";
            barra.Value = 0;
            barra.Visible = true;
            lblEstado.Text = "Descargando la versión " + ActualizacionesDatos.Mostrar(version.Numero) + "...";
            Cursor = Cursors.WaitCursor;

            Task.Run(() => ActualizacionesDatos.Descargar(version, destino, porciento =>
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        barra.Value = Math.Max(0, Math.Min(100, porciento));
                        lblEstado.Text = "Descargando la versión " + ActualizacionesDatos.Mostrar(version.Numero) + "... " + porciento + " %";
                    }));
                }
                catch (InvalidOperationException)
                {
                    // La ventana ya no esta.
                }
            })).ContinueWith(tarea =>
            {
                descargando = false;
                Cursor = Cursors.Default;
                if (tarea.IsFaulted)
                {
                    Exception ex = tarea.Exception.GetBaseException();
                    Log.Error("Descargar DigLab " + version.Version, ex);
                    barra.Visible = false;
                    lblEstado.Text = "";
                    lblError.Text = ex is ErrorDeDatos
                        ? ex.Message
                        : "No se pudo descargar la versión nueva" + (MensajeDeFallo.EsDeRed(ex) ? ": se perdió la conexión con el servidor" : "")
                            + ". DigLab sigue con la que tiene; inténtalo al volver a abrirlo.";
                    btnDespues.Enabled = true;
                    btnDespues.Text = "Cerrar";
                    return;
                }

                lblEstado.Text = "Descargada y comprobada. Se abre el instalador...";
                Ruta = destino;
                DialogResult = DialogResult.OK;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
