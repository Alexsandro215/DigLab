using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DigBit.conexion
{
    public enum TipoComoQueda
    {
        Igual,
        Nueva,
        Cambios,
        SeQuita
    }

    /// <summary>Como queda una materia del borrador frente a la clase de la que se copio.</summary>
    public class ComoQueda
    {
        public TipoComoQueda Tipo;
        public string Texto = "";
    }

    /// <summary>Un aviso de una materia. Los que bloquean no dejan publicar (en rojo); los demas, en ambar.</summary>
    public class AvisoDeCarga
    {
        public string Texto = "";
        public bool Bloquea;
    }

    /// <summary>
    /// Dos horas que se cruzan el mismo dia en el mismo laboratorio, con el
    /// mismo profesor o con el mismo grupo (la regla de HorariosDatos.ValidarFranja).
    /// A siempre es del borrador; B, del borrador o una clase que ya esta en Clases.
    /// </summary>
    public class ChoqueCiclo
    {
        public CargaCiclo CargaA;
        public FranjaCiclo FranjaA;
        public CargaCiclo CargaB;
        public FranjaCiclo FranjaB;
        /// <summary>Si B ya esta en Clases: su franja.</summary>
        public FranjaInfo Programada;
        public bool MismoLaboratorio;
        public bool MismoProfesor;
        public bool MismoGrupo;

        public int Dia
        {
            get { return FranjaA.DiaSemana; }
        }

        /// <summary>Las horas que se cruzan.</summary>
        public TimeSpan Inicio
        {
            get { return FranjaA.Inicio > InicioB ? FranjaA.Inicio : InicioB; }
        }

        public TimeSpan Fin
        {
            get { return FranjaA.Fin < FinB ? FranjaA.Fin : FinB; }
        }

        private TimeSpan InicioB
        {
            get { return Programada != null ? Programada.Inicio : FranjaB.Inicio; }
        }

        private TimeSpan FinB
        {
            get { return Programada != null ? Programada.Fin : FranjaB.Fin; }
        }

        /// <summary>"mismo laboratorio", "misma persona docente y mismo grupo"...</summary>
        public string Motivos
        {
            get
            {
                List<string> motivos = new List<string>();
                if (MismoLaboratorio) motivos.Add("mismo laboratorio");
                if (MismoProfesor) motivos.Add("misma persona docente");
                if (MismoGrupo) motivos.Add("mismo grupo");
                return RevisionCiclo.Enumerar(motivos);
            }
        }

        public bool Toca(CargaCiclo carga)
        {
            return CargaA == carga || CargaB == carga;
        }

        /// <summary>
        /// Visto desde una de sus materias: "Choca con Redes de computadoras con
        /// 6ISC11 (Jorge Salinas) el Lunes de 08:00 a 09:00: mismo laboratorio."
        /// </summary>
        public string DescribirPara(CargaCiclo carga)
        {
            string otra;
            if (Programada != null)
            {
                otra = Programada.Materia + " con " + Programada.Grupo + " (" + Programada.ProfesorCorto + ", ya está en Clases)";
            }
            else
            {
                CargaCiclo b = carga == CargaB ? CargaA : CargaB;
                otra = b == carga ? "otro horario suyo" : b.Nombre + " (" + (b.ProfesorId.HasValue ? b.ProfesorCorto : "sin docente") + ")";
            }

            return "Choca con " + otra + " el " + HorariosDatos.NombresDia[Dia] + " de " + HorariosDatos.Hora(Inicio) + " a " + HorariosDatos.Hora(Fin)
                + ": " + Motivos + ".";
        }
    }

    /// <summary>Lo que se calcula de un borrador leido (RevisionCiclo.Revisar).</summary>
    public class RevisionDelCiclo
    {
        public List<ChoqueCiclo> Choques = new List<ChoqueCiclo>();
        public Dictionary<int, ComoQueda> ComoQueda = new Dictionary<int, ComoQueda>();
        public Dictionary<int, List<AvisoDeCarga>> Avisos = new Dictionary<int, List<AvisoDeCarga>>();
        public int Revisados;
        public int TotalProfesores;
        /// <summary>Materias sin quitar y sin profesor.</summary>
        public int SinProfesor;
        /// <summary>Materias sin quitar y sin horas en ningun laboratorio.</summary>
        public int SinHorario;
        /// <summary>Materias que no quedan igual (nuevas, con cambios o que se quitan).</summary>
        public int ConCambios;
        /// <summary>Materias con algun aviso que no deja publicar.</summary>
        public int Bloqueadas;

        public List<AvisoDeCarga> AvisosDe(CargaCiclo carga)
        {
            List<AvisoDeCarga> avisos;
            return Avisos.TryGetValue(carga.Id, out avisos) ? avisos : new List<AvisoDeCarga>();
        }

        public bool Bloquea(CargaCiclo carga)
        {
            return AvisosDe(carga).Any(a => a.Bloquea);
        }

        public ComoQueda De(CargaCiclo carga)
        {
            ComoQueda como;
            return ComoQueda.TryGetValue(carga.Id, out como) ? como : new ComoQueda { Tipo = TipoComoQueda.Igual, Texto = "Igual" };
        }

        /// <summary>Cuantos choques distintos tocan alguna materia de ese profesor (null: las sin profesor).</summary>
        public int ChoquesDe(int? profesor)
        {
            return Choques.Count(c => c.CargaA.ProfesorId == profesor || (c.CargaB != null && c.CargaB.ProfesorId == profesor));
        }
    }

    /// <summary>
    /// Las reglas del horario preliminar (Horarios > Proximo ciclo), en memoria:
    /// como queda cada materia frente a la clase de la que se copio, sus avisos,
    /// los choques y las cuentas. La usan la pantalla y Publicar.
    /// </summary>
    internal static class RevisionCiclo
    {
        private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

        /// <summary>
        /// 'lab:dia:HH:MM-HH:MM;...' ordenado por dia, inicio y laboratorio: la
        /// misma forma que guarda horario_origen al copiar (GROUP_CONCAT).
        /// </summary>
        public static string Firma(IEnumerable<FranjaCiclo> franjas)
        {
            return string.Join(";", franjas
                .OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio).ThenBy(f => f.LaboratorioId)
                .Select(f => f.LaboratorioId.ToString(Invariante) + ":" + f.DiaSemana.ToString(Invariante) + ":"
                    + HorariosDatos.Hora(f.Inicio) + "-" + HorariosDatos.Hora(f.Fin)));
        }

        public static double Horas(IEnumerable<FranjaCiclo> franjas)
        {
            return franjas.Sum(f => (f.Fin - f.Inicio).TotalHours);
        }

        /// <summary>Las horas de una firma (horario_origen): lo que daba en el ciclo que se copio.</summary>
        public static double HorasDeFirma(string firma)
        {
            double horas = 0;
            foreach (string parte in (firma ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] trozos = parte.Split(':');
                // lab:dia:HH:MM-HH:MM -> ["lab", "dia", "HH", "MM-HH", "MM"]
                TimeSpan inicio;
                TimeSpan fin;
                if (trozos.Length == 5
                    && TimeSpan.TryParse(trozos[2] + ":" + trozos[3].Split('-')[0], Invariante, out inicio)
                    && TimeSpan.TryParse(trozos[3].Split('-').Last() + ":" + trozos[4], Invariante, out fin))
                {
                    horas += (fin - inicio).TotalHours;
                }
            }

            return horas;
        }

        /// <summary>"1 hora", "14 horas", "14.5 horas".</summary>
        public static string TextoHoras(double horas)
        {
            double redondeadas = Math.Round(horas * 2) / 2;
            return redondeadas == 1 ? "1 hora" : redondeadas.ToString("0.#", Invariante) + " horas";
        }

        public static string TextoMaterias(int materias)
        {
            return materias == 1 ? "1 materia" : materias + " materias";
        }

        /// <summary>"a", "a y b", "a, b y c".</summary>
        public static string Enumerar(IList<string> partes)
        {
            if (partes.Count == 0)
            {
                return "";
            }

            return partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }

        /// <summary>Como queda una materia: "Igual", "Nueva", "Se quita" o lo que cambio ("Viene de Jorge Salinas, Cambió el horario").</summary>
        public static ComoQueda ComoQuedaDe(CargaCiclo carga)
        {
            if (carga.Quitada)
            {
                return new ComoQueda { Tipo = TipoComoQueda.SeQuita, Texto = "Se quita" };
            }

            if (!carga.Copiada)
            {
                return new ComoQueda { Tipo = TipoComoQueda.Nueva, Texto = "Nueva" };
            }

            List<string> partes = new List<string>();
            if (carga.ProfesorOrigenId != carga.ProfesorId)
            {
                partes.Add(!carga.ProfesorId.HasValue ? "Sin docente (era de " + carga.ProfesorOrigenCorto + ")"
                    : carga.ProfesorOrigenId.HasValue ? "Viene de " + carga.ProfesorOrigenCorto
                    : "Cambió de docente");
            }

            if (carga.GrupoOrigenId.HasValue && carga.GrupoOrigenId.Value != carga.GrupoId)
            {
                partes.Add("Cambió el grupo (antes " + carga.GrupoOrigen + ")");
            }

            if (carga.MateriaOrigenId.HasValue && carga.MateriaOrigenId.Value != carga.MateriaId)
            {
                partes.Add("Cambió la materia (antes " + carga.MateriaOrigen + ")");
            }

            if (Firma(carga.Franjas) != (carga.HorarioOrigen ?? ""))
            {
                partes.Add("Cambió el horario");
            }

            return partes.Count == 0
                ? new ComoQueda { Tipo = TipoComoQueda.Igual, Texto = "Igual" }
                : new ComoQueda { Tipo = TipoComoQueda.Cambios, Texto = string.Join(", ", partes) };
        }

        /// <summary>Todo lo que se calcula del borrador: como queda, avisos, choques y cuentas.</summary>
        public static RevisionDelCiclo Revisar(CicloLeido leido)
        {
            RevisionDelCiclo revision = new RevisionDelCiclo();
            List<CargaCiclo> vivas = leido.Cargas.Where(c => !c.Quitada).ToList();
            revision.Choques = Choques(vivas, leido.YaProgramadas);

            foreach (CargaCiclo carga in leido.Cargas)
            {
                ComoQueda como = ComoQuedaDe(carga);
                revision.ComoQueda[carga.Id] = como;
                if (como.Tipo != TipoComoQueda.Igual)
                {
                    revision.ConCambios++;
                }
            }

            foreach (CargaCiclo carga in vivas)
            {
                List<AvisoDeCarga> avisos = new List<AvisoDeCarga>();
                if (!carga.ProfesorId.HasValue)
                {
                    avisos.Add(Rojo("Sin docente: dásela a alguien o quítala."));
                    revision.SinProfesor++;
                }
                else if (leido.ConBajas && !carga.ProfesorActivo)
                {
                    avisos.Add(Rojo(carga.ProfesorCorto + " está de baja: pásala a otra persona docente."));
                }

                if (leido.ConLaboratoriosDesactivables)
                {
                    foreach (string laboratorio in carga.Franjas.Where(f => !f.LaboratorioActivo).Select(f => f.Laboratorio).Distinct())
                    {
                        avisos.Add(Rojo("El " + EstiloHorarios.NombreLaboratorio(laboratorio) + " está desactivado: cámbiale el horario."));
                    }
                }

                foreach (ChoqueCiclo choque in revision.Choques.Where(c => c.Toca(carga)))
                {
                    avisos.Add(Rojo(choque.DescribirPara(carga)));
                }

                if (carga.ProfesorId.HasValue && vivas.Any(o => o != carga && o.ProfesorId == carga.ProfesorId
                    && o.GrupoId == carga.GrupoId && o.MateriaId == carga.MateriaId))
                {
                    avisos.Add(Rojo("Repetida: " + carga.ProfesorCorto + " tiene dos veces " + carga.Nombre + "."));
                }

                if (carga.Franjas.Count == 0)
                {
                    avisos.Add(Ambar("Sin horario: no saldrá en ningún laboratorio."));
                    revision.SinHorario++;
                }

                foreach (CargaCiclo otra in vivas.Where(o => o != carga && o.GrupoId == carga.GrupoId && o.MateriaId == carga.MateriaId
                    && o.ProfesorId != carga.ProfesorId))
                {
                    avisos.Add(Ambar(otra.ProfesorId.HasValue ? "También la tiene " + otra.ProfesorCorto + "." : "También está en «Sin docente»."));
                }

                if (!carga.ProfesorId.HasValue && vivas.Any(o => o != carga && !o.ProfesorId.HasValue
                    && o.GrupoId == carga.GrupoId && o.MateriaId == carga.MateriaId))
                {
                    avisos.Add(Ambar("Está dos veces en «Sin docente»."));
                }

                revision.Avisos[carga.Id] = avisos;
                if (avisos.Any(a => a.Bloquea))
                {
                    revision.Bloqueadas++;
                }
            }

            revision.TotalProfesores = leido.Profesores.Count;
            revision.Revisados = leido.Profesores.Count(p => p.Estado != EstadoProfesorCiclo.PorRevisar);
            return revision;
        }

        private static AvisoDeCarga Rojo(string texto)
        {
            return new AvisoDeCarga { Texto = texto, Bloquea = true };
        }

        private static AvisoDeCarga Ambar(string texto)
        {
            return new AvisoDeCarga { Texto = texto, Bloquea = false };
        }

        /// <summary>Los choques entre las franjas de esas materias y con las clases ya programadas, cada par una vez.</summary>
        public static List<ChoqueCiclo> Choques(List<CargaCiclo> cargas, List<FranjaInfo> programadas)
        {
            List<ChoqueCiclo> choques = new List<ChoqueCiclo>();
            List<KeyValuePair<CargaCiclo, FranjaCiclo>> todas = cargas
                .SelectMany(c => c.Franjas.Select(f => new KeyValuePair<CargaCiclo, FranjaCiclo>(c, f)))
                .ToList();
            for (int i = 0; i < todas.Count; i++)
            {
                CargaCiclo a = todas[i].Key;
                FranjaCiclo fa = todas[i].Value;
                for (int j = i + 1; j < todas.Count; j++)
                {
                    CargaCiclo b = todas[j].Key;
                    FranjaCiclo fb = todas[j].Value;
                    if (fa.DiaSemana != fb.DiaSemana || !(fa.Inicio < fb.Fin && fa.Fin > fb.Inicio))
                    {
                        continue;
                    }

                    ChoqueCiclo choque = new ChoqueCiclo
                    {
                        CargaA = a,
                        FranjaA = fa,
                        CargaB = b,
                        FranjaB = fb,
                        MismoLaboratorio = fa.LaboratorioId == fb.LaboratorioId,
                        MismoProfesor = a.ProfesorId.HasValue && a.ProfesorId == b.ProfesorId,
                        MismoGrupo = a.GrupoId == b.GrupoId
                    };
                    if (choque.MismoLaboratorio || choque.MismoProfesor || choque.MismoGrupo)
                    {
                        choques.Add(choque);
                    }
                }

                foreach (FranjaInfo p in programadas ?? new List<FranjaInfo>())
                {
                    if (fa.DiaSemana != p.DiaSemana || !(fa.Inicio < p.Fin && fa.Fin > p.Inicio))
                    {
                        continue;
                    }

                    ChoqueCiclo choque = new ChoqueCiclo
                    {
                        CargaA = a,
                        FranjaA = fa,
                        Programada = p,
                        MismoLaboratorio = fa.LaboratorioId == p.LaboratorioId,
                        MismoProfesor = a.ProfesorId.HasValue && a.ProfesorId.Value == p.ProfesorId,
                        MismoGrupo = a.GrupoId == p.GrupoId
                    };
                    if (choque.MismoLaboratorio || choque.MismoProfesor || choque.MismoGrupo)
                    {
                        choques.Add(choque);
                    }
                }
            }

            return choques;
        }

        /// <summary>
        /// Para los dialogos, antes de guardar: con que chocarian esas horas si la
        /// materia fuera de ese profesor y ese grupo. Las de las materias de
        /// "excluir" (la misma que se cambia) no cuentan.
        /// </summary>
        public static List<string> ChoquesSi(CicloLeido leido, int? profesor, int grupo, IEnumerable<FranjaCiclo> franjas, ICollection<int> excluir)
        {
            CargaCiclo supuesta = new CargaCiclo { Id = -1, ProfesorId = profesor, GrupoId = grupo };
            supuesta.Franjas.AddRange(franjas.Select(f => new FranjaCiclo
            {
                Id = -1,
                LaboratorioId = f.LaboratorioId,
                Laboratorio = f.Laboratorio,
                DiaSemana = f.DiaSemana,
                Inicio = f.Inicio,
                Fin = f.Fin
            }));
            List<CargaCiclo> otras = leido.Cargas.Where(c => !c.Quitada && !excluir.Contains(c.Id)).ToList();
            List<string> textos = new List<string>();
            foreach (ChoqueCiclo choque in Choques(new[] { supuesta }.Concat(otras).ToList(), leido.YaProgramadas).Where(c => c.Toca(supuesta)))
            {
                string otra = choque.Programada != null
                    ? choque.Programada.Materia + " con " + choque.Programada.Grupo + " (" + choque.Programada.ProfesorCorto + ", ya está en Clases)"
                    : (choque.CargaA == supuesta ? choque.CargaB : choque.CargaA) == supuesta ? "otro de sus horarios"
                    : (choque.CargaA == supuesta ? choque.CargaB : choque.CargaA).Nombre + " ("
                        + ((choque.CargaA == supuesta ? choque.CargaB : choque.CargaA).ProfesorId.HasValue
                            ? (choque.CargaA == supuesta ? choque.CargaB : choque.CargaA).ProfesorCorto : "sin docente") + ")";
                textos.Add(HorariosDatos.NombresDia[choque.Dia] + " de " + HorariosDatos.Hora(choque.Inicio) + " a " + HorariosDatos.Hora(choque.Fin)
                    + ": choca con " + otra + ", " + choque.Motivos + ".");
            }

            return textos.Distinct().ToList();
        }

        /// <summary>
        /// "Por revisar", "No sigue" o, ya revisado: "Igual" (todo como estaba),
        /// "Se incorpora" (no daba clases en el ciclo copiado) o "Con cambios".
        /// </summary>
        public static string EstadoDe(ProfesorDelCiclo profesor, CicloLeido leido, RevisionDelCiclo revision)
        {
            if (profesor.Estado == EstadoProfesorCiclo.NoSigue)
            {
                return "No sigue";
            }

            if (profesor.Estado == EstadoProfesorCiclo.PorRevisar)
            {
                return "Por revisar";
            }

            List<CargaCiclo> eran = leido.Cargas.Where(c => c.ProfesorOrigenId == profesor.Id).ToList();
            if (eran.Count == 0)
            {
                return "Se incorpora";
            }

            bool igual = eran.All(c => c.ProfesorId == profesor.Id && !c.Quitada)
                && leido.Cargas.Where(c => c.ProfesorId == profesor.Id && !c.Quitada).All(c => revision.De(c).Tipo == TipoComoQueda.Igual);
            return igual ? "Igual" : "Con cambios";
        }
    }
}
