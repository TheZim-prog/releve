using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Fuite : son intention est de fuir (aucune compétence, elle-même pour cible) ; elle n'attaque pas et se rapproche à
/// pied de la sortie la plus proche (le bord de carte, ou les sorties de la mission pour le vilain poursuivi). Quitter
/// le combat en l'atteignant est géré par la simulation.
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

        // La sortie libre la plus proche à pied, puis la case atteignable qui s'en rapproche le plus. Le vilain poursuivi
        // dont toutes les sorties sont tenues s'approche quand même de la plus proche, pour s'y glisser dès qu'elle se libère.
        var fromUnit = Pathfinder.ReachableCells(sim.Grid, unit.Position, int.MaxValue, OccupantFor);
        var exits = fromUnit.Where(c => sim.IsExitFor(unit, c.Key)).ToList();
        IReadOnlyDictionary<Cell, int>? toExit = exits.Count > 0
            ? Pathfinder.ReachableCells(sim.Grid, exits.MinBy(c => c.Value).Key, int.MaxValue, OccupantFor)
            : unit.IsPursued
                ? sim.PursuitExits.Select(exit => Pathfinder.ReachableCells(sim.Grid, exit, int.MaxValue, OccupantFor))
                    .Where(paths => paths.ContainsKey(unit.Position))
                    .MinBy(paths => paths[unit.Position])
                : null;
        if (toExit is null)
            return null;

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
