using Game.Core.Grids;
using Game.Core.Scripts;

namespace Game.Core.Combat;

// Actions du script : repérer les unités, trouver l'activation qui fait l'action, et les actions ordonnées (un allié
// du jeu qui n'agit que sur ordre, le coup scénarisé d'un ennemi).
public sealed partial class CombatSimulation
{
    private Unit? Tagged(string tag) => _units.Find(u => u.Tag == tag);

    /// <summary>
    /// L'action, ses repères remplacés par les unités du combat ; <c>null</c> si l'unité qui agit n'est pas (encore) sur la
    /// carte. Une cible absente reste <c>null</c> (l'action est alors impossible).
    /// </summary>
    private ResolvedAction? Resolve(ScriptAction action) =>
        Tagged(action.Unit) is { } unit
            ? new ResolvedAction(unit.Id, action.Skill, action.Target is { } tag ? Tagged(tag)?.Id : null, action.Cell, action.MoveTo)
            : null;

    /// <summary>
    /// Chaque unité fait aussitôt l'action ordonnée, après la réplique éventuelle (<paramref name="say"/>) : sans attendre
    /// sa jauge ni la recharge de la compétence, mais selon toutes les autres règles (cf.
    /// <see cref="ValidateActivation"/>). Absente, impossible, ou annulée en chemin (fuite, état reçu sur une attaque
    /// d'opportunité) : <see cref="ScriptedActionFailed"/>, et le script continue. Sa jauge repart comme après toute action.
    /// </summary>
    private void Act(IReadOnlyList<ScriptAction> actions, string? speaker, string? say)
    {
        if (say is not null)
            Show(speaker, say, freezes: false);
        foreach (var action in actions)
        {
            var resolved = Resolve(action);
            var unit = resolved is null ? null : FindUnit(resolved.Unit)!;
            var targetMissing = action.Target is not null && resolved?.Target is null;
            if (unit is null || targetMissing || FirstPlanFor(resolved!, unit, scriptOrder: true) is not { } plan)
            {
                _events.Add(new ScriptedActionFailed(CurrentTick, action.Unit, unit?.Id));
                continue;
            }
            var activations = unit.Activations;
            ApplyActivation(plan);
            if (plan.Action is not null && unit.Activations == activations)
                _events.Add(new ScriptedActionFailed(CurrentTick, action.Unit, unit.Id));
        }
    }

    /// <summary>
    /// Ces unités quittent aussitôt le combat, après la réplique éventuelle : retirées, enfuies et non vaincues (comme un
    /// fuyard qui atteint le bord ; un chef qui part ainsi entraîne ses sbires). Absente ou déjà hors de combat :
    /// <see cref="ScriptedActionFailed"/>, et le script continue.
    /// </summary>
    private void Leave(IReadOnlyList<string> tags, string? speaker, string? say)
    {
        if (say is not null)
            Show(speaker, say, freezes: false);
        foreach (var tag in tags)
        {
            if (Tagged(tag) is { Status: UnitStatus.Active } unit)
                Escape(unit);
            else
                _events.Add(new ScriptedActionFailed(CurrentTick, tag, Tagged(tag)?.Id));
        }
    }

    /// <summary>
    /// Une activation valide qui fait l'action, ou <c>null</c> s'il n'y en a aucune : sur place d'abord, puis depuis
    /// chaque case atteignable (dans l'ordre du calcul des chemins, déterministe) quand le déplacement est libre.
    /// <paramref name="scriptOrder"/> : un ordre du script, sans attendre la jauge ni la recharge.
    /// </summary>
    private ActivationPlan? FirstPlanFor(ResolvedAction action, Unit unit, bool scriptOrder)
    {
        if (unit.Status != UnitStatus.Active)
            return null;
        var use = action.Skill is { } skill ? new SkillUse(skill, action.Target, action.Cell) : null;
        foreach (var moveTo in Destinations(action, unit))
        {
            if (ValidateActivation(new ActivateCommand(unit.Id, moveTo, use), scriptOrder: scriptOrder).Plan is { } plan)
                return plan;
        }
        return null;
    }

    private IEnumerable<Cell?> Destinations(ResolvedAction action, Unit unit)
    {
        if (action.MoveTo is { } imposed)
        {
            yield return imposed;
            yield break;
        }
        yield return null;
        foreach (var cell in ReachableCells(unit.Id).Keys)
        {
            if (cell != unit.Position)
                yield return cell;
        }
    }
}
