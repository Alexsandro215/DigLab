using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// "Lista de asistencia: Redes (5A)" desde la ficha del docente (2026-10-31): la hoja y el
    /// tablero de la clase entera, como la ve su docente en Mis grupos, SOLO PARA VER. La lista
    /// sale de la misma lectura que la ficha (LecturaEstadisticas.Listas: AsistenciaDatos.Calcular,
    /// el calculo de Mis grupos), sin otra consulta. La hoja va con PuedeCambiar en false (no se
    /// puede quitar ni dar asistencias) y en tercera persona ("Su docente le dio..."). Nunca se usa
    /// VentanaMisGrupos con otro id: escribiria a nombre del docente. "Copiar la lista" y "Guardar
    /// en Excel..." no existen en kiosco y, para la persona responsable del laboratorio,
    /// salen apagados con su motivo y la hoja no se copia con Ctrl+C (los nombres solo se ven en
    /// pantalla). El tablero y la hoja son de la hora de la lectura de la ficha (lo dice el subtitulo).
    /// </summary>
    internal sealed class VentanaListaDeClase : Form
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Color TextoGris = Color.FromArgb(105, 105, 105);

        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 16F, FontStyle.Bold);
        private readonly DrawingFont fuenteSubtitulo = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuenteAviso = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 15000 };
        private readonly Label lblTitulo;
        private readonly Label lblSubtitulo;
        private readonly Label lblAviso;
        private readonly BotonCabecera btnCopiar;
        private readonly BotonCabecera btnCsv;
        private readonly BotonCabecera btnCerrar;
        private readonly TableroAsistencia tablero;
        private readonly HojaAsistencia hoja;
        private readonly AsistenciaDeClase lista;
        private readonly FilaClase clase;
        private readonly string docente;
        private readonly DateTime ahoraServidor;
        private readonly bool listo;

        public VentanaListaDeClase(LecturaEstadisticas lectura, FilaClase clase, string docente)
        {
            if (lectura == null || clase == null)
            {
                throw new ArgumentNullException(lectura == null ? "lectura" : "clase");
            }

            this.clase = clase;
            this.docente = docente ?? "";
            lectura.Listas.TryGetValue(clase.ClaseId, out lista);
            // La hora de la lectura y no la de ahora: la lista es de esa lectura (sin F5 en la ficha, "hoy
            // en curso" con sus pendientes) y el tablero, con la hora de ahora, daba la clase por terminada.
            ahoraServidor = lectura.AhoraServidor;
            DateTime ahora = DateTime.Now + VentanaEstadisticas.Desfase(lectura);

            BackColor = Color.White;
            Text = "Lista de asistencia: " + clase.Materia + " (" + clase.Grupo + ")" + (Permisos.SoloConsulta ? " (solo consulta)" : "");
            Icon = (Icon)new System.ComponentModel.ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            ShowInTaskbar = false;
            KeyPreview = true;

            lblTitulo = new Label { AutoSize = false, AutoEllipsis = true, Font = fuenteTitulo, ForeColor = EstiloHorarios.VerdeTeschi, UseMnemonic = false, Text = "Lista de asistencia: " + clase.Materia + " (" + clase.Grupo + ")" };
            lblSubtitulo = new Label { AutoSize = false, AutoEllipsis = true, Font = fuenteSubtitulo, ForeColor = EstiloAdmin.TextoSecundario, UseMnemonic = false };
            lblAviso = new Label { AutoSize = false, AutoEllipsis = true, Font = fuenteAviso, ForeColor = Color.DarkGreen, UseMnemonic = false, TextAlign = ContentAlignment.MiddleRight };
            lblSubtitulo.Text = "Docente: " + this.docente + ". Toda la clase, " + Periodo() + ", como la ve su docente en Mis grupos. Solo para ver. Leído "
                + (ahoraServidor.Date == ahora.Date ? "" : "el " + EstadisticasDatos.Fecha(ahoraServidor.Date) + " ") + "a las " + ahoraServidor.ToString("HH:mm", Inv)
                + " (hora del servidor).";
            ayuda.SetToolTip(lblSubtitulo, lblSubtitulo.Text);

            btnCerrar = new BotonCabecera("Cerrar") { TabIndex = 4 };
            btnCerrar.Click += (s, e) => Close();
            CancelButton = btnCerrar;
            // Los nombres solo se ven en pantalla: la responsable no se los lleva (como el
            // CSV de Estadisticas). Nunca puede, asi que no ve los botones (apagados
            // siempre parecian descompuestos); en kiosco no hay archivos.
            btnCopiar = new BotonCabecera("Copiar la lista") { TabIndex = 2, Visible = PuedeLlevarse() };
            btnCopiar.Click += (s, e) => Copiar();
            ayuda.SetToolTip(btnCopiar, "Copia la hoja entera para pegarla en Excel (Ctrl+V).");
            btnCsv = new BotonCabecera("Guardar en Excel...") { TabIndex = 3, Visible = PuedeLlevarse() };
            btnCsv.Click += (s, e) => GuardarCsv();
            ayuda.SetToolTip(btnCsv, "Guarda la hoja entera como libro de Excel, con sus colores, lista para imprimir. También se puede guardar como CSV.");

            tablero = new TableroAsistencia { TabIndex = 0, EnTerceraPersona = true };
            // La responsable tampoco se la lleva con Ctrl+A y Ctrl+C (SinCopiar), no solo sin el boton.
            hoja = new HojaAsistencia
            {
                TabIndex = 1,
                EnTerceraPersona = true,
                PuedeCambiar = false,
                ConComoCopiar = !Kiosco.Activo && !Permisos.SoloConsulta,
                SinCopiar = Kiosco.Activo || Permisos.SoloConsulta
            };
            tablero.TarjetaPulsada += TarjetaPulsada;
            hoja.MarcaQuitada += () =>
            {
                if (tablero.Marcada.HasValue)
                {
                    tablero.Marcada = null;
                }
            };

            Controls.AddRange(new Control[] { lblTitulo, lblSubtitulo, lblAviso, btnCopiar, btnCsv, btnCerrar, tablero, hoja });

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            MinimumSize = new Size(Math.Min(1000, area.Width - 20), Math.Min(560, area.Height - 20));
            ClientSize = new Size(Math.Min(1340, area.Width - 60), Math.Min(800, area.Height - 60));
            StartPosition = FormStartPosition.CenterParent;
            // A pantalla completa, como Mis grupos.
            WindowState = FormWindowState.Maximized;

            // Primero con su tamano: la hoja se desplaza a sus clases mas recientes al llenarse.
            listo = true;
            Acomodar();
            if (lista == null)
            {
                tablero.MostrarError("Esta clase no tiene lista en lo leído: actualiza la ficha del docente.");
            }
            else
            {
                hoja.Mostrar(lista, this.docente);
                tablero.Mostrar(ResumenAsistencia.De(lista), BloquesDeHoy(lectura, clase.ClaseId), ahoraServidor.TimeOfDay);
            }

            ActiveControl = hoja;
        }

        /// <summary>La hoja que se ve (para las pruebas).</summary>
        internal HojaAsistencia Hoja
        {
            get { return hoja; }
        }

        internal TableroAsistencia Tablero
        {
            get { return tablero; }
        }

        /// <summary>"del 30/09/2026 a hoy" (o "al ..." si su ultima clase fue antes).</summary>
        private string Periodo()
        {
            if (lista == null)
            {
                return "en su periodo";
            }

            DateTime? ultima = lista.Dias.Count == 0 ? (DateTime?)null : lista.Dias[lista.Dias.Count - 1].Fecha;
            return ultima == null ? "desde el " + EstadisticasDatos.Fecha(lista.VigenteDesde)
                : "del " + EstadisticasDatos.Fecha(lista.VigenteDesde) + (ultima.Value == lista.AhoraServidor.Date ? " a hoy" : " al " + EstadisticasDatos.Fecha(ultima.Value));
        }

        /// <summary>
        /// Sus bloques de hoy en el calendario de la lectura (no cancelados y utilizables), los
        /// mismos que Mis grupos saca de su agenda y que usa la lista (EstadisticasDatos.TramosDeHoy).
        /// </summary>
        internal static List<TramoHorario> BloquesDeHoy(LecturaEstadisticas lectura, int claseId)
        {
            return lectura.Calendario.Clases
                .Where(c => c.ClaseId == claseId && c.Fecha == lectura.Hoy && !c.Cancelada && c.Utilizable)
                .OrderBy(c => c.Inicio)
                .Select(c => new TramoHorario(c.Inicio, c.Fin))
                .ToList();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Acomodar();
        }

        /// <summary>Como Mis grupos: titulo y botones arriba, el tablero y, debajo, la hoja hasta abajo.</summary>
        private void Acomodar()
        {
            Size cliente = ClientSize;
            if (!listo || WindowState == FormWindowState.Minimized || cliente.Width <= 0 || cliente.Height <= 0)
            {
                return;
            }

            bool amplio = cliente.Width >= 1600 && cliente.Height >= 860;
            int margen = amplio ? 22 : 16;
            int yBotones = amplio ? 12 : 8;
            btnCerrar.Location = new Point(cliente.Width - margen - btnCerrar.Width, yBotones);
            btnCsv.Location = new Point(btnCerrar.Left - 8 - btnCsv.Width, yBotones);
            btnCopiar.Location = new Point(btnCsv.Left - 8 - btnCopiar.Width, yBotones);
            // De PuedeLlevarse y no de Visible (falso mientras la ventana no se ve).
            int finTitulo = (PuedeLlevarse() ? btnCopiar.Left : btnCerrar.Left) - 16;
            // Una materia larga no llega a los botones: se corta con "..." (entera en la barra de titulo).
            Size pide = lblTitulo.PreferredSize;
            lblTitulo.SetBounds(margen, amplio ? 12 : 8, Math.Max(100, Math.Min(pide.Width, finTitulo - margen)), pide.Height);
            lblAviso.SetBounds(Math.Max(margen, finTitulo - 420), yBotones + btnCerrar.Height + 2, Math.Min(420, finTitulo - margen), 20);
            // El aviso (tras copiar o guardar) solo ocupa su lugar con texto: si no, el subtitulo llega hasta los botones.
            bool conAviso = !string.IsNullOrEmpty(lblAviso.Text);
            lblAviso.Visible = conAviso;
            int ySub = lblTitulo.Bottom + 2;
            int finSub = conAviso ? lblAviso.Left - 8 : cliente.Width - margen;
            lblSubtitulo.SetBounds(margen, ySub, Math.Max(100, finSub - margen), 20);
            int arriba = Math.Max(ySub + 26, yBotones + btnCerrar.Height + 24);
            int altoTablero = amplio ? 108 : 84;
            tablero.Amplio = amplio;
            tablero.SetBounds(margen, arriba, cliente.Width - 2 * margen, altoTablero);
            int yHoja = arriba + altoTablero + (amplio ? 16 : 10);
            hoja.SetBounds(margen, yHoja, cliente.Width - 2 * margen, Math.Max(0, cliente.Height - (amplio ? 12 : 8) - yHoja));
        }

        /// <summary>Una tarjeta del tablero: marca a sus alumnos en la hoja; otra vez, quita la marca (como Mis grupos).</summary>
        private void TarjetaPulsada(int tarjeta)
        {
            if (tablero.Marcada == tarjeta)
            {
                hoja.QuitarMarca();
                tablero.Marcada = null;
                return;
            }

            IList<int> ids = tablero.IdsDe(tarjeta);
            if (ids == null || ids.Count == 0)
            {
                if (tablero.Marcada.HasValue)
                {
                    tablero.Marcada = null;
                    hoja.QuitarMarca();
                }

                return;
            }

            hoja.MarcarAlumnos(ids);
            tablero.Marcada = tarjeta;
        }

        private void Avisar(string texto, Color color)
        {
            lblAviso.ForeColor = color;
            lblAviso.Text = texto;
            ayuda.SetToolTip(lblAviso, texto);
            Acomodar();
        }

        private bool PuedeLlevarse()
        {
            return !Kiosco.Activo && !Permisos.SoloConsulta;
        }

        private void Copiar()
        {
            if (!PuedeLlevarse())
            {
                return;
            }

            string detalle;
            HojaAsistencia.Copia copia;
            try
            {
                copia = hoja.CopiarTodo(out detalle);
            }
            catch (Exception ex)
            {
                Log.Error("Ficha del docente: copiar la lista de la clase " + clase.ClaseId, ex);
                copia = HojaAsistencia.Copia.Fallo;
                detalle = MensajeDeFallo.Explicar("No se pudo copiar la lista", ex, MensajeDeFallo.RevisaElRegistro);
            }

            switch (copia)
            {
                case HojaAsistencia.Copia.Hecha:
                    Avisar("Lista copiada: pégala en Excel con Ctrl+V.", Color.DarkGreen);
                    break;
                case HojaAsistencia.Copia.SinAlumnos:
                case HojaAsistencia.Copia.SinLista:
                    Avisar("Esta clase aún no tiene estudiantes en su lista.", TextoGris);
                    break;
                default:
                    Avisar(detalle ?? "No se pudo copiar la lista.", HojaAsistencia.FaltaTexto);
                    break;
            }
        }

        private void GuardarCsv()
        {
            if (!PuedeLlevarse())
            {
                return;
            }

            HojaAsistencia.Copia como;
            if (hoja.ComoExcel(out como) == null)
            {
                Avisar("Esta clase aún no tiene estudiantes en su lista.", TextoGris);
                return;
            }

            string nombre = "Lista de asistencia " + clase.Grupo + " " + clase.Materia + " " + ahoraServidor.ToString("yyyy-MM-dd", Inv);
            foreach (char invalido in Path.GetInvalidFileNameChars())
            {
                nombre = nombre.Replace(invalido, '-');
            }

            string ruta;
            using (SaveFileDialog dialogo = new SaveFileDialog
            {
                Title = "Guardar la lista de asistencia en Excel",
                Filter = HojaAsistencia.FiltroGuardar,
                FilterIndex = 1,
                DefaultExt = "xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = nombre.Trim(),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                ruta = dialogo.FileName;
            }

            bool comoCsv;
            try
            {
                comoCsv = hoja.GuardarEn(ruta, out como);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                || ex is NotSupportedException || ex is ArgumentException)
            {
                Log.Error("Ficha del docente: guardar en Excel la lista de la clase " + clase.ClaseId, ex);
                MessageBox.Show(this, VisorPdf.ExplicarArchivo("No se pudo guardar la lista", ex, "Excel"), "Guardar en Excel",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log.Info("Ficha del docente: lista de la clase " + clase.ClaseId + " guardada " + (comoCsv ? "en CSV." : "como libro de Excel."));
            Avisar("Guardada: ábrela con doble clic, se abre en Excel.", Color.DarkGreen);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (Kiosco.Activo)
            {
                VentanaEstadisticas.VaciarPortapapelesDeDigLab();
            }

            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                ayuda.Dispose();
                fuenteTitulo.Dispose();
                fuenteSubtitulo.Dispose();
                fuenteAviso.Dispose();
            }
        }
    }
}
