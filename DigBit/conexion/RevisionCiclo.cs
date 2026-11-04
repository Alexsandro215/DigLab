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

    /// <summary>Como queda una clase de la configuracion frente a lo que se copio (o se guardo por ultima vez).</summary>
    public class ComoQueda
    {
        public TipoComoQueda Tipo;
        public string Texto = "";
    }

    /// <summary>Un aviso de una clase. Los que bloquean no dejan aplicar (en rojo); los demas, en ambar.</summary>
    public class AvisoDeCarga
    {
        public string Texto = "";
        public bool Bloquea;
    }

    /// <summary>
    /// Dos horas que se cruzan el mismo dia. Sin MismoLaboratorio es un choque
    /// (RevisionDelCiclo.Choques): en laboratorios distintos, con la misma
    /// persona docente o con el mismo grupo (decision del 2026-10-31). Con
    /// MismoLaboratorio, dos clases que se enciman en un laboratorio
    /// (RevisionDelCiclo.Encimadas): no es un choque (al poner una encima de
    /// otra, la reemplaza), pero si llegan asi (datos viejos o copiados) no
    /// dejan aplicar.
    /// A siempre es de la configuracion; B, de la configuracion o una clase que ya esta en Clases.
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

        /// <summary>"misma persona docente", "mismo grupo" o "misma persona docente y mismo grupo".</summary>
        public string Motivos
        {
            get
            {
                List<string> motivos = new List<string>();
                if (MismoProfesor) motivos.Add("misma persona docente");
                if (MismoGrupo) motivos.Add("mismo grupo");
                return RevisionCiclo.Enumerar(motivos);
            }
        }

        public bool Toca(CargaCiclo carga)
        {
            return CargaA == carga || CargaB == carga;
        }

        /// <summary>El laboratorio de la otra, vista desde esa materia.</summary>
        private string LaboratorioDeLaOtra(CargaCiclo carga)
        {
            return Programada != null ? Programada.Laboratorio : carga == CargaB ? FranjaA.Laboratorio : FranjaB.Laboratorio;
        }

        /// <summary>
        /// Visto desde una de sus materias: "Choca con Redes de computadoras con
        /// 6ISC11 (Jorge Salinas) en el Laboratorio CISCO el Lunes de 08:00 a
        /// 09:00: mismo grupo." o, si se enciman, "Se encima con ... el Lunes de
        /// 08:00 a 09:00 en el mismo laboratorio: quitale esas horas a una de las dos."
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

            string cuando = " el " + HorariosDatos.NombresDia[Dia] + " de " + HorariosDatos.Hora(Inicio) + " a " + HorariosDatos.Hora(Fin);
            if (MismoLaboratorio)
            {
                return "Se encima con " + otra + cuando + " en el mismo laboratorio: quítale esas horas a una de las dos.";
            }

            return "Choca con " + otra + " en el " + EstiloHorarios.NombreLaboratorio(LaboratorioDeLaOtra(carga)) + cuando + ": " + Motivos + ".";
        }
    }

    /// <summary>
    /// Que clases ensena la lista Clases de una configuracion (las pastillas de
    /// resumen y el enlace "Verlas" de Aplicacion): RevisionCiclo.EntraEnFiltro.
    /// </summary>
    public enum FiltroClases
    {
        Ninguno,
        SinDocente,
        /// <summary>De docentes de baja (con db/08).</summary>
        DeBaja,
        /// <summary>Con horas en un laboratorio desactivado (con db/08).</summary>
        LaboratorioDesactivado,
        Choques,
        /// <summary>El mismo docente con la misma materia y grupo dos veces.</summary>
        Repetidas,
        SinHorario,
        /// <summary>La misma materia con el mismo grupo con dos personas docentes (o dos veces sin docente).</summary>
        DosDocentes,
        /// <summary>Encimada con otra en el mismo laboratorio (RevisionDelCiclo.Encimadas).</summary>
        Encimadas
    }

    /// <summary>Lo que se calcula de una configuracion leida (RevisionCiclo.Revisar).</summary>
    public class RevisionDelCiclo
    {
        /// <summary>La misma persona docente o el mismo grupo a la misma hora en laboratorios distintos.</summary>
        public List<ChoqueCiclo> Choques = new List<ChoqueCiclo>();
        /// <summary>Dos clases a la misma hora en el mismo laboratorio (MismoLaboratorio): no dejan aplicar.</summary>
        public List<ChoqueCiclo> Encimadas = new List<ChoqueCiclo>();
        public Dictionary<int, ComoQueda> ComoQueda = new Dictionary<int, ComoQueda>();
        public Dictionary<int, List<AvisoDeCarga>> Avisos = new Dictionary<int, List<AvisoDeCarga>>();
        /// <summary>Personas docentes distintas con clases.</summary>
        public int TotalProfesores;
        /// <summary>Clases sin quitar y sin docente.</summary>
        public int SinProfesor;
        /// <summary>Clases sin quitar y sin horas en ningun laboratorio.</summary>
        public int SinHorario;
        /// <summary>Clases de docentes de baja (con db/08) y sus nombres cortos, sin repetir.</summary>
        public int DeBaja;
        public List<string> DocentesDeBaja = new List<string>();
        /// <summary>Clases con horas en un laboratorio desactivado (con db/08) y esos laboratorios, sin repetir.</summary>
        public int EnLaboratorioDesactivado;
        public List<string> LaboratoriosDesactivados = new List<string>();
        /// <summary>Las clases repetidas (mismo docente, grupo y materia), cada una una vez.</summary>
        public List<CargaCiclo> Repetidas = new List<CargaCiclo>();
        /// <summary>Clases que no quedan igual (nuevas, con cambios o que se quitan).</summary>
        public int ConCambios;
        /// <summary>Clases con algun aviso que no deja aplicar.</summary>
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
    /// Las reglas de una configuracion de horario (Horarios > Configuraciones y
    /// Aplicacion), en memoria: como queda cada clase frente a lo que se copio,
    /// sus avisos, los choques y las cuentas. La usan las pantallas, la revision
    /// de Aplicacion (RevisionAplicacion) y Aplicar.
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

        /// <summary>"1 clase", "23 clases".</summary>
        public static string TextoClases(int clases)
        {
            return clases == 1 ? "1 clase" : clases + " clases";
        }

        /// <summary>"1 docente", "10 docentes".</summary>
        public static string TextoDocentes(int docentes)
        {
            return docentes == 1 ? "1 docente" : docentes + " docentes";
        }

        /// <summary>"1 horario", "60 horarios" (franjas).</summary>
        public static string TextoHorarios(int horarios)
        {
            return horarios == 1 ? "1 horario" : horarios + " horarios";
        }

        /// <summary>"1 laboratorio", "4 laboratorios".</summary>
        public static string TextoLaboratorios(int laboratorios)
        {
            return laboratorios == 1 ? "1 laboratorio" : laboratorios + " laboratorios";
        }

        /// <summary>"23 clases de 10 docentes, 60 horas en 4 laboratorios" (sin punto): el resumen de una configuracion.</summary>
        public static string Resumen(CicloPreliminar ciclo)
        {
            return TextoClases(ciclo.Clases) + " de " + TextoDocentes(ciclo.Docentes) + ", " + TextoHoras(ciclo.Horas)
                + " en " + TextoLaboratorios(ciclo.Laboratorios);
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

        /// <summary>Como queda una materia: "Igual", "Nueva", "Se quita" o lo que cambio ("Viene de Jorge Salinas, Cambio el horario").</summary>
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

        /// <summary>Todo lo que se calcula de una configuracion: como queda, avisos, choques y cuentas.</summary>
        public static RevisionDelCiclo Revisar(CicloLeido leido)
        {
            RevisionDelCiclo revision = new RevisionDelCiclo();
            List<CargaCiclo> vivas = leido.Cargas.Where(c => !c.Quitada).ToList();
            List<ChoqueCiclo> cruces = Cruces(vivas, leido.YaProgramadas);
            revision.Choques = cruces.Where(c => !c.MismoLaboratorio).ToList();
            revision.Encimadas = cruces.Where(c => c.MismoLaboratorio).ToList();
            HashSet<string> deBaja = new HashSet<string>();
            HashSet<string> desactivados = new HashSet<string>();

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
                    revision.DeBaja++;
                    if (deBaja.Add(carga.ProfesorCorto))
                    {
                        revision.DocentesDeBaja.Add(carga.ProfesorCorto);
                    }
                }

                if (leido.ConLaboratoriosDesactivables)
                {
                    List<string> apagados = carga.Franjas.Where(f => !f.LaboratorioActivo).Select(f => f.Laboratorio).Distinct().ToList();
                    foreach (string laboratorio in apagados)
                    {
                        avisos.Add(Ambar("El " + EstiloHorarios.NombreLaboratorio(laboratorio) + " está deshabilitado: estas horas no corren hasta habilitarlo."));
                        if (desactivados.Add(laboratorio))
                        {
                            revision.LaboratoriosDesactivados.Add(EstiloHorarios.NombreLaboratorio(laboratorio));
                        }
                    }

                    if (apagados.Count > 0)
                    {
                        revision.EnLaboratorioDesactivado++;
                    }
                }

                foreach (ChoqueCiclo choque in revision.Choques.Where(c => c.Toca(carga)))
                {
                    avisos.Add(Rojo(choque.DescribirPara(carga)));
                }

                foreach (ChoqueCiclo encimada in revision.Encimadas.Where(c => c.Toca(carga)))
                {
                    avisos.Add(Rojo(encimada.DescribirPara(carga)));
                }

                if (EsRepetida(vivas, carga))
                {
                    avisos.Add(Rojo("Repetida: " + carga.ProfesorCorto + " tiene dos veces " + carga.Nombre + "."));
                    revision.Repetidas.Add(carga);
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
            return revision;
        }

        /// <summary>Con docente, y el mismo docente tiene otra vez la misma materia con el mismo grupo.</summary>
        private static bool EsRepetida(List<CargaCiclo> vivas, CargaCiclo carga)
        {
            return carga.ProfesorId.HasValue && vivas.Any(o => o != carga && o.ProfesorId == carga.ProfesorId
                && o.GrupoId == carga.GrupoId && o.MateriaId == carga.MateriaId);
        }

        /// <summary>
        /// Si la clase entra en el filtro de la lista Clases (las pastillas de
        /// resumen y "Verlas"). La revision es la de RevisionCiclo.Revisar del mismo leido.
        /// </summary>
        public static bool EntraEnFiltro(CicloLeido leido, RevisionDelCiclo revision, CargaCiclo carga, FiltroClases filtro)
        {
            switch (filtro)
            {
                case FiltroClases.SinDocente:
                    return !carga.ProfesorId.HasValue;
                case FiltroClases.DeBaja:
                    return leido.ConBajas && carga.ProfesorId.HasValue && !carga.ProfesorActivo;
                case FiltroClases.LaboratorioDesactivado:
                    return leido.ConLaboratoriosDesactivables && carga.Franjas.Any(f => !f.LaboratorioActivo);
                case FiltroClases.Choques:
                    return revision.Choques.Any(c => c.Toca(carga));
                case FiltroClases.Encimadas:
                    return revision.Encimadas.Any(c => c.Toca(carga));
                case FiltroClases.Repetidas:
                    return EsRepetida(leido.Cargas.Where(c => !c.Quitada).ToList(), carga);
                case FiltroClases.SinHorario:
                    return carga.Franjas.Count == 0;
                case FiltroClases.DosDocentes:
                    return leido.Cargas.Any(o => o != carga && !o.Quitada && o.GrupoId == carga.GrupoId && o.MateriaId == carga.MateriaId
                        && (o.ProfesorId != carga.ProfesorId || !o.ProfesorId.HasValue));
                default:
                    return true;
            }
        }

        /// <summary>El texto de la ficha de un filtro ("Con choque", "Sin horario"...); vacio en Ninguno.</summary>
        public static string TextoFiltro(FiltroClases filtro)
        {
            switch (filtro)
            {
                case FiltroClases.SinDocente:
                    return "Sin docente";
                case FiltroClases.DeBaja:
                    return "De docentes de baja";
                case FiltroClases.LaboratorioDesactivado:
                    return "En un laboratorio deshabilitado";
                case FiltroClases.Choques:
                    return "Con choque";
                case FiltroClases.Repetidas:
                    return "Repetidas";
                case FiltroClases.SinHorario:
                    return "Sin horario";
                case FiltroClases.DosDocentes:
                    return "Con dos docentes";
                case FiltroClases.Encimadas:
                    return "Encimadas en un laboratorio";
                default:
                    return "";
            }
        }

        private static AvisoDeCarga Rojo(string texto)
        {
            return new AvisoDeCarga { Texto = texto, Bloquea = true };
        }

        private static AvisoDeCarga Ambar(string texto)
        {
            return new AvisoDeCarga { Texto = texto, Bloquea = false };
        }

        /// <summary>
        /// Los choques entre las franjas de esas materias y con las clases ya
        /// programadas, cada par una vez: el mismo dia, a horas que se cruzan, en
        /// laboratorios distintos, con la misma persona docente o con el mismo grupo.
        /// </summary>
        public static List<ChoqueCiclo> Choques(List<CargaCiclo> cargas, List<FranjaInfo> programadas)
        {
            return Cruces(cargas, programadas).Where(c => !c.MismoLaboratorio).ToList();
        }

        /// <summary>Las que se enciman en el mismo laboratorio (de cualquier docente o grupo), cada par una vez.</summary>
        public static List<ChoqueCiclo> Encimadas(List<CargaCiclo> cargas, List<FranjaInfo> programadas)
        {
            return Cruces(cargas, programadas).Where(c => c.MismoLaboratorio).ToList();
        }

        /// <summary>Choques y encimadas juntos (MismoLaboratorio dice cual es cual).</summary>
        private static List<ChoqueCiclo> Cruces(List<CargaCiclo> cargas, List<FranjaInfo> programadas)
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
                    if (EsCruce(choque))
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
                    if (EsCruce(choque))
                    {
                        choques.Add(choque);
                    }
                }
            }

            return choques;
        }

        /// <summary>En el mismo laboratorio, cualquiera; en otro, solo con la misma persona docente o el mismo grupo.</summary>
        private static bool EsCruce(ChoqueCiclo choque)
        {
            return choque.MismoLaboratorio || choque.MismoProfesor || choque.MismoGrupo;
        }

        /// <summary>
        /// Para los dialogos, antes de guardar: con que chocarian esas horas si la
        /// materia fuera de ese profesor y ese grupo (en otro laboratorio: lo del
        /// mismo se reemplaza, Reemplazos). Las de las materias de "excluir" (la
        /// misma que se cambia) no cuentan, ni las franjas de "sinFranjas" (las
        /// que se estan cambiando, en "Cambiar horas").
        /// </summary>
        public static List<string> ChoquesSi(CicloLeido leido, int? profesor, int grupo, IEnumerable<FranjaCiclo> franjas, ICollection<int> excluir,
            ICollection<int> sinFranjas = null)
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
            List<CargaCiclo> otras = leido.Cargas.Where(c => !c.Quitada && !excluir.Contains(c.Id)).Select(c => SinEsas(c, sinFranjas)).ToList();
            List<string> textos = new List<string>();
            foreach (ChoqueCiclo choque in Choques(new[] { supuesta }.Concat(otras).ToList(), leido.YaProgramadas).Where(c => c.Toca(supuesta)))
            {
                string otra = choque.Programada != null
                    ? choque.Programada.Materia + " con " + choque.Programada.Grupo + " (" + choque.Programada.ProfesorCorto + ", ya está en Clases)"
                    : (choque.CargaA == supuesta ? choque.CargaB : choque.CargaA) == supuesta ? "otro de sus horarios"
                    : (choque.CargaA == supuesta ? choque.CargaB : choque.CargaA).Nombre + " ("
                        + ((choque.CargaA == supuesta ? choque.CargaB : choque.CargaA).ProfesorId.HasValue
                            ? (choque.CargaA == supuesta ? choque.CargaB : choque.CargaA).ProfesorCorto : "sin docente") + ")";
                string laboratorio = choque.Programada != null ? choque.Programada.Laboratorio
                    : choque.CargaA == supuesta ? choque.FranjaB.Laboratorio : choque.FranjaA.Laboratorio;
                textos.Add(HorariosDatos.NombresDia[choque.Dia] + " de " + HorariosDatos.Hora(choque.Inicio) + " a " + HorariosDatos.Hora(choque.Fin)
                    + ": choca con " + otra + " en el " + EstiloHorarios.NombreLaboratorio(laboratorio) + ", " + choque.Motivos + ".");
            }

            return textos.Distinct().ToList();
        }

        /// <summary>
        /// Lo que quedaria de una franja si encima se ponen esas horas: los trozos
        /// de antes y de despues de lo que se cruza en su laboratorio y su dia
        /// (ninguno si la tapan entera). Null si ninguna la toca: se queda igual.
        /// </summary>
        public static List<TimeSpan[]> Resto(FranjaCiclo franja, IEnumerable<FranjaCiclo> encima)
        {
            List<TimeSpan[]> trozos = new List<TimeSpan[]> { new[] { franja.Inicio, franja.Fin } };
            bool tocada = false;
            foreach (FranjaCiclo n in encima.Where(n => n.LaboratorioId == franja.LaboratorioId && n.DiaSemana == franja.DiaSemana))
            {
                List<TimeSpan[]> quedan = new List<TimeSpan[]>();
                foreach (TimeSpan[] t in trozos)
                {
                    if (!(n.Inicio < t[1] && n.Fin > t[0]))
                    {
                        quedan.Add(t);
                        continue;
                    }

                    tocada = true;
                    if (t[0] < n.Inicio)
                    {
                        quedan.Add(new[] { t[0], n.Inicio });
                    }

                    if (n.Fin < t[1])
                    {
                        quedan.Add(new[] { n.Fin, t[1] });
                    }
                }

                trozos = quedan;
            }

            return tocada ? trozos : null;
        }

        /// <summary>
        /// En la preparacion, lo que haria poner esas horas a esa clase: a que
        /// franjas de OTRAS clases del mismo laboratorio se les quitaria lo que se
        /// cruza (como Horario semanal con db/25: lo que se les quita queda en su
        /// "Falta asignar", CicloPreliminarDatos.RecortarEncimadas). Sin las
        /// franjas de "sinFranjas" (la que se mueve con "Cambiar horas"). Lo de la
        /// misma persona docente o el mismo grupo en otro laboratorio no se
        /// reemplaza: es un choque (ChoquesSi).
        /// </summary>
        public static List<FranjaReemplazada> Reemplazos(CicloLeido leido, int idCarga, IList<FranjaCiclo> nuevas, ICollection<int> sinFranjas = null)
        {
            List<FranjaReemplazada> quitadas = new List<FranjaReemplazada>();
            foreach (CargaCiclo otra in leido.Cargas.Where(c => !c.Quitada && c.Id != idCarga))
            {
                foreach (FranjaCiclo f in otra.Franjas.Where(f => sinFranjas == null || !sinFranjas.Contains(f.Id)))
                {
                    foreach (FranjaCiclo n in nuevas.Where(n => n.LaboratorioId == f.LaboratorioId && n.DiaSemana == f.DiaSemana
                        && n.Inicio < f.Fin && n.Fin > f.Inicio))
                    {
                        quitadas.Add(new FranjaReemplazada
                        {
                            FranjaId = f.Id,
                            ClaseId = otra.Id,
                            Materia = otra.Materia,
                            Grupo = otra.Grupo,
                            Profesor = otra.ProfesorId.HasValue ? otra.ProfesorCorto : "sin docente",
                            LaboratorioId = f.LaboratorioId,
                            Laboratorio = f.Laboratorio,
                            DiaSemana = f.DiaSemana,
                            Inicio = f.Inicio,
                            Fin = f.Fin,
                            QuitaInicio = n.Inicio > f.Inicio ? n.Inicio : f.Inicio,
                            QuitaFin = n.Fin < f.Fin ? n.Fin : f.Fin
                        });
                    }
                }
            }

            return quitadas.OrderBy(q => q.DiaSemana).ThenBy(q => q.QuitaInicio).ToList();
        }

        /// <summary>De las clases que pierden horas con esos reemplazos, las que se quedarian sin ninguna (salen en "Sin horario").</summary>
        public static List<CargaCiclo> SeQuedanSinHoras(CicloLeido leido, IList<FranjaCiclo> nuevas, IList<FranjaReemplazada> quitadas)
        {
            List<CargaCiclo> sinHoras = new List<CargaCiclo>();
            foreach (int id in quitadas.Select(q => q.ClaseId).Distinct())
            {
                CargaCiclo carga = leido.Carga(id);
                if (carga != null && carga.Franjas.All(f => quitadas.Any(q => q.FranjaId == f.Id) && Resto(f, nuevas).Count == 0))
                {
                    sinHoras.Add(carga);
                }
            }

            return sinHoras;
        }

        /// <summary>La clase tal cual o, si tiene alguna de esas franjas, una copia sin ellas (para ChoquesSi).</summary>
        private static CargaCiclo SinEsas(CargaCiclo carga, ICollection<int> sinFranjas)
        {
            if (sinFranjas == null || sinFranjas.Count == 0 || !carga.Franjas.Any(f => sinFranjas.Contains(f.Id)))
            {
                return carga;
            }

            CargaCiclo copia = new CargaCiclo
            {
                Id = carga.Id,
                ProfesorId = carga.ProfesorId,
                Profesor = carga.Profesor,
                ProfesorCorto = carga.ProfesorCorto,
                ProfesorActivo = carga.ProfesorActivo,
                GrupoId = carga.GrupoId,
                Grupo = carga.Grupo,
                GrupoCarreraId = carga.GrupoCarreraId,
                MateriaId = carga.MateriaId,
                Materia = carga.Materia
            };
            copia.Franjas.AddRange(carga.Franjas.Where(f => !sinFranjas.Contains(f.Id)));
            return copia;
        }
    }
}
