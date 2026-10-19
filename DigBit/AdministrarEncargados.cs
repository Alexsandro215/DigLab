using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;

namespace DigBit
{
    /// <summary>
    /// Encargados de laboratorio (tipo 4, db/18): quienes entran al panel del
    /// administrador en solo consulta. La abre Profesores, solo para el
    /// administrador. Es la forma de Profesores: lista con buscador y, sobre el
    /// elegido, editar, restablecer la contrasena y dar de baja o reactivar, con
    /// DialogoUsuario para el alta y la edicion. Cada accion empieza con
    /// Permisos.Bloquear, y la capa de datos con Permisos.Exigir.
    /// </summary>
    internal class AdministrarEncargados : PantallaAdmin
    {
        // Lo que se muestra es la cuenta (activa, de baja): sin "activos" ni "dados de baja".
        private const string Activos = "Cuentas activas";
        private const string DadosDeBaja = "Cuentas de baja";
        private const string Todos = "Todas";

        private readonly EncargadosDatos datos = new EncargadosDatos();
        private readonly Dictionary<int, string> textoBusqueda = new Dictionary<int, string>();
        private List<EncargadoInfo> encargados = new List<EncargadoInfo>();
        // Si la ultima lectura salio bien: si no, no se sabe si la base tiene el tipo 4.
        private bool leida;

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboMostrar;
        private readonly Button btnNuevo;
        private readonly Button btnEditar;
        private readonly Button btnContrasena;
        private readonly Button btnBaja;

        public AdministrarEncargados()
            : base("Responsables de laboratorio", Permisos.SoloConsulta
                    ? "Ven el panel, las alertas, las bitácoras y los horarios, pero no pueden cambiar nada."
                    : "Ven el panel, las alertas, las bitácoras y los horarios, pero no pueden cambiar nada. Doble clic para editar.",
                new Size(1180, 620))
        {
            // El subtitulo es largo: con la ventana mas estrecha se meteria debajo de "Registrar responsable".
            MinimumSize = new Size(1100, MinimumSize.Height);

            // En solo consulta no se crean los botones que escriben: ocultarlos
            // dejaria huecos, porque se reparten el espacio al crearse.
            if (!Permisos.SoloConsulta)
            {
                btnNuevo = BotonPrincipalArriba("Registrar responsable", 210);
                btnNuevo.Click += (s, e) => NuevoEncargado();
            }

            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Mostrar", new Point(380, 88)));
            cboMostrar = EstiloHorarios.CrearCombo(new Point(380, 111), 170);
            cboMostrar.Items.AddRange(new object[] { Activos, DadosDeBaja, Todos });
            cboMostrar.SelectedIndex = 0;
            cboMostrar.SelectedIndexChanged += (s, e) => Aplicar(null);
            Controls.Add(cboMostrar);

            CrearLista(156);
            grid.Columns.Add("numero", "No. de empleado");
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("correo", "Correo");
            grid.Columns.Add("estado", "Estado");
            EstiloHorarios.Pesos(grid, 110, 240, 240, 110);

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            if (!Permisos.SoloConsulta)
            {
                SeparacionAbajo(20);
                btnBaja = BotonAbajo("Dar de baja", 150);
                btnBaja.Click += (s, e) => CambiarEstadoSeleccionado();
                btnContrasena = BotonAbajo("Restablecer contraseña", 210);
                btnContrasena.Click += (s, e) => RestablecerContrasenaSeleccionado();
                btnEditar = BotonAbajo("Editar", 110);
                btnEditar.Click += (s, e) => EditarSeleccionado();
            }

            ActiveControl = txtBuscar;
            ActualizarBotones();
        }

        // =====================================================================
        // Carga y filtros
        // =====================================================================

        protected override void Recargar(string seleccionar)
        {
            try
            {
                encargados = datos.Listar();
                leida = true;
            }
            catch (Exception ex)
            {
                encargados = new List<EncargadoInfo>();
                leida = false;
                AvisoDeFalloAdmin.Mostrar(this, "Leer la lista de encargados de laboratorio", ex, "No se pudo leer la lista de responsables de laboratorio", Text);
            }

            textoBusqueda.Clear();
            foreach (EncargadoInfo encargado in encargados)
            {
                textoBusqueda[encargado.Id] = EstiloAdmin.Normalizar(encargado.Matricula + " " + encargado.NombreCompleto + " " + encargado.Correo);
            }

            // Sin la migracion 08 no hay bajas: se ve a todos y no se da de baja a nadie.
            cboMostrar.Enabled = datos.BajasDisponibles;

            // Sin el tipo 4 en la base (falta db/18) se ve la lista, pero no se da
            // de alta a nadie: la clave foranea de la escuela lo rechazaria.
            bool sinTipo = leida && !datos.AltaDisponible;
            if (btnNuevo != null)
            {
                btnNuevo.Enabled = !sinTipo;
            }

            NotaFija = Permisos.SoloConsulta ? ""
                : sinTipo ? "Para registrar responsables falta aplicar db/18_encargados.sql en la base."
                : leida && !datos.PuedenEntrar ? "Aún no pueden entrar: falta su cuenta de solo consulta. Pide a quien administra el servidor que aplique db/18_encargados.sql con root."
                : leida && !datos.BajasDisponibles ? "Para dar de baja falta aplicar la migración 08 en la base."
                : "";
            Aplicar(seleccionar);
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        /// <summary>Filtra la lista en memoria y la vuelve a pintar.</summary>
        private void Aplicar(string seleccionar)
        {
            string mostrar = datos.BajasDisponibles ? (string)cboMostrar.SelectedItem : Todos;
            List<EncargadoInfo> visibles = encargados.Where(x =>
                    (mostrar == Todos || (mostrar == Activos) == x.Activo)
                    && EstiloAdmin.Coincide(textoBusqueda[x.Id], txtBuscar.Text))
                .ToList();

            grid.Columns["estado"].Visible = mostrar != Activos;
            LlenarLista(visibles,
                x => new object[]
                {
                    x.Matricula,
                    x.NombreParaLista,
                    EstiloAdmin.Guion(x.Correo),
                    AdministrarAlumnos.EstadoDe(x)
                },
                x => !x.Activo, x => x.Matricula, seleccionar);

            int bajas = encargados.Count(x => !x.Activo);
            if (encargados.Count == 0)
            {
                lblResumen.Text = "Todavía no hay responsables de laboratorio.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = "Nadie coincide con la búsqueda.";
            }
            else
            {
                lblResumen.Text = (visibles.Count == encargados.Count ? "" : visibles.Count + " de ")
                    + encargados.Count + (encargados.Count == 1 ? " responsable" : " responsables")
                    + (bajas > 0 ? " (" + bajas + " de baja)" : "");
            }
        }

        protected override void ActualizarBotones()
        {
            // btnEditar es el ultimo que se crea: sin el (al construir la lista, o en
            // solo consulta) no hay botones que tocar.
            if (btnEditar == null)
            {
                return;
            }

            EncargadoInfo encargado = Seleccionado<EncargadoInfo>();
            btnEditar.Enabled = encargado != null;
            btnContrasena.Enabled = encargado != null && encargado.Activo;
            btnBaja.Enabled = encargado != null && datos.BajasDisponibles;
            btnBaja.Text = encargado != null && !encargado.Activo ? "Reactivar" : "Dar de baja";
        }

        protected override void AlDobleClic()
        {
            // En solo consulta el doble clic y Enter no hacen nada: editar escribe.
            if (Permisos.SoloConsulta)
            {
                return;
            }

            EditarSeleccionado();
        }

        // =====================================================================
        // Acciones
        // =====================================================================

        private void NuevoEncargado()
        {
            if (Permisos.Bloquear(this, "Dar de alta un encargado de laboratorio")) return;
            using (DialogoUsuario dialogo = new DialogoUsuario(datos, null, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.IdentificadorGuardado);
                    Avisar("Se registró a " + dialogo.NombreGuardado + ". Ya puede entrar con su número de empleado y su contraseña.");
                }
            }
        }

        private void EditarSeleccionado()
        {
            if (Permisos.Bloquear(this, "Editar un encargado de laboratorio")) return;
            EncargadoInfo encargado = Seleccionado<EncargadoInfo>();
            if (encargado == null)
            {
                return;
            }

            using (DialogoUsuario dialogo = new DialogoUsuario(datos, encargado, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.IdentificadorGuardado);
                    Avisar("Se guardaron los cambios de " + dialogo.NombreGuardado + ".");
                }
            }
        }

        private void RestablecerContrasenaSeleccionado()
        {
            if (Permisos.Bloquear(this, "Restablecer la contrasena de un encargado de laboratorio")) return;
            EncargadoInfo encargado = Seleccionado<EncargadoInfo>();
            if (encargado == null)
            {
                return;
            }

            using (DialogoContrasena dialogo = new DialogoContrasena(encargado.NombreCompleto + " (" + encargado.Matricula + ")"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    datos.RestablecerContrasena(encargado.Id, dialogo.Contrasena);
                    Avisar("Contraseña nueva para " + encargado.NombreCompleto + ". Ya puede entrar con ella.");
                }
                catch (Exception ex)
                {
                    AvisoDeFalloAdmin.Mostrar(this, "Restablecer la contrasena de " + encargado.Matricula, ex, "No se pudo cambiar la contraseña", Text);
                }
            }
        }

        private void CambiarEstadoSeleccionado()
        {
            if (Permisos.Bloquear(this, "Dar de baja o reactivar un encargado de laboratorio")) return;
            EncargadoInfo encargado = Seleccionado<EncargadoInfo>();
            if (encargado == null)
            {
                return;
            }

            try
            {
                if (!encargado.Activo)
                {
                    // Reactivar no quita nada a nadie: no se pregunta.
                    datos.Reactivar(encargado.Id);
                    Recargar(encargado.Matricula);
                    Avisar("Se reactivó a " + encargado.NombreCompleto + ".");
                    return;
                }

                if (!DialogoConfirmacion.Preguntar(this, "Dar de baja",
                    "¿Dar de baja a " + encargado.NombreCompleto + " (" + encargado.Matricula + ")?",
                    "Ya no podrá entrar a DigBit. Puedes reactivar su cuenta cuando quieras.",
                    "Dar de baja"))
                {
                    return;
                }

                datos.DarDeBaja(encargado.Id);
                Recargar(encargado.Matricula);
                Avisar("Se dio de baja a " + encargado.NombreCompleto + ": ya no podrá entrar.");
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Cambiar el estado del encargado " + encargado.Matricula, ex, "No se pudo cambiar el estado de la cuenta", Text);
            }
        }
    }
}
