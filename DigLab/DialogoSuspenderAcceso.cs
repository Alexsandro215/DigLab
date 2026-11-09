using System;
using System.Drawing;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// "Suspender acceso..." de Estudiantes: cuantos dias, desde este momento, y por
    /// que. Dice de una vez hasta cuando seria y que la persona vera la razon. Al
    /// vencer el plazo vuelve a entrar sola. Solo la administracion llega aqui.
    /// </summary>
    internal sealed class DialogoSuspenderAcceso : Form
    {
        private const int Ancho = 560;
        private const int Alto = 356;
        private const int DiasPorOmision = 7;

        private readonly int idAlumno;
        private readonly NumericUpDown numDias;
        private readonly Label lblHasta;
        private readonly TextBox txtRazon;
        private readonly Label lblError;
        // Para decir hasta cuando con el reloj del servidor, que es el que cuenta.
        private readonly TimeSpan desfase;

        /// <summary>Hasta cuando quedo, ya guardado.</summary>
        public DateTime Hasta { get; private set; }

        public DialogoSuspenderAcceso(int idAlumno, string quien)
        {
            this.idAlumno = idAlumno;
            desfase = DesfaseConElServidor();
            EstiloHorarios.PrepararDialogo(this, "Suspender acceso", new Size(Ancho, Alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Suspender acceso", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(70, 70, 70),
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, 44),
                Text = quien + Environment.NewLine + "No podrá iniciar sesión en DigLab. Su cuenta y sus bitácoras no cambian."
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Días, desde ahora", new Point(24, 106)));
            numDias = new NumericUpDown
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(24, 130),
                Width = 90,
                // Mas ancho que lo permitido: con el limite de verdad, 999 se volveria 365
                // al salir de la caja, sin avisar, justo al pulsar "Suspender". Lo dice Guardar.
                Minimum = 0,
                Maximum = 9999,
                Value = DiasPorOmision,
                TextAlign = HorizontalAlignment.Right,
                AccessibleName = "Días de suspensión, desde ahora"
            };
            // Con las flechas: al avisar, Text todavia dice lo de antes.
            numDias.ValueChanged += (s, e) => MostrarHasta((int)numDias.Value);
            // Mientras se teclea: ValueChanged no llega hasta salir de la caja.
            numDias.KeyUp += (s, e) => MostrarLoTecleado();
            Controls.Add(numDias);

            lblHasta = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = EstiloAdmin.TextoOscuro,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(126, 128),
                Size = new Size(Ancho - 150, 30)
            };
            Controls.Add(lblHasta);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Razón (la verá al intentar entrar)", new Point(24, 170)));
            txtRazon = EstiloHorarios.CrearTexto(new Point(24, 194), Ancho - 48);
            txtRazon.Multiline = true;
            txtRazon.Height = 64;
            txtRazon.MaxLength = SuspensionesDatos.LargoRazon;
            txtRazon.AccessibleName = "Razón de la suspensión";
            EstiloHorarios.PonerTextoGuia(txtRazon, "Por ejemplo: daño al teclado del equipo PC-07");
            Controls.Add(txtRazon);

            lblError = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(190, 30, 30),
                Location = new Point(24, 262),
                Size = new Size(Ancho - 48, 34)
            };
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Suspender", Guardar);
            MostrarHasta(DiasPorOmision);
            ActiveControl = numDias;
        }

        private static TimeSpan DesfaseConElServidor()
        {
            try
            {
                return SuspensionesDatos.AhoraEnElServidor() - DateTime.Now;
            }
            catch (Exception ex)
            {
                // Solo es para el texto: lo que se guarda lo calcula el servidor.
                Log.Aviso("Suspender acceso: no se pudo leer la hora del servidor: " + ex.Message);
                return TimeSpan.Zero;
            }
        }

        private void MostrarHasta(int dias)
        {
            lblHasta.Text = DiasValidos(dias)
                ? "Podrá volver a entrar el " + SuspensionesDatos.Fecha(DateTime.Now + desfase + TimeSpan.FromDays(dias)) + "."
                : "De 1 a " + SuspensionesDatos.MaximoDias + " días.";
        }

        private void MostrarLoTecleado()
        {
            int dias;
            MostrarHasta(int.TryParse(numDias.Text, out dias) ? dias : 0);
        }

        private static bool DiasValidos(int dias)
        {
            return dias >= 1 && dias <= SuspensionesDatos.MaximoDias;
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Suspender el acceso de un estudiante")) return;
            // Text y no Value: lo que se ve en la caja, tecleado o con las flechas.
            int dias;
            if (!int.TryParse(numDias.Text, out dias) || !DiasValidos(dias))
            {
                lblError.Text = "Los días van de 1 a " + SuspensionesDatos.MaximoDias + ".";
                numDias.Focus();
                return;
            }

            if (HistorialCambios.Limpiar(txtRazon.Text, SuspensionesDatos.LargoRazon) == null)
            {
                lblError.Text = "Escribe la razón: la verá al intentar entrar.";
                txtRazon.Focus();
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                Hasta = SuspensionesDatos.Suspender(idAlumno, dias, txtRazon.Text);
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Suspender el acceso del usuario id " + idAlumno, ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo suspender", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
