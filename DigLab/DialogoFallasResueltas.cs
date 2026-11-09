using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// "Resolver fallas..." de Bitacoras: lo que se reporto en una sesion, una casilla
    /// por cada cosa. Marcada, ya se arreglo: sale de naranja y de Alertas, y va al
    /// final del PDF. Desmarcada, vuelve a estar pendiente (por si se marco por error).
    /// Lo que se reporto no cambia. Solo la administracion llega aqui.
    /// </summary>
    internal sealed class DialogoFallasResueltas : Form
    {
        private const int Ancho = 820;
        private const int Alto = 480;
        private readonly int idSesion;
        private readonly List<FallaDeSesion> fallas;
        private readonly CheckedListBox lista;
        private readonly TextBox txtNota;
        private readonly Label lblError;

        /// <summary>Cuantas se dieron por resueltas y cuantas volvieron a estar pendientes.</summary>
        public int Resueltas { get; private set; }

        public int Reabiertas { get; private set; }

        /// <param name="sesion">De que sesion son, en una linea: lo dice arriba.</param>
        public DialogoFallasResueltas(int idSesion, List<FallaDeSesion> fallas, string sesion)
        {
            this.idSesion = idSesion;
            this.fallas = fallas;
            EstiloHorarios.PrepararDialogo(this, "Resolver fallas", new Size(Ancho, Alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Fallas de esta sesión", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(70, 70, 70),
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, 62),
                Text = sesion + Environment.NewLine
                    + "Marca lo que ya se arregló: deja de salir en naranja y en Alertas, y el PDF lo dice al final. "
                    + "Si marcaste algo por error, desmárcalo. Lo que se reportó no cambia."
            });

            lista = new CheckedListBox
            {
                CheckOnClick = true,
                Font = new DrawingFont("Century Gothic", 10F),
                HorizontalScrollbar = true,
                IntegralHeight = false,
                Location = new Point(24, 118),
                Size = new Size(Ancho - 48, 190),
                AccessibleName = "Lo que se reportó en esta sesión; marcado, ya está resuelto"
            };
            foreach (FallaDeSesion falla in fallas)
            {
                lista.Items.Add(falla, falla.Resuelta);
            }

            Controls.Add(lista);
            LinkLabel lnkTodas = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Location = new Point(24, 314),
                Text = "Marcar todas"
            };
            lnkTodas.LinkClicked += (s, e) =>
            {
                for (int i = 0; i < lista.Items.Count; i++)
                {
                    lista.SetItemChecked(i, true);
                }
            };
            Controls.Add(lnkTodas);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Qué se hizo (opcional, sale en el PDF)", new Point(24, 340)));
            txtNota = EstiloHorarios.CrearTexto(new Point(24, 364), Ancho - 48);
            txtNota.MaxLength = FallasResueltasDatos.LargoNota;
            EstiloHorarios.PonerTextoGuia(txtNota, "Por ejemplo: se cambió el teclado");
            Controls.Add(txtNota);

            lblError = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(190, 30, 30),
                Location = new Point(24, Alto - 62),
                Size = new Size(Ancho - 48 - 300, 52)
            };
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ActiveControl = lista;
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Dar por resueltas las fallas de una sesion")) return;
            List<FallaDeSesion> resolver = new List<FallaDeSesion>();
            List<FallaDeSesion> reabrir = new List<FallaDeSesion>();
            for (int i = 0; i < lista.Items.Count; i++)
            {
                FallaDeSesion falla = (FallaDeSesion)lista.Items[i];
                bool marcada = lista.GetItemChecked(i);
                if (marcada && !falla.Resuelta)
                {
                    resolver.Add(falla);
                }
                else if (!marcada && falla.Resuelta)
                {
                    reabrir.Add(falla);
                }
            }

            if (resolver.Count + reabrir.Count == 0)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                FallasResueltasDatos.Guardar(idSesion, resolver, reabrir, txtNota.Text.Trim());
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Guardar las fallas resueltas de la sesion " + idSesion, ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Resueltas = resolver.Count;
            Reabiertas = reabrir.Count;
            DialogResult = DialogResult.OK;
        }
    }
}
