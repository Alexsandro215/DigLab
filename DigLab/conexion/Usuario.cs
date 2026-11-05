using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DigLab.conexion
{

    public class Usuario
    {
        public string Nombre { get; set; }
        public string ApellidoPaterno { get; set; }
        public string ApellidoMaterno { get; set; }
        public string Correo { get; set; }
        public int CarreraId { get; set; }
        public int SemestreId { get; set; }
        public int GrupoId { get; set; }
        public string id { get; set; }

        public string CarreraNombre { get; set; }
        public string SemestreNombre { get; set; }
        public string GrupoNombre { get; set; }

        public string HoraEntrada { get; set; }
        public string HoraSalida { get; set; }
        public string GruposId { get; set; }
        public string MateriasId { get; set; }
        public string NombreMateria { get; set; }
        public string NombreGrupo { get; set; }
        public string HoraRegistro { get; set; }
    }

    public class UsuarioConInformacionAdicional
    {
        public string id {get; set;}
        public int TipoUsuarioId { get; set; }
        public string Nombre { get; set; }
        public string ApellidoPaterno { get; set; }
        public string ApellidoMaterno { get; set; }
        public string Correo { get; set; }
        public string NombreCarrera { get; set; }
        public string NombreSemestre { get; set; }
        public string NombreGrupo { get; set; }
    }
}
