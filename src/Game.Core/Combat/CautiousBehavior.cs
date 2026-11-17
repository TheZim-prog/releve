using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Prudent (une personne à protéger qui ne se bat pas) : son intention est de rester sur ses gardes (aucune compétence,
/// elle-même pour cible) ; elle n'attaque pas (sauf confuse, comme tout le monde). Attaquée par un adversaire depuis sa
/// dernière action, elle recule vers la case atteignable la plus éloignée de l'adversaire le plus proche, si elle y gagne
/// de la distance, par un trajet qui ne provoque pas d'attaque d'opportunité ; sinon elle attend, jauge pleine.
/// </summary>
internal sealed class CautiousBehavior : IUnitBehavior
{
    public bool ObeysTaunts => false;

    public Intention? ChooseIntention(CombatSimulation sim, Unit unit) => new(null, unit.Id);

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention) => null;

    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (!unit.AttackedSinceActivation || unit.Reach == 0)
            return null;
        var threats = sim.Units
            .Where(e => e.Status == UnitStatus.Active && CombatSimulation.AreOpponents(e, unit))
            .Select(e => e.Position)
            .ToList();
        if (threats.Count == 0)
            return null;

        int Distance(Cell cell) => threats.Min(p => p.DistanceTo(cell));
        var current = Distance(unit.Position);
        var best = BasicBehavior.ReachableCells(sim, unit)
            .Select(c => (Cell: c, Distance: Distance(c)))
            .Where(c => c.Distance > current
                        && sim.PathFor(unit.Id, c.Cell) is { } path && !sim.PathProvokesOpportunityAttack(unit, path))
            .DefaultIfEmpty()
            .MaxBy(c => c.Distance); // à égalité, la première dans l'ordre
        return best.Distance > current ? new ActivateCommand(unit.Id, best.Cell) : null;
    }
}
