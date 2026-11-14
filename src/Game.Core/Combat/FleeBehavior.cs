using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Fuite : son intention est de fuir (aucune compétence, elle-même pour cible) ; elle n'attaque pas et se rapproche à
/// pied du bord de carte le plus proche. Quitter le combat en l'atteignant est géré par la simulation.
/// </summary>
internal sealed class FleeBehavior : IUnitBehavior
{
    public bool ObeysTaunts => false;

    public Intention? ChooseIntention(CombatSimulation sim, Unit unit) => new(null, unit.Id);

    public ActivateCommand? Execute(CombatSimulation sim, Unit unit, Intention intention) => null;

    public ActivateCommand? Approach(CombatSimulation sim, Unit unit, Intention intention)
    {
        if (unit.Reach == 0)
            return null;
        Occupant OccupantFor(Cell cell) => sim.OccupantFor(unit, cell);

        // Le bord le plus proche à pied, puis la case atteignable qui s'en rapproche le plus.
        var fromUnit = Pathfinder.ReachableCells(sim.Grid, unit.Position, int.MaxValue, OccupantFor);
        var exits = fromUnit.Where(c => sim.Grid.IsEdge(c.Key)).ToList();
        if (exits.Count == 0)
            return null;
        var exit = exits.MinBy(c => c.Value).Key;
        var toExit = Pathfinder.ReachableCells(sim.Grid, exit, int.MaxValue, OccupantFor);

        var best = Pathfinder.ReachableCells(sim.Grid, unit.Position, unit.Reach, OccupantFor).Keys
            .Where(c => c != unit.Position && toExit.ContainsKey(c))
            .Select(c => (Cell: c, Distance: toExit[c]))
            .DefaultIfEmpty((Cell: unit.Position, Distance: int.MaxValue))
            .MinBy(c => c.Distance);
        return best.Distance < toExit.GetValueOrDefault(unit.Position, int.MaxValue)
            ? new ActivateCommand(unit.Id, best.Cell)
            : null;
    }
}
