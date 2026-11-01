using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace DigBit
{
    /// <summary>
    /// Un solo lenguaje para los botones (2026-10-31): apagado se ve apagado. Lo
    /// pone EstiloHorarios.CrearBoton en todos los suyos, y con el los principales
    /// (EstiloAdmin.BotonPrincipal) y los rojos (EstiloAdmin.Peligroso). Antes,
    /// un boton apagado se veia casi igual que uno encendido y tres pantallas lo
    /// arreglaban a mano, cada una a su modo (y al encenderse volvian a un verde viejo).
    ///
    /// Al apagarse, el boton guarda sus colores y toma los de apagado: el relleno
    /// (el verde principal, el rojo) pasa a gris verdoso, con el borde igual; el
    /// de borde (secundario), a un fondo gris muy claro con borde y letra grises.
    /// Al encenderse vuelve a los suyos, asi que no se pierden ni el rojo ni los
    /// colores propios de una pantalla. Si la pantalla cambia un color mientras
    /// esta apagado, al encenderse se queda el de la pantalla (no se pisa).
    ///
    /// Windows pinta la letra de un boton plano apagado con un gris sacado del
    /// fondo, sin mirar ForeColor: la del secundario se repinta encima (como los
    /// chips del estudiante). La del principal, sobre su gris verdoso, ya se lee
    /// apagada (es la de "Registrar mi bitacora" de siempre).
    /// </summary>
    internal static class BotonesApagados
    {
        /// <summary>Fondo y borde del boton relleno (principal o rojo) apagado.</summary>
        public static readonly Color FondoPrincipal = Color.FromArgb(205, 210, 205);
        /// <summary>Fondo del boton de borde (secundario) apagado.</summary>
        public static readonly Color FondoSecundario = Color.FromArgb(246, 246, 246);
        /// <summary>Borde y letra del boton de borde (secundario) apagado.</summary>
        public static readonly Color GrisSecundario = Color.FromArgb(200, 200, 200);
        /// <summary>
        /// La letra del secundario apagado: un poco mas oscura que su borde, para
        /// que aun se lea que boton es (Exportar, PDF) aunque no se pueda pulsar.
        /// </summary>
        public static readonly Color LetraSecundario = Color.FromArgb(160, 160, 160);

        /// <summary>Lo que el boton tenia al apagarse y lo que se le puso.</summary>
        private sealed class Estado
        {
            public bool Apagado;
            public Color Fondo;
            public Color Borde;
            public Color FondoPuesto;
            public Color BordePuesto;
        }

        private static readonly ConditionalWeakTable<Button, Estado> estados = new ConditionalWeakTable<Button, Estado>();

        /// <summary>
        /// El boton se ve apagado mientras lo este (tambien si se apaga su
        /// contenedor). Una sola vez por boton; llamarlo otra vez no hace nada.
        /// </summary>
        public static void Seguir(Button boton)
        {
            Estado estado;
            if (boton == null || estados.TryGetValue(boton, out estado))
            {
                return;
            }

            estados.Add(boton, new Estado());
            boton.EnabledChanged += (s, e) => Aplicar(boton);
            boton.Paint += PintarLetraApagada;
            Aplicar(boton);
        }

        /// <summary>
        /// Pone el fondo y el borde de un boton que sigue esta pieza (EstiloAdmin.Peligroso):
        /// si esta apagado, son los que tomara al encenderse, y mientras tanto se ve
        /// apagado como lo que es (relleno o de borde).
        /// </summary>
        public static void PonerColores(Button boton, Color fondo, Color borde)
        {
            Estado estado;
            if (boton == null)
            {
                return;
            }

            if (!estados.TryGetValue(boton, out estado) || !estado.Apagado)
            {
                boton.BackColor = fondo;
                boton.FlatAppearance.BorderColor = borde;
                return;
            }

            estado.Fondo = fondo;
            estado.Borde = borde;
            Poner(boton, estado);
        }

        /// <summary>Relleno (principal, rojo) o de borde (blanco o casi): asi se apaga.</summary>
        public static bool EsRelleno(Color fondo)
        {
            return fondo.A > 0 && fondo.GetBrightness() < 0.8f;
        }

        private static void Aplicar(Button boton)
        {
            Estado estado;
            if (!estados.TryGetValue(boton, out estado))
            {
                return;
            }

            bool apagado = !boton.Enabled;
            if (apagado == estado.Apagado)
            {
                return;
            }

            if (apagado)
            {
                estado.Fondo = boton.BackColor;
                estado.Borde = boton.FlatAppearance.BorderColor;
                estado.Apagado = true;
                Poner(boton, estado);
                return;
            }

            estado.Apagado = false;
            // Solo lo que sigue como se puso: si la pantalla lo cambio mientras tanto, manda la pantalla.
            if (boton.BackColor.ToArgb() == estado.FondoPuesto.ToArgb())
            {
                boton.BackColor = estado.Fondo;
            }

            if (boton.FlatAppearance.BorderColor.ToArgb() == estado.BordePuesto.ToArgb())
            {
                boton.FlatAppearance.BorderColor = estado.Borde;
            }
        }

        private static void Poner(Button boton, Estado estado)
        {
            bool relleno = EsRelleno(estado.Fondo);
            estado.FondoPuesto = relleno ? FondoPrincipal : FondoSecundario;
            estado.BordePuesto = relleno ? FondoPrincipal : GrisSecundario;
            boton.BackColor = estado.FondoPuesto;
            boton.FlatAppearance.BorderColor = estado.BordePuesto;
        }

        /// <summary>La letra gris del secundario apagado, encima de la que pinta Windows.</summary>
        private static void PintarLetraApagada(object sender, PaintEventArgs e)
        {
            Button boton = (Button)sender;
            Estado estado;
            if (boton.Enabled || !estados.TryGetValue(boton, out estado) || !estado.Apagado || EsRelleno(estado.Fondo)
                || boton.Image != null || boton.BackgroundImage != null || string.IsNullOrEmpty(boton.Text))
            {
                return;
            }

            int borde = boton.FlatStyle == FlatStyle.Flat ? boton.FlatAppearance.BorderSize : 0;
            Rectangle dentro = new Rectangle(borde, borde, boton.Width - 2 * borde, boton.Height - 2 * borde);
            if (dentro.Width <= 0 || dentro.Height <= 0)
            {
                return;
            }

            using (SolidBrush fondo = new SolidBrush(boton.BackColor))
            {
                e.Graphics.FillRectangle(fondo, dentro);
            }

            Rectangle texto = new Rectangle(dentro.X + boton.Padding.Left, dentro.Y + boton.Padding.Top,
                dentro.Width - boton.Padding.Horizontal, dentro.Height - boton.Padding.Vertical);
            TextRenderer.DrawText(e.Graphics, boton.Text, boton.Font, texto, LetraSecundario,
                Alineacion(boton.TextAlign) | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis
                | (boton.UseMnemonic ? TextFormatFlags.HidePrefix : TextFormatFlags.NoPrefix));
        }

        private static TextFormatFlags Alineacion(ContentAlignment alineacion)
        {
            TextFormatFlags horizontal;
            switch (alineacion)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.BottomLeft:
                    horizontal = TextFormatFlags.Left;
                    break;
                case ContentAlignment.TopRight:
                case ContentAlignment.MiddleRight:
                case ContentAlignment.BottomRight:
                    horizontal = TextFormatFlags.Right;
                    break;
                default:
                    horizontal = TextFormatFlags.HorizontalCenter;
                    break;
            }

            switch (alineacion)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.TopCenter:
                case ContentAlignment.TopRight:
                    return horizontal | TextFormatFlags.Top;
                case ContentAlignment.BottomLeft:
                case ContentAlignment.BottomCenter:
                case ContentAlignment.BottomRight:
                    return horizontal | TextFormatFlags.Bottom;
                default:
                    return horizontal | TextFormatFlags.VerticalCenter;
            }
        }
    }
}
