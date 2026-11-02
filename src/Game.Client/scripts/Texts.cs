using Game.Core.Combat;

namespace Game.Client;

/// <summary>Textes affichés : états, raisons de refus, et une ligne de journal par événement.</summary>
public static class Texts
{
    public static string Status(StatusKind status) => status switch
    {
        StatusKind.Stunned => "étourdi",
        StatusKind.OffBalance => "déséquilibré",
        StatusKind.Hasted => "accéléré",
        StatusKind.Empowered => "surpuissant",
        StatusKind.Exposed => "vulnérable",
        StatusKind.Confused => "confus",
        StatusKind.Dominated => "dominé",
        _ => status.ToString(),
    };

    public static string Rejection(RejectionReason reason) => reason switch
    {
        RejectionReason.UnknownUnit => "unité inconnue",
        RejectionReason.Incapacitated => "hors de combat",
        RejectionReason.NotControllable => "unité non contrôlable",
        RejectionReason.Stunned => "étourdi",
        RejectionReason.Charging => "en train de charger",
        RejectionReason.NotReady => "jauge pas pleine",
        RejectionReason.Empty => "rien à faire",
        RejectionReason.TwoActions => "une seule action à la fois",
        RejectionReason.InvalidDestination => "case invalide",
        RejectionReason.Unreachable => "case inaccessible",
        RejectionReason.UnknownSkill => "compétence inconnue",
        RejectionReason.OnCooldown => "en recharge",
        RejectionReason.InvalidTarget => "cible invalide",
        RejectionReason.NoEffect => "sans effet",
        RejectionReason.OutOfRange => "hors de portée",
        RejectionReason.NoLineOfSight => "pas de ligne de vue",
        _ => reason.ToString(),
    };

    /// <summary>Ligne de journal pour un événement, ou <c>null</c> pour ceux qui ne méritent pas d'y figurer.</summary>
    public static string? Describe(CombatEvent e, CombatSimulation sim)
    {
        string N(UnitId id) => sim.FindUnit(id)?.Definition.Name ?? $"#{id.Value}";
        string S(UnitId unit, string skill) => sim.FindUnit(unit)?.FindSkill(skill)?.DisplayName ?? skill;

        return e switch
        {
            SkillUsed u => u.Target is { } target && target != u.Unit
                ? $"{N(u.Unit)} : {S(u.Unit, u.SkillId)} sur {N(target)}"
                : $"{N(u.Unit)} : {S(u.Unit, u.SkillId)}",
            ChargeStarted c => $"{N(c.Unit)} charge {S(c.Unit, c.SkillId)}…",
            ChargeInterrupted c => $"{N(c.Unit)} : {S(c.Unit, c.SkillId)} interrompue",
            ChargeMissed c => $"{N(c.Unit)} : {S(c.Unit, c.SkillId)} manque sa cible",
            UnitDamaged d => $"{N(d.Unit)} perd {d.Amount} PV ({d.Hp})",
            UnitHealed h => $"{N(h.Unit)} regagne {h.Amount} PV ({h.Hp})",
            UnitDowned d => $"{N(d.Unit)} est à terre !",
            UnitRemoved r => $"{N(r.Unit)} est hors de combat",
            UnitEscaped x => sim.FindUnit(x.Unit)?.IsCivilian == true ? $"{N(x.Unit)} est évacué" : $"{N(x.Unit)} s'enfuit !",
            UnitFleeing f => $"{N(f.Unit)} prend la fuite",
            UnitRevived r => $"{N(r.By)} relève {N(r.Unit)} ({r.Hp} PV)",
            UnitRescued r => $"{N(r.By)} met {N(r.Unit)} à l'abri",
            StatusApplied s => $"{N(s.Unit)} : {Status(s.Status)}",
            OpportunityAttack o => $"Attaque d'opportunité de {N(o.Attacker)} sur {N(o.Target)}",
            UnitExecuted x => $"{N(x.Attacker)} exécute {N(x.Target)}",
            HitBlocked b => $"Le dôme protège {N(b.Unit)}",
            HitCovered c => $"{N(c.Unit)} est à couvert (−{c.Absorbed})",
            CoverDestroyed => "Un obstacle est détruit",
            UnitResisted r => $"{N(r.Unit)} résiste",
            UnitSummoned s => $"{N(s.Owner)} pose : {N(s.Unit)}",
            TeamChanged t => $"{N(t.Unit)} change de camp",
            UnitCollided c => c.With is { } with ? $"{N(c.Unit)} percute {N(with)}" : $"{N(c.Unit)} percute le décor",
            CommandRejected r => $"Refusé : {Rejection(r.Reason)}",
            _ => null,
        };
    }
}
