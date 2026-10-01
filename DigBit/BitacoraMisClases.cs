using DigBit.conexion;
using DigBit.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// El horario semanal del profesor. Al pulsar una clase se abre su bitacora
    /// (BitacoraSesion) con quien registro en ella. Sustituye a la pantalla que
    /// pedia el codigo a mano, asi que un profesor solo ve sus propias clases.
    /// </summary>
    public class BitacoraMisClases : Form
    {
        private readonly Consultas consultas = new Consultas();
        private readonly HorariosDatos datos = new HorariosDatos();
        private int idProfesor;
        private string nombreProfesor = "";

        private Label lblTitulo;
        private Label lblSubtitulo;
        private Label lblAyuda;
        private Panel contenedorSemana;
        private CuadriculaSemanal cuadricula;
        private Button btnActualizar;
        private Button btnCerrar;

        public BitacoraMisClases()
        {
            InicializarComponentes();
            CargarProfesor();
            CargarHorario();
        }

        private void InicializarComponentes()
        {
            BackColor = Color.White;
            System.Drawing.Rectangle area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1180, area.Width - 80), Math.Min(700, area.Height - 80));
            MinimumSize = new Size(Math.Min(900, area.Width - 40), Math.Min(560, area.Height - 40));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Mi horario";

            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;

            lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(28, 18),
                Text = "Mi horario"
            };

            lblSubtitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(30, 55),
                Text = ""
            };

            lblAyuda = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.Gray,
                Location = new Point(30, 84),
                Text = "Pulsa una clase para ver quién registró su bitácora la última vez que se dio. En gris: fuera de vigencia. "
                    + "Rayado naranja: sesión extra; rayado gris: clase cancelada ese día."
            };

            contenedorSemana = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(30, 110),
                Size = new Size(ancho - 60, alto - 168),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            cuadricula = new CuadriculaSemanal { Dock = DockStyle.Fill, SoloLectura = true };
            cuadricula.FranjaClic += cuadricula_FranjaClic;
            cuadricula.ExcepcionClic += cuadricula_ExcepcionClic;
            contenedorSemana.Controls.Add(cuadricula);

            btnActualizar = CrearBoton("Actualizar", new Point(ancho - 220, alto - 46), new Size(110, 34));
            btnActualizar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnActualizar.Click += (s, e) => CargarHorario();

            btnCerrar = CrearBoton("Cerrar", new Point(ancho - 100, alto - 46), new Size(70, 34));
            btnCerrar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCerrar.Click += (s, e) => Close();

            Controls.Add(lblTitulo);
            Controls.Add(lblSubtitulo);
            Controls.Add(lblAyuda);
            Controls.Add(contenedorSemana);
            Controls.Add(btnActualizar);
            Controls.Add(btnCerrar);

            CancelButton = btnCerrar;
        }

        private Button CrearBoton(string texto, Point posicion, Size tamano)
        {
            Button boton = new Button
            {
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = posicion,
                Size = tamano,
                Text = texto,
                UseVisualStyleBackColor = false
            };
            boton.FlatAppearance.BorderColor = Color.DarkGreen;
            boton.FlatAppearance.BorderSize = 2;
            return boton;
        }

        // =====================================================================
        // Horario del profesor
        // =====================================================================

        private void CargarProfesor()
        {
            string numero = Datos_User.getUser();
            try
            {
                idProfesor = consultas.ObtenerIdPorMatricula(numero);
                nombreProfesor = consultas.MostrarNombreProfesor(numero);
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraMisClases.CargarProfesor", ex);
                MessageBox.Show("No se pudo leer tu perfil: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            lblSubtitulo.Text = string.IsNullOrWhiteSpace(nombreProfesor)
                ? "Tu horario de la semana"
                : nombreProfesor + ": tu horario de la semana";
        }

        private void CargarHorario()
        {
            if (idProfesor <= 0)
            {
                return;
            }

            try
            {
                List<FranjaInfo> franjas = datos.ObtenerFranjasDeProfesor(idProfesor);
                DateTime ahora = consultas.AhoraServidor();

                // Solo las excepciones de sus clases: cancelaciones y sesiones extra.
                HashSet<int> misClases = new HashSet<int>(franjas.Select(f => f.ClaseId));
                foreach (ClaseInfo clase in datos.ObtenerClasesDeProfesor(idProfesor))
                {
                    misClases.Add(clase.Id);
                }

                List<ExcepcionInfo> excepciones = datos.ObtenerExcepciones(ahora.Date, ahora.Date.AddDays(7), 0)
                    .Where(x => misClases.Contains(x.ClaseId))
                    .ToList();

                cuadricula.Ahora = ahora;
                cuadricula.Hoy = ahora.Date;
                cuadricula.Franjas = franjas;
                cuadricula.Excepciones = excepciones;
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraMisClases.CargarHorario", ex);
                MessageBox.Show("No se pudo leer tu horario: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // =====================================================================
        // Abrir la bitacora de una clase
        // =====================================================================

        private void cuadricula_FranjaClic(FranjaInfo franja)
        {
            if (franja == null)
            {
                return;
            }

            AbrirBitacora(franja.Id, null,
                franja.Codigo + " - " + franja.Materia + " - " + franja.Grupo,
                NombreDia(franja.DiaSemana) + " de " + HorariosDatos.Hora(franja.Inicio) + " a "
                    + HorariosDatos.Hora(franja.Fin) + ", " + franja.Laboratorio);
        }

        private void cuadricula_ExcepcionClic(ExcepcionInfo extra)
        {
            if (extra == null)
            {
                return;
            }

            AbrirBitacora(null, extra.Id,
                extra.Codigo + " - " + extra.Materia + " - " + extra.Grupo,
                "Sesion extra del " + extra.Fecha.ToString("dd/MM/yyyy") + ", " + HorariosDatos.Hora(extra.Inicio) + " a "
                    + HorariosDatos.Hora(extra.Fin) + ", " + extra.Laboratorio);
        }

        /// <summary>Abre la bitacora de esa clase en su propia ventana.</summary>
        internal void AbrirBitacora(int? idFranja, int? idExcepcion, string clase, string horario)
        {
            using (BitacoraSesion bitacora = new BitacoraSesion(idFranja, idExcepcion, clase, horario, nombreProfesor))
            {
                bitacora.ShowDialog(this);
            }
        }

        private static string NombreDia(int dia)
        {
            return dia >= 1 && dia < HorariosDatos.NombresDia.Length ? HorariosDatos.NombresDia[dia] : dia.ToString();
        }
    }
}
