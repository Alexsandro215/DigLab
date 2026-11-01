using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;

namespace DigBit
{
    /// <summary>
    /// El "Periodo" de Estadisticas y de la ficha del docente (2026-10-31): una sola pieza
    /// para las dos. Estadisticas llena su combo con Llenar y pide las fechas con
    /// PedirFechas (sus campos y su comportamiento no cambian); la ficha usa el control
    /// entero (la etiqueta "Periodo" y el combo), que recuerda la opcion de antes para volver
    /// a ella si se cancela "Elegir fechas...". Las opciones son las de
    /// EstadisticasDatos.OpcionesDePeriodo: "Este ciclo", cada ciclo que ya termino, cada
    /// plantilla de horario aplicada (en su bloque, entre dos rayas), "Este mes", "Esta
    /// semana", "Ultimos 30 dias", las fechas elegidas y "Elegir fechas...".
    /// </summary>
    internal sealed class SelectorDePeriodo : Control
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly Label etiqueta;
        private readonly ComboBox combo;
        private readonly ToolTip ayuda;
        private PeriodoElegido periodo;
        private int antes = -1;
        private bool poniendo;

        /// <summary>Se eligio otro periodo (tambien unas fechas): quien lo usa vuelve a leer.</summary>
        public event Action<PeriodoElegido> Cambio;

        public SelectorDePeriodo(PeriodoElegido inicial, ToolTip ayuda)
        {
            this.ayuda = ayuda;
            periodo = inicial ?? PeriodoElegido.De(TipoPeriodo.Ciclo);
            // Solo agrupa la etiqueta y el combo (como un Panel): sin esto era una parada invisible
            // del tabulador antes del combo (no pinta el foco y las flechas no hacen nada).
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.White;
            etiqueta = EstiloHorarios.CrearEtiqueta("Periodo", Point.Empty);
            combo = EstiloHorarios.CrearCombo(Point.Empty, 300);
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 20;
            combo.MaxDropDownItems = 20;
            combo.AccessibleName = "Periodo";
            combo.DrawItem += Dibujar;
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (!poniendo)
                {
                    Elegido();
                }
            };
            Controls.Add(etiqueta);
            Controls.Add(combo);
            poniendo = true;
            try
            {
                antes = Llenar(combo, null, null, periodo);
            }
            finally
            {
                poniendo = false;
            }

            Height = combo.Height;
        }

        /// <summary>Lo que se pide ahora.</summary>
        public PeriodoElegido Periodo
        {
            get { return periodo; }
        }

        public ComboBox Combo
        {
            get { return combo; }
        }

        /// <summary>Lo que mide la etiqueta y el hueco hasta el combo.</summary>
        public int AnchoEtiqueta
        {
            get { return etiqueta.PreferredSize.Width + 6; }
        }

        /// <summary>
        /// Despues de una lectura: lo pedido queda como lo que se ve (VentanaEstadisticas.PeriodoQueQueda,
        /// la misma regla) y el combo lleva las opciones de esa lectura, con su ayuda.
        /// </summary>
        public void Poner(LecturaEstadisticas leida, ResultadoEstadisticas r)
        {
            if (r != null)
            {
                periodo = VentanaEstadisticas.PeriodoQueQueda(periodo, r.Filtro);
            }

            poniendo = true;
            try
            {
                antes = Llenar(combo, leida, r, periodo);
            }
            finally
            {
                poniendo = false;
            }

            ayuda.SetToolTip(combo, r == null ? null : EstadisticasDatos.AyudaDelPeriodo(r, leida));
        }

        /// <summary>Otra lectura no se pudo hacer: lo pedido vuelve al periodo de lo que se sigue viendo.</summary>
        public void Volver(PeriodoElegido visto, LecturaEstadisticas leida, ResultadoEstadisticas r)
        {
            if (visto != null)
            {
                periodo = visto;
            }

            Poner(leida, r);
        }

        /// <summary>Para "Elegir fechas...": la lectura y lo que se ve (sus dias), y el desfase con el servidor.</summary>
        public Func<LecturaEstadisticas> LecturaVista;
        public Func<ResultadoEstadisticas> ResultadoVisto;

        private void Elegido()
        {
            VentanaEstadisticas.OpcionPeriodo opcion = combo.SelectedItem as VentanaEstadisticas.OpcionPeriodo;
            if (opcion == null)
            {
                return;
            }

            if (!opcion.ElegirFechas)
            {
                antes = combo.SelectedIndex;
                periodo = opcion.Periodo;
                if (Cambio != null)
                {
                    Cambio(periodo);
                }

                return;
            }

            LecturaEstadisticas leida = LecturaVista == null ? null : LecturaVista();
            ResultadoEstadisticas visto = ResultadoVisto == null ? null : ResultadoVisto();
            PeriodoElegido fechas = PedirFechas(FindForm(), leida, visto, periodo, VentanaEstadisticas.Desfase(leida));
            if (fechas == null)
            {
                poniendo = true;
                combo.SelectedIndex = antes >= 0 && antes < combo.Items.Count ? antes : 0;
                poniendo = false;
                return;
            }

            periodo = fechas;
            poniendo = true;
            try
            {
                // El combo ya dice las fechas mientras lee.
                antes = Llenar(combo, leida, null, periodo);
            }
            finally
            {
                poniendo = false;
            }

            if (Cambio != null)
            {
                Cambio(periodo);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Size e = etiqueta.PreferredSize;
            etiqueta.Location = new Point(0, Math.Max(0, (ClientSize.Height - e.Height) / 2));
            int x = AnchoEtiqueta;
            combo.SetBounds(x, 0, Math.Max(80, ClientSize.Width - x), combo.Height);
            AnchoDeLaLista(combo);
        }

        private static void AnchoDeLaLista(ComboBox combo)
        {
            int pide = 0;
            foreach (object opcion in combo.Items)
            {
                pide = Math.Max(pide, TextRenderer.MeasureText(Convert.ToString(opcion, Inv), combo.Font).Width);
            }

            pide += 14 + SystemInformation.VerticalScrollBarWidth;
            combo.DropDownWidth = Math.Max(combo.Width, Math.Min(pide, 640));
        }

        /// <summary>Una opcion, cortada con "..." si no cabe (entera en la lista y en la ayuda), como en Estadisticas.</summary>
        private static void Dibujar(object sender, DrawItemEventArgs e)
        {
            DibujarOpcion((ComboBox)sender, e);
        }

        /// <summary>
        /// Lo comun con Estadisticas (VentanaEstadisticas.DibujarOpcion): la opcion cortada con
        /// "..." si no cabe y, en la lista abierta, una raya gris encima de la que abre un bloque
        /// (OpcionPeriodo.AbreBloque: la primera plantilla aplicada y "Este mes" despues de ellas).
        /// </summary>
        internal static void DibujarOpcion(ComboBox combo, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index >= 0)
            {
                object item = combo.Items[e.Index];
                VentanaEstadisticas.OpcionPeriodo opcion = item as VentanaEstadisticas.OpcionPeriodo;
                if (opcion != null && opcion.AbreBloque && (e.State & DrawItemState.ComboBoxEdit) == 0)
                {
                    using (Pen raya = new Pen(Color.FromArgb(200, 206, 200)))
                    {
                        e.Graphics.DrawLine(raya, e.Bounds.Left + 3, e.Bounds.Top, e.Bounds.Right - 4, e.Bounds.Top);
                    }
                }

                TextRenderer.DrawText(e.Graphics, Convert.ToString(item, Inv), e.Font,
                    new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), e.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            e.DrawFocusRectangle();
        }

        // =====================================================================
        // Lo comun con Estadisticas
        // =====================================================================

        /// <summary>
        /// Las opciones del periodo en ese combo: "Este ciclo" (si hoy vale alguna clase), un
        /// renglon por cada ciclo que ya termino, un renglon por cada plantilla aplicada que ya
        /// empezo (en su bloque, entre dos rayas), "Este mes", "Esta semana", "Ultimos 30 dias",
        /// las fechas elegidas (si las hay) y "Elegir fechas..." al final. Elige lo que se ve (r;
        /// sin el, lo pedido) y devuelve su indice. Es lo que hacia VentanaEstadisticas.LlenarPeriodo.
        /// </summary>
        internal static int Llenar(ComboBox combo, LecturaEstadisticas leida, ResultadoEstadisticas r, PeriodoElegido periodo)
        {
            FiltroEstadisticas filtro = r == null ? null : r.Filtro;
            combo.BeginUpdate();
            combo.Items.Clear();
            foreach (PeriodoElegido p in EstadisticasDatos.OpcionesDePeriodo(leida))
            {
                combo.Items.Add(new VentanaEstadisticas.OpcionPeriodo { Periodo = p, Texto = EstadisticasDatos.TextoDePeriodo(p, leida) });
            }

            // Un ciclo pasado que aun no esta en la lista (el recordado, antes de la primera
            // lectura): con su nombre de cuando se eligio, justo despues de "Este ciclo".
            if (periodo.Tipo == TipoPeriodo.CicloPasado
                && !combo.Items.Cast<VentanaEstadisticas.OpcionPeriodo>().Any(o => o.Periodo.Tipo == TipoPeriodo.CicloPasado && o.Periodo.Desde == periodo.Desde))
            {
                int despues = combo.Items.Count > 0 && ((VentanaEstadisticas.OpcionPeriodo)combo.Items[0]).Periodo.Tipo == TipoPeriodo.Ciclo ? 1 : 0;
                combo.Items.Insert(despues, new VentanaEstadisticas.OpcionPeriodo { Periodo = periodo, Texto = EstadisticasDatos.TextoDePeriodo(periodo, null) });
            }

            // Lo mismo con una plantilla recordada: con su texto de cuando se eligio, al final del
            // bloque de las plantillas (antes de "Este mes"). Si al leer ya no esta, se ve "Este ciclo".
            if (periodo.Tipo == TipoPeriodo.Plantilla
                && !combo.Items.Cast<VentanaEstadisticas.OpcionPeriodo>().Any(o => o.Periodo.Tipo == TipoPeriodo.Plantilla && o.Periodo.Plantilla == periodo.Plantilla))
            {
                int mes = combo.Items.Cast<VentanaEstadisticas.OpcionPeriodo>().ToList().FindIndex(o => o.Periodo.Tipo == TipoPeriodo.Mes);
                combo.Items.Insert(mes < 0 ? combo.Items.Count : mes, new VentanaEstadisticas.OpcionPeriodo { Periodo = periodo, Texto = EstadisticasDatos.TextoDePeriodo(periodo, null) });
            }

            // El bloque de las plantillas: una raya encima de la primera y otra encima de lo que sigue.
            bool enElBloque = false;
            foreach (VentanaEstadisticas.OpcionPeriodo opcion in combo.Items)
            {
                bool esPlantilla = opcion.Periodo.Tipo == TipoPeriodo.Plantilla;
                opcion.AbreBloque = esPlantilla != enElBloque;
                enElBloque = esPlantilla;
            }

            // Las fechas de lo que se ve (sin nada calculado, las pedidas).
            TipoPeriodo visto = filtro != null ? filtro.Tipo : periodo.Tipo;
            if (visto == TipoPeriodo.Fechas)
            {
                combo.Items.Add(new VentanaEstadisticas.OpcionPeriodo
                {
                    Periodo = periodo,
                    Texto = filtro != null && !filtro.HastaHoy ? EstadisticasDatos.DelAl(filtro.Desde, filtro.Hasta) : EstadisticasDatos.TextoDePeriodo(periodo, leida)
                });
            }

            combo.Items.Add(new VentanaEstadisticas.OpcionPeriodo { ElegirFechas = true, Texto = "Elegir fechas..." });

            // Lo que se ve: el mismo tipo y, con un ciclo pasado, el mismo primer dia; con una
            // plantilla, la misma (por su idciclo).
            DateTime cicloVisto = filtro != null && filtro.Ciclo != null ? filtro.Ciclo.Desde : periodo.Desde;
            int plantillaVista = filtro != null && filtro.Plantilla != null ? filtro.Plantilla.Id : periodo.Plantilla;
            int indice = 0;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                VentanaEstadisticas.OpcionPeriodo opcion = (VentanaEstadisticas.OpcionPeriodo)combo.Items[i];
                if (!opcion.ElegirFechas && opcion.Periodo.Tipo == visto && (visto != TipoPeriodo.CicloPasado || opcion.Periodo.Desde == cicloVisto)
                    && (visto != TipoPeriodo.Plantilla || opcion.Periodo.Plantilla == plantillaVista))
                {
                    indice = i;
                    break;
                }
            }

            combo.SelectedIndex = indice;
            combo.EndUpdate();
            return indice;
        }

        /// <summary>
        /// "Elegir fechas...": el dialogo (con "Hasta hoy"), con los dias que de verdad cuentan
        /// en lo que se ve (o la ultima semana). Null si se cancela.
        /// </summary>
        internal static PeriodoElegido PedirFechas(IWin32Window dueno, LecturaEstadisticas lectura, ResultadoEstadisticas visto, PeriodoElegido periodo, TimeSpan desfase)
        {
            DateTime hoy = lectura != null ? lectura.Hoy : (DateTime.Now + desfase).Date;
            DateTime desde = visto != null ? visto.Desde : hoy.AddDays(-6);
            DateTime hasta = visto != null ? visto.Hasta : hoy;
            bool hastaHoy = periodo.Tipo == TipoPeriodo.Fechas && periodo.HastaHoy;
            using (VentanaEstadisticas.DialogoFechas dialogo = new VentanaEstadisticas.DialogoFechas(hoy, desde, hasta, hastaHoy))
            {
                if (dialogo.ShowDialog(dueno) != DialogResult.OK)
                {
                    return null;
                }

                return dialogo.HastaHoy ? PeriodoElegido.DesdeHastaHoy(dialogo.Desde) : PeriodoElegido.EntreFechas(dialogo.Desde, dialogo.Hasta);
            }
        }

        /// <summary>El mismo periodo pedido: mismo tipo y, con fechas o un ciclo pasado, mismos dias; con una plantilla, la misma.</summary>
        internal static bool Iguales(PeriodoElegido a, PeriodoElegido b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Tipo != b.Tipo)
            {
                return false;
            }

            switch (a.Tipo)
            {
                case TipoPeriodo.Fechas:
                    return a.Desde == b.Desde && a.HastaHoy == b.HastaHoy && (a.HastaHoy || a.Hasta == b.Hasta);
                case TipoPeriodo.CicloPasado:
                    return a.Desde == b.Desde;
                case TipoPeriodo.Plantilla:
                    return a.Plantilla == b.Plantilla;
                default:
                    return true;
            }
        }
    }
}
