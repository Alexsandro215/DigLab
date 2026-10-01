using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// El codigo de clase del profesor en grande y deletreado: es lo que dicta a
    /// sus alumnos, uno para todas sus clases (db/09). En una base sin la 09
    /// cada clase tiene el suyo y se listan.
    /// </summary>
    internal class TarjetaCodigo : Control
    {
        private const float TamanoCodigo = 60F;
        private const int Relleno = 22;

        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 13F, FontStyle.Bold);
        private readonly DrawingFont fuenteTexto = new DrawingFont("Century Gothic", 10.5F);
        private readonly DrawingFont fuenteSinCodigo = new DrawingFont("Century Gothic", 24F, FontStyle.Bold);
        private readonly DrawingFont fuenteCodigoLista = new DrawingFont("Consolas", 16F, FontStyle.Bold);
        private PanoramaProfesor panorama;

        public TarjetaCodigo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
        }

        public void Mostrar(PanoramaProfesor datos)
        {
            panorama = datos;
            Invalidate();
        }

        /// <summary>"O mayúscula, 6, 6, I mayúscula, w minúscula": al dictarlo no se confunde la O con el 0 ni la I con la l.</summary>
        internal static string Deletrear(string codigo)
        {
            List<string> partes = new List<string>();
            foreach (char c in codigo)
            {
                partes.Add(char.IsLetter(c) ? c + (char.IsUpper(c) ? " mayúscula" : " minúscula") : c.ToString());
            }

            return string.Join(", ", partes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteTitulo.Dispose();
                fuenteTexto.Dispose();
                fuenteSinCodigo.Dispose();
                fuenteCodigoLista.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle caja = new Rectangle(Resplandor.Margen + 1, Resplandor.Margen + 1,
                Width - 2 * Resplandor.Margen - 3, Height - 2 * Resplandor.Margen - 3);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, 12))
            using (Pen borde = new Pen(Color.DarkGreen, 2f))
            {
                g.DrawPath(borde, forma);
            }

            Rectangle interior = Rectangle.Inflate(caja, -Relleno, -18);
            int y = interior.Top;
            Texto(g, "Tu código de clase", fuenteTitulo, Color.DarkGreen, interior, ref y, false);
            if (panorama == null)
            {
                Texto(g, "Cargando...", fuenteTexto, Color.Gray, interior, ref y, false);
                return;
            }

            if (!panorama.CodigoPorProfesor)
            {
                DibujarLista(g, interior, y);
                return;
            }

            if (string.IsNullOrEmpty(panorama.Codigo))
            {
                y += 24;
                Texto(g, "Sin código", fuenteSinCodigo, Color.Gray, interior, ref y, false);
                y += 10;
                Texto(g, "Todavía no tienes código de clase. Te lo asigna el administrador en Profesores, o se crea al darte de alta tu primera clase.",
                    fuenteTexto, Color.FromArgb(70, 70, 70), interior, ref y, true);
                return;
            }

            // El codigo, centrado en el espacio libre y lo mas grande que quepa.
            int altoTextos = 150;
            Rectangle zonaCodigo = new Rectangle(interior.Left, y, interior.Width, Math.Max(90, interior.Bottom - y - altoTextos));
            DibujarCodigo(g, panorama.Codigo, zonaCodigo);

            y = zonaCodigo.Bottom + 6;
            Texto(g, Deletrear(panorama.Codigo), fuenteTexto, Color.FromArgb(90, 90, 90), interior, ref y, true);
            y += 12;
            Texto(g, "Díctalo a tus alumnos al empezar la clase. Es el mismo para todas tus clases y solo se acepta en el laboratorio "
                + "y el horario de cada una, desde " + HorariosDatos.MargenAntesDelInicioMinutos + " minutos antes de empezar.",
                fuenteTexto, Color.FromArgb(50, 50, 50), interior, ref y, true);
        }

        /// <summary>Letra por letra, con aire entre ellas, en una fuente donde la O, el 0, la I y la l no se parecen.</summary>
        private static void DibujarCodigo(Graphics g, string codigo, Rectangle zona)
        {
            float tamano = TamanoCodigo;
            for (int intento = 0; intento < 8; intento++)
            {
                using (DrawingFont fuente = new DrawingFont("Consolas", tamano, FontStyle.Bold))
                {
                    int espacio = (int)(tamano / 5);
                    Size[] medidas = codigo.Select(c => TextRenderer.MeasureText(g, c.ToString(), fuente, Size.Empty, TextFormatFlags.NoPadding)).ToArray();
                    int total = medidas.Sum(m => m.Width) + espacio * (codigo.Length - 1);
                    int alto = medidas.Length == 0 ? 0 : medidas.Max(m => m.Height);
                    if ((total <= zona.Width && alto <= zona.Height) || intento == 7)
                    {
                        int x = zona.Left + (zona.Width - total) / 2;
                        int y = zona.Top + (zona.Height - alto) / 2;
                        for (int i = 0; i < codigo.Length; i++)
                        {
                            TextRenderer.DrawText(g, codigo[i].ToString(), fuente, new Point(x, y), Color.DarkGreen, TextFormatFlags.NoPadding);
                            x += medidas[i].Width + espacio;
                        }

                        return;
                    }
                }

                tamano *= 0.8f;
            }
        }

        /// <summary>Sin la migracion 09: un codigo por clase.</summary>
        private void DibujarLista(Graphics g, Rectangle interior, int y)
        {
            y += 8;
            if (panorama.Clases.Count == 0)
            {
                Texto(g, "No tienes clases vigentes. El administrador crea las clases y su código.", fuenteTexto, Color.FromArgb(70, 70, 70), interior, ref y, true);
                return;
            }

            Texto(g, "Cada clase tiene el suyo; díctale a cada grupo el de su clase.", fuenteTexto, Color.FromArgb(70, 70, 70), interior, ref y, true);
            y += 8;
            foreach (ClaseInfo clase in panorama.Clases)
            {
                int altoCodigo = TextRenderer.MeasureText(g, "Ág", fuenteCodigoLista).Height;
                if (y + altoCodigo > interior.Bottom)
                {
                    break;
                }

                TextRenderer.DrawText(g, clase.Codigo, fuenteCodigoLista, new Point(interior.Left, y), Color.DarkGreen);
                TextRenderer.DrawText(g, clase.Materia + ", " + clase.Grupo, fuenteTexto,
                    new Rectangle(interior.Left + 130, y + 4, interior.Width - 130, altoCodigo), Color.FromArgb(50, 50, 50),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                y += altoCodigo + 4;
            }
        }

        private static void Texto(Graphics g, string texto, DrawingFont fuente, Color color, Rectangle interior, ref int y, bool variasLineas)
        {
            if (y >= interior.Bottom)
            {
                return;
            }

            TextFormatFlags formato = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix
                | (variasLineas ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Size medida = TextRenderer.MeasureText(g, texto, fuente, new Size(interior.Width, int.MaxValue), formato);
            TextRenderer.DrawText(g, texto, fuente, new Rectangle(interior.Left, y, interior.Width, Math.Min(medida.Height, interior.Bottom - y)), color, formato);
            y += medida.Height;
        }
    }

    /// <summary>
    /// La clase del profesor ahora mismo (con su tiempo restante y cuantos han
    /// registrado bitacora) o, si no hay, la proxima: hoy o, segun su horario,
    /// otro dia. Mientras hay una clase en curso la tarjeta lleva el halo
    /// animado del panel del administrador (Resplandor).
    /// </summary>
    internal class TarjetaAhora : Control
    {
        private const int Relleno = 22;
        private static readonly string[] Dias = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
        private static readonly Color TextoPrincipal = Color.FromArgb(30, 30, 30);
        private static readonly Color TextoSecundario = Color.FromArgb(75, 75, 75);
        private static readonly Color Naranja = Color.FromArgb(190, 85, 0);

        private readonly DrawingFont fuenteEtiqueta = new DrawingFont("Century Gothic", 13F, FontStyle.Bold);
        private readonly DrawingFont fuenteMateria = new DrawingFont("Century Gothic", 20F, FontStyle.Bold);
        private readonly DrawingFont fuenteNormal = new DrawingFont("Century Gothic", 11F);
        private readonly DrawingFont fuenteEstado = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
        private readonly DrawingFont fuentePequena = new DrawingFont("Century Gothic", 10F);
        private readonly Button btnVer;
        private readonly Timer animacion = new Timer { Interval = Resplandor.IntervaloMs };

        private PanoramaProfesor panorama;
        private DateTime ahora;

        // Lo que se muestra, recalculado con cada segundo del reloj.
        private SesionAgenda destacada;
        private bool enCurso;
        private bool pronto;

        /// <summary>"Ver quién registró" en la clase que muestra la tarjeta.</summary>
        public event Action<SesionAgenda> VerRegistro;

        public TarjetaAhora()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;

            btnVer = EstiloAdmin.BotonPrincipal("Ver quién registró", Point.Empty, new Size(210, 42));
            btnVer.Visible = false;
            btnVer.Click += (s, e) =>
            {
                if (destacada != null && VerRegistro != null)
                {
                    VerRegistro(destacada);
                }
            };
            Controls.Add(btnVer);

            animacion.Tick += (s, e) =>
            {
                if (enCurso)
                {
                    Invalidate();
                }
            };
            animacion.Start();
        }

        /// <summary>Hora del servidor; la pone el panel cada segundo.</summary>
        public DateTime Ahora
        {
            set
            {
                ahora = value;
                Calcular();
                Invalidate();
            }
        }

        public void Mostrar(PanoramaProfesor datos)
        {
            panorama = datos;
            Calcular();
            Invalidate();
        }

        private Rectangle Caja
        {
            get
            {
                return new Rectangle(Resplandor.Margen + 1, Resplandor.Margen + 1,
                    Width - 2 * Resplandor.Margen - 3, Height - 2 * Resplandor.Margen - 3);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Rectangle caja = Caja;
            btnVer.Location = new Point(caja.Left + Relleno, caja.Bottom - 18 - btnVer.Height);
        }

        /// <summary>Sus clases de hoy que no estan canceladas, por hora.</summary>
        private List<SesionAgenda> Activas()
        {
            return panorama == null || panorama.AhoraServidor.Date != ahora.Date
                ? new List<SesionAgenda>()
                : panorama.Hoy.Where(s => !s.Cancelada).OrderBy(s => s.Inicio).ToList();
        }

        /// <summary>
        /// La clase en curso; si no, la que ya acepta el codigo (15 min antes);
        /// si no, la siguiente de hoy; y si ya no queda ninguna, la ultima que
        /// termino hoy, para poder revisar su bitacora.
        /// </summary>
        private void Calcular()
        {
            TimeSpan hora = ahora.TimeOfDay;
            TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            List<SesionAgenda> activas = Activas();

            SesionAgenda enCursoAhora = activas.FirstOrDefault(s => s.Inicio <= hora && hora < s.Fin);
            SesionAgenda prontoAhora = activas.FirstOrDefault(s => hora < s.Inicio && hora >= s.Inicio - margen);
            SesionAgenda siguiente = activas.FirstOrDefault(s => s.Inicio > hora);
            SesionAgenda ultima = activas.LastOrDefault(s => s.Fin <= hora);

            destacada = enCursoAhora ?? prontoAhora ?? siguiente ?? ultima;
            enCurso = enCursoAhora != null;
            pronto = enCursoAhora == null && prontoAhora != null;

            // Solo tiene sentido ver quien registro si ya se puede registrar o ya paso.
            bool verVisible = destacada != null && (enCurso || pronto || destacada.Fin <= hora);
            if (btnVer.Visible != verVisible)
            {
                btnVer.Visible = verVisible;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animacion.Dispose();
                fuenteEtiqueta.Dispose();
                fuenteMateria.Dispose();
                fuenteNormal.Dispose();
                fuenteEstado.Dispose();
                fuentePequena.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle caja = Caja;
            if (enCurso)
            {
                Resplandor.Dibujar(g, caja, 12);
            }

            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, 12))
            {
                using (SolidBrush fondo = new SolidBrush(Color.White))
                {
                    g.FillPath(fondo, forma);
                }

                using (Pen borde = new Pen(enCurso ? Resplandor.Borde() : Color.DarkGreen, enCurso ? 3f : 2f))
                {
                    g.DrawPath(borde, forma);
                }
            }

            Rectangle interior = Rectangle.Inflate(caja, -Relleno, -18);
            // Lo de abajo del todo (mas tarde hoy, canceladas) no se mete debajo del boton.
            int limite = btnVer.Visible ? btnVer.Top - 10 : interior.Bottom;
            int y = interior.Top;
            if (panorama == null)
            {
                Linea(g, "Tu clase", fuenteEtiqueta, Color.DarkGreen, interior, limite, ref y);
                Linea(g, "Cargando...", fuenteNormal, Color.Gray, interior, limite, ref y);
                return;
            }

            TimeSpan hora = ahora.TimeOfDay;
            List<SesionAgenda> activas = Activas();
            if (destacada == null || destacada.Fin <= hora)
            {
                DibujarSinClase(g, interior, limite, ref y, activas);
            }
            else
            {
                DibujarClase(g, interior, limite, ref y, hora);
            }

            // Lo demas de hoy: las que vienen despues y las canceladas.
            List<string> despues = activas
                .Where(s => destacada != null && s != destacada && s.Inicio >= destacada.Fin)
                .Select(s => HorariosDatos.Hora(s.Inicio) + " – " + HorariosDatos.Hora(s.Fin) + "  " + s.Materia + ", " + s.Grupo
                    + LaboratorioEntre(s.LaboratorioId))
                .ToList();
            List<string> canceladas = panorama.AhoraServidor.Date != ahora.Date ? new List<string>() : panorama.Hoy
                .Where(s => s.Cancelada)
                .Select(s => HorariosDatos.Hora(s.Inicio) + "  " + s.Materia + ", " + s.Grupo + (string.IsNullOrEmpty(s.Motivo) ? "" : ". Motivo: " + s.Motivo))
                .ToList();

            int alto = TextRenderer.MeasureText(g, "Ág", fuentePequena).Height;
            int lineas = (despues.Count > 0 ? despues.Count + 1 : 0) + (canceladas.Count > 0 ? canceladas.Count + 1 : 0);
            int yAbajo = Math.Max(y + 12, limite - lineas * alto);
            if (despues.Count > 0)
            {
                Linea(g, "Más tarde hoy", fuenteEstado, Color.DarkGreen, interior, limite, ref yAbajo);
                foreach (string texto in despues)
                {
                    Linea(g, texto, fuentePequena, TextoSecundario, interior, limite, ref yAbajo);
                }
            }

            if (canceladas.Count > 0)
            {
                Linea(g, canceladas.Count == 1 ? "Cancelada hoy" : "Canceladas hoy", fuenteEstado, Color.Gray, interior, limite, ref yAbajo);
                foreach (string texto in canceladas)
                {
                    Linea(g, texto, fuentePequena, Color.Gray, interior, limite, ref yAbajo);
                }
            }
        }

        private void DibujarClase(Graphics g, Rectangle interior, int limite, ref int y, TimeSpan hora)
        {
            SesionAgenda s = destacada;
            string etiqueta = enCurso ? "Ahora" : pronto ? "Empieza pronto" : "Tu próxima clase hoy";
            Linea(g, etiqueta, fuenteEtiqueta, Color.DarkGreen, interior, limite, ref y);
            y += 4;
            Linea(g, s.Materia, fuenteMateria, TextoPrincipal, interior, limite, ref y);
            Linea(g, s.Grupo + EnLaboratorio(panorama.Laboratorio(s.LaboratorioId)), fuenteNormal, TextoSecundario, interior, limite, ref y);

            string horario = HorariosDatos.Hora(s.Inicio) + " – " + HorariosDatos.Hora(s.Fin) + (s.EsExtra ? " (sesión extra)" : "");
            y += 6;
            if (enCurso)
            {
                Linea(g, horario + ". Quedan " + PanelLaboratoriosAhora.Duracion(s.Fin - hora), fuenteEstado, Color.DarkGreen, interior, limite, ref y);
                y += 4;
                double total = (s.Fin - s.Inicio).TotalSeconds;
                double avance = total <= 0 ? 1 : Math.Max(0, Math.Min(1, (hora - s.Inicio).TotalSeconds / total));
                Rectangle barra = new Rectangle(interior.Left, y, interior.Width, 8);
                using (SolidBrush vacio = new SolidBrush(Color.FromArgb(225, 235, 225)))
                using (SolidBrush lleno = new SolidBrush(Color.DarkGreen))
                {
                    g.FillRectangle(vacio, barra);
                    g.FillRectangle(lleno, new Rectangle(barra.X, barra.Y, (int)(barra.Width * avance), barra.Height));
                }

                y += barra.Height + 8;
            }
            else if (pronto)
            {
                Linea(g, horario + ". Empieza en " + PanelLaboratoriosAhora.Duracion(s.Inicio - hora) + "; ya se acepta tu código.",
                    fuenteEstado, Color.DarkGreen, interior, limite, ref y);
            }
            else
            {
                TimeSpan desde = s.Inicio - TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
                Linea(g, horario + ". Empieza en " + PanelLaboratoriosAhora.Duracion(s.Inicio - hora) + ".", fuenteEstado, Color.DarkGreen, interior, limite, ref y);
                Linea(g, "Tu código se acepta desde las " + HorariosDatos.Hora(desde) + ".", fuenteNormal, TextoSecundario, interior, limite, ref y);
            }

            if (enCurso || pronto)
            {
                y += 4;
                DibujarRegistro(g, s, interior, limite, ref y, true);
            }
        }

        /// <summary>No queda clase hoy: lo que ya dio (con su bitacora) y su proxima clase otro dia.</summary>
        private void DibujarSinClase(Graphics g, Rectangle interior, int limite, ref int y, List<SesionAgenda> activas)
        {
            Linea(g, "Hoy", fuenteEtiqueta, Color.DarkGreen, interior, limite, ref y);
            y += 4;
            Linea(g, activas.Count == 0 ? "Hoy no tienes clases" : "Ya terminaste tus clases de hoy", fuenteMateria, TextoPrincipal, interior, limite, ref y);

            if (destacada != null)
            {
                y += 4;
                Linea(g, "La última: " + destacada.Materia + ", " + destacada.Grupo + ", " + HorariosDatos.Hora(destacada.Inicio) + " – "
                    + HorariosDatos.Hora(destacada.Fin) + ".", fuenteNormal, TextoSecundario, interior, limite, ref y);
                DibujarRegistro(g, destacada, interior, limite, ref y, false);
            }

            string proxima = ProximaOtroDia();
            y += 10;
            Linea(g, proxima == null ? "No tienes clases en los próximos 7 días." : "Tu próxima clase es " + proxima,
                fuenteNormal, TextoSecundario, interior, limite, ref y, true);
        }

        private void DibujarRegistro(Graphics g, SesionAgenda s, Rectangle interior, int limite, ref int y, bool enMarcha)
        {
            if (s.Alumnos == 0)
            {
                Linea(g, enMarcha ? "Aún nadie ha registrado su bitácora." : "Nadie registró su bitácora.", fuenteEstado,
                    enMarcha ? Color.Gray : Naranja, interior, limite, ref y);
                return;
            }

            string texto = s.Alumnos + (s.Alumnos == 1 ? " alumno registró su bitácora" : " alumnos registraron su bitácora");
            if (s.ConFallas > 0)
            {
                Linea(g, texto + ", " + s.ConFallas + " con fallas.", fuenteEstado, Naranja, interior, limite, ref y);
            }
            else
            {
                Linea(g, texto + ", sin fallas.", fuenteEstado, Color.DarkGreen, interior, limite, ref y);
            }
        }

        /// <summary>
        /// La primera clase de su horario en los proximos 7 dias ("mañana
        /// 30/09 a las 08:00: ..."). Segun el horario semanal: no mira si ese dia
        /// hay una cancelacion.
        /// </summary>
        private string ProximaOtroDia()
        {
            for (int d = 1; d <= 7; d++)
            {
                DateTime dia = ahora.Date.AddDays(d);
                int diaSemana = ((int)dia.DayOfWeek + 6) % 7 + 1;
                FranjaInfo franja = panorama.Franjas
                    .Where(f => f.DiaSemana == diaSemana && f.Activa && dia >= f.VigenteDesde.Date && dia <= f.VigenteHasta.Date)
                    .OrderBy(f => f.Inicio)
                    .FirstOrDefault();
                if (franja != null)
                {
                    string laboratorio = franja.Laboratorio ?? "";
                    string donde = laboratorio.Length == 0 ? ""
                        : laboratorio.StartsWith("laboratorio", StringComparison.OrdinalIgnoreCase) ? " en el " + laboratorio
                        : " en el laboratorio " + laboratorio;
                    return (d == 1 ? "mañana" : "el " + Dias[(int)dia.DayOfWeek]) + " " + dia.ToString("dd/MM") + " a las " + HorariosDatos.Hora(franja.Inicio)
                        + ", " + franja.Materia + " (" + franja.Grupo + ")" + donde + ".";
                }
            }

            return null;
        }

        private string LaboratorioEntre(int id)
        {
            string nombre = panorama.Laboratorio(id);
            return nombre.Length == 0 ? "" : " (" + nombre + ")";
        }

        /// <summary>", laboratorio CISCO"; si el nombre ya empieza por "Laboratorio", sin repetirlo.</summary>
        private static string EnLaboratorio(string nombre)
        {
            if (nombre.Length == 0)
            {
                return "";
            }

            return nombre.StartsWith("laboratorio", StringComparison.OrdinalIgnoreCase) ? ", " + nombre : ", laboratorio " + nombre;
        }

        private static void Linea(Graphics g, string texto, DrawingFont fuente, Color color, Rectangle interior, int limite, ref int y, bool variasLineas = false)
        {
            TextFormatFlags formato = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix
                | (variasLineas ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Size medida = TextRenderer.MeasureText(g, texto, fuente, new Size(interior.Width, int.MaxValue), formato);
            if (y + Math.Min(medida.Height, TextRenderer.MeasureText(g, "Ág", fuente).Height) > limite)
            {
                return;
            }

            TextRenderer.DrawText(g, texto, fuente, new Rectangle(interior.Left, y, interior.Width, Math.Min(medida.Height, limite - y)), color, formato);
            y += medida.Height;
        }
    }
}
