using Game.Core.Combat;
using Game.Core.Network;
using Game.Core.Objectives;

namespace Game.Client;

/// <summary>Textes affichés : états, raisons de refus, et une ligne de journal par événement.</summary>
public static class Texts
{
    public static string JoinRefusal(JoinRefusal reason) => reason switch
    {
        Game.Core.Network.JoinRefusal.ProtocolMismatch or Game.Core.Network.JoinRefusal.CodeMismatch =>
            "Version du jeu différente de celle de l'hôte : comparez vos versions (en bas du menu) et prenez tous le dernier zip.",
        Game.Core.Network.JoinRefusal.ContentMismatch => "Données du jeu (data/) différentes de celles de l'hôte : mettez-vous à jour.",
        Game.Core.Network.JoinRefusal.InvalidName => $"Nom invalide (1 à {Protocol.MaxNameLength} caractères).",
        Game.Core.Network.JoinRefusal.NameTaken => "Ce nom est déjà pris par un joueur connecté.",
        Game.Core.Network.JoinRefusal.CombatInProgress => "Un combat est en cours sans toi : attends la fin pour rejoindre.",
        _ => reason.ToString(),
    };

    public static string RequestRefusal(RequestRefusal reason) => reason switch
    {
        Game.Core.Network.RequestRefusal.HeroTaken => "Ce héros est déjà pris par un autre joueur.",
        Game.Core.Network.RequestRefusal.HeroesMissing => "Chaque joueur doit d'abord choisir un héros.",
        Game.Core.Network.RequestRefusal.NotAHero => "Ce n'est pas un héros jouable.",
        Game.Core.Network.RequestRefusal.NotYourUnit => "Ordre refusé : cette unité n'est pas à toi.",
        Game.Core.Network.RequestRefusal.TooManyCommands => "Ordre refusé : trop d'ordres en attente.",
        _ => $"Refusé ({reason}).",
    };

    /// <summary>Les PV d'une unité, avec ses PV temporaires s'il lui en reste : « 120/160 (+40) ».</summary>
    public static string Hp(Unit unit) => unit.TempHp > 0 ? $"{unit.Hp}/{unit.MaxHp} (+{unit.TempHp})" : $"{unit.Hp}/{unit.MaxHp}";

    /// <summary>Ce qu'affiche une unité provoquée, avec le nombre d'activations qu'il lui reste à subir s'il y en a plusieurs.</summary>
    public static string Taunted(Unit unit) => unit.TauntActionsLeft > 1 ? $"provoqué ({unit.TauntActionsLeft})" : "provoqué";

    public static string Guard(GuardAgainst against) =>
        against == GuardAgainst.Melee ? "en garde (corps à corps)" : "en garde (tirs)";

    public static string Status(StatusKind status) => status switch
    {
        StatusKind.Stunned => "étourdi",
        StatusKind.OffBalance => "déséquilibré",
        StatusKind.Hasted => "accéléré",
        StatusKind.Empowered => "surpuissant",
        StatusKind.Exposed => "vulnérable",
        StatusKind.Confused => "confus",
        StatusKind.Dominated => "dominé",
        StatusKind.Immobilized => "immobilisé",
        StatusKind.Disarmed => "désarmé",
        StatusKind.Serene => "serein",
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
        RejectionReason.Immobilized => "immobilisé",
        RejectionReason.Disarmed => "désarmé",
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
        RejectionReason.NoScenery => "pas d'obstacle à prendre",
        _ => reason.ToString(),
    };

    /// <summary>Ce que demande un objectif : « Éliminer tous les ennemis ».</summary>
    public static string Objective(ObjectiveDefinition objective) => objective.Kind switch
    {
        ObjectiveKind.Eliminate => "Éliminer tous les ennemis",
        _ => objective.Kind.ToString(),
    };

    /// <summary>Un objectif et où il en est, pour le panneau d'état : « Éliminer tous les ennemis : réussi ».</summary>
    public static string ObjectiveLine(Objective objective)
    {
        var secondary = objective.Definition.Secondary ? " (secondaire)" : "";
        var state = objective.State switch
        {
            ObjectiveState.Completed => " : réussi",
            ObjectiveState.Failed => " : raté",
            _ => "",
        };
        return $"- {Objective(objective.Definition)}{secondary}{state}";
    }

    /// <summary>Ligne de journal pour un événement, ou <c>null</c> pour ceux qui ne méritent pas d'y figurer.</summary>
    public static string? Describe(CombatEvent e, CombatSimulation sim)
    {
        string N(UnitId id) => sim.FindUnit(id)?.Definition.Name ?? $"#{id.Value}";
        string S(UnitId unit, string skill) => sim.FindUnit(unit)?.FindSkill(skill)?.DisplayName ?? skill;
        string O(int index) => Objective(sim.Objectives[index].Definition);

        return e switch
        {
            ObjectiveCompleted o => $"Objectif réussi : {O(o.Objective)}",
            ObjectiveFailed o => $"Objectif raté : {O(o.Objective)}",
            SkillUsed u => u.Target is { } target && target != u.Unit
                ? $"{N(u.Unit)} : {S(u.Unit, u.SkillId)} sur {N(target)}"
                : $"{N(u.Unit)} : {S(u.Unit, u.SkillId)}",
            StrikeJoined j => $"{N(j.Unit)} frappe {N(j.Target)} (frappe coordonnée)",
            ChargeStarted c => sim.FindUnit(c.Unit)?.FindSkill(c.SkillId)?.HoldsDome == true
                ? $"{N(c.Unit)} : {S(c.Unit, c.SkillId)}"
                : $"{N(c.Unit)} charge {S(c.Unit, c.SkillId)}…",
            ChargeInterrupted c => $"{N(c.Unit)} : {S(c.Unit, c.SkillId)} interrompue",
            ChargeMissed c => $"{N(c.Unit)} : {S(c.Unit, c.SkillId)} manque sa cible",
            UnitDamaged d => $"{N(d.Unit)} perd {d.Amount} PV ({d.Hp})",
            UnitHealed h => $"{N(h.Unit)} regagne {h.Amount} PV ({h.Hp})",
            TempHpGained g => $"{N(g.Unit)} gagne {g.Amount} PV temporaires",
            DamageAbsorbed a => $"{N(a.Unit)} encaisse {a.Amount} (PV temporaires : {a.TempHpLeft})",
            TempHpEnded x => $"{N(x.Unit)} perd ses PV temporaires",
            GaugeLost g => $"{N(g.Unit)} perd de la jauge",
            CooldownsReset c => $"{N(c.Unit)} : recharges terminées",
            StatusSpread s => $"Contagion : {N(s.To)} est {Status(s.Status)} à son tour",
            GuardianAngelSaved g => $"{N(g.Unit)} tient debout grâce à {N(g.By)} (ange gardien)",
            ChainExplosion => "Réaction en chaîne !",
            UnitTaunted t => $"{N(t.Unit)} est provoqué par {N(t.By)}",
            TauntEnded t => $"{N(t.Unit)} n'est plus provoqué",
            UnitDowned d => $"{N(d.Unit)} est à terre !",
            UnitRemoved r => $"{N(r.Unit)} est hors de combat",
            UnitEscaped x => sim.FindUnit(x.Unit)?.IsCivilian == true ? $"{N(x.Unit)} est évacué" : $"{N(x.Unit)} s'enfuit !",
            UnitFleeing f => $"{N(f.Unit)} prend la fuite",
            LeaderFell l => l.Minions == MinionReaction.Surrender
                ? $"{N(l.Leader)} est tombé : ses hommes se rendent !"
                : $"{N(l.Leader)} est tombé : ses hommes prennent la fuite !",
            UnitSurrendered s => $"{N(s.Unit)} se rend",
            UnitRevived r => $"{N(r.By)} relève {N(r.Unit)} ({r.Hp} PV)",
            UnitRescued r => $"{N(r.By)} met {N(r.Unit)} à l'abri",
            StatusApplied s => $"{N(s.Unit)} : {Status(s.Status)}",
            OpportunityAttack o => $"Attaque d'opportunité de {N(o.Attacker)} sur {N(o.Target)}",
            UnitExecuted x => $"{N(x.Attacker)} exécute {N(x.Target)}",
            HitBlocked b => $"Le dôme protège {N(b.Unit)}",
            HitCovered c => $"{N(c.Unit)} est à couvert (−{c.Absorbed})",
            CoverDestroyed => "Un obstacle est détruit",
            SceneryTaken t => t.To is null ? $"{N(t.Unit)} arrache un obstacle et le lance" : $"{N(t.Unit)} déplace un obstacle",
            TrapTriggered t => $"{N(t.Unit)} déclenche le piège de {N(t.Owner)}",
            AttackParried p => $"{N(p.Unit)} pare l'attaque de {N(p.Attacker)}",
            AttackDodged d => $"{N(d.Unit)} esquive l'attaque de {N(d.Attacker)}",
            AttackIntercepted i => $"{N(i.Guard)} s'interpose devant {N(i.Ally)}",
            CounterAttack c => $"{N(c.Unit)} riposte contre {N(c.Target)}",
            UnitBlewUp b => $"{N(b.Unit)} explose !",
            WatchFired w => $"{N(w.Owner)} tire sur {N(w.Target)} (surveillance)",
            UnitResisted r => $"{N(r.Unit)} résiste",
            UnitSummoned s => $"{N(s.Owner)} pose : {N(s.Unit)}",
            TeamChanged t => $"{N(t.Unit)} change de camp",
            UnitCollided c => c.With is { } with ? $"{N(c.Unit)} percute {N(with)}" : $"{N(c.Unit)} percute le décor",
            CommandRejected r => $"{N(r.Command.Unit)} : refusé ({Rejection(r.Reason)})",
            OrderCancelled o => $"{N(o.Unit)} : ordre annulé",
            _ => null,
        };
    }
}
