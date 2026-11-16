using Game.Core.Grids;

namespace Game.Core.Combat;

// Ce qu'une unité fait pour son camp : relever un allié à distance (Réanimation express), lui rendre ses compétences
// (Rechargement), faire frapper ses alliés (Frappe coordonnée), les couvrir (Diversion), lire l'adversaire (Analyse
// tactique).
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Réanimation express, depuis <paramref name="position"/> : un allié à terre (pas un civil), joignable à pied en
    /// <see cref="SkillDefinition.Range"/> pas au plus, comme une ruée. <see cref="PlannedAction.Destination"/> : la case
    /// où le lanceur s'arrête, à son contact.
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanExpressRevive(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        if (use.Target is not { } allyId || use.Cell is not null || FindUnit(allyId) is not { Status: UnitStatus.Downed } ally
            || ally.Team != unit.Team || ally.IsCivilian)
            return (null, RejectionReason.InvalidTarget);
        return DashEnd(unit, position, ally, skill.Range) is { } end
            ? (new PlannedAction(skill, ally, null, end), null)
            : (null, RejectionReason.OutOfRange);
    }

    /// <summary>Le lanceur court au contact de l'allié à terre (sans attaque d'opportunité), puis le relève s'il le peut encore.</summary>
    private void ExpressRevive(Unit caster, SkillDefinition skill, Unit ally, Cell end)
    {
        _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, ally.Id));
        if (caster.Position != end)
            SelfDisplace(caster, end);
        if (CanStillAct(caster) && ally.Status == UnitStatus.Downed)
            Revive(caster, ally);
    }

    /// <summary>Rechargement : les recharges en cours de l'unité sont annulées.</summary>
    private void ResetCooldowns(Unit unit)
    {
        if (!unit.HasCooldownRunning(CurrentTick))
            return;
        unit.ClearCooldowns();
        _events.Add(new CooldownsReset(CurrentTick, unit.Id));
    }

    /// <summary>
    /// Frappe coordonnée : les alliés de <paramref name="caster"/> qui frapperaient <paramref name="target"/> de leur
    /// attaque de base, là où ils sont : debout, ni étourdis ni en train de charger, la cible à portée et en vue
    /// (dans l'ordre des unités). Seule une attaque de base ordinaire compte : un coup sur une cible, instantané, sans
    /// zone, chaîne ni mouvement du lanceur.
    /// </summary>
    private IEnumerable<Unit> AlliesStriking(Unit caster, Unit target) =>
        _units.Where(u => u != caster && u.Team == caster.Team && !u.IsCivilian && u.Status == UnitStatus.Active
                          && !u.HasStatus(StatusKind.Stunned) && u.Charge is null
                          && u.BasicAttack is { Zone: null, IsCharged: false, SelfMove: null, Chain: null } basic
                          && CheckTarget(u, u.Position, target, basic) is null);

    /// <summary>
    /// Chaque allié qui le peut porte aussitôt son attaque de base à la cible désignée (avec son déplacement forcé
    /// éventuel), sans toucher à sa jauge ; chaque coup est une source à part (un dôme n'en bloque qu'un).
    /// </summary>
    private void CoordinatedStrike(Unit caster, SkillDefinition skill, Unit target)
    {
        _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, target.Id));
        foreach (var ally in AlliesStriking(caster, target).ToList())
        {
            // Une cible tombée, ou un allié tombé ou étourdi par la riposte d'un coup précédent, arrête la suite.
            if (target.Status != UnitStatus.Active)
                break;
            if (ally.Status != UnitStatus.Active || ally.HasStatus(StatusKind.Stunned))
                continue;
            var basic = ally.BasicAttack!;
            _events.Add(new StrikeJoined(CurrentTick, ally.Id, basic.Id, target.Id));
            var factor = DamageFactorOf(ally);
            if (ResolveHit(ally, basic, target, factor, origin: ally.Position) && target.Status == UnitStatus.Active
                && basic.ForcedMove is { } move)
                ApplyForcedMove(ally, basic, target, move, factor, null);
            EndSource();
        }
    }

    /// <summary>
    /// Diversion : un allié de <paramref name="mover"/> (pas lui-même), debout, avec ce passif, est au contact de
    /// <paramref name="enemy"/>, qui ne peut alors pas punir <paramref name="mover"/>.
    /// </summary>
    private bool IsDistracted(Unit enemy, Unit mover) =>
        _units.Any(u => u != mover && u.Team == mover.Team && u.Status == UnitStatus.Active && u.HasPassive(PassiveKind.Diversion)
                        && IsInMeleeReach(u, enemy.Position));

    /// <summary>
    /// Analyse tactique : les unités dont l'intention est révélée à leurs adversaires, parce qu'un adversaire debout qui
    /// a ce passif est à <see cref="CombatRules.TacticalAnalysisRange"/> cases au plus (sans ligne de vue). Calculées
    /// en une fois (l'affichage les demande à chaque image).
    /// </summary>
    public IReadOnlySet<UnitId> RevealedIntentions()
    {
        var analysts = _units.Where(u => u.Status == UnitStatus.Active && u.HasPassive(PassiveKind.TacticalAnalysis)).ToList();
        return analysts.Count == 0
            ? new HashSet<UnitId>()
            : _units.Where(u => analysts.Any(a => AreOpponents(a, u) && IsNear(a, u, PassiveKind.TacticalAnalysis, Rules.TacticalAnalysisRange)))
                .Select(u => u.Id)
                .ToHashSet();
    }
}
