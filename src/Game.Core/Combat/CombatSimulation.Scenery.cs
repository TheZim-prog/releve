using Game.Core.Grids;

namespace Game.Core.Combat;

// Décor pris par une compétence : un obstacle bas arraché, puis lancé (Lancer de décor) ou posé ailleurs (Barricade).
public sealed partial class CombatSimulation
{
    /// <summary>
    /// L'obstacle bas que <paramref name="unit"/>, depuis <paramref name="position"/>, prendrait pour viser
    /// <paramref name="aimed"/> : à sa portée (au contact sans coupe d'angle, ou à distance et en vue), autre que la case
    /// visée, le plus près d'elle, puis du lanceur, puis dans l'ordre des cases. <c>null</c> s'il n'y en a pas.
    /// </summary>
    private Cell? SceneryFor(Unit unit, Cell position, Cell aimed, SceneryDefinition scenery)
    {
        Cell? best = null;
        var reach = scenery.Reach;
        for (var y = position.Y - reach; y <= position.Y + reach; y++)
        {
            for (var x = position.X - reach; x <= position.X + reach; x++)
            {
                var cell = new Cell(x, y);
                if (cell == aimed || !_grid.Contains(cell) || _grid.TerrainAt(cell) != Terrain.LowObstacle)
                    continue;
                // La vue (le plus coûteux) seulement pour un obstacle mieux placé que le meilleur déjà vu.
                var better = best is not { } current
                    || (cell.DistanceTo(aimed), cell.DistanceTo(position)).CompareTo((current.DistanceTo(aimed), current.DistanceTo(position))) < 0;
                if (better && SeesObstacle(unit, position, cell))
                    best = cell;
            }
        }
        return best;
    }

    /// <summary>
    /// Vue d'un obstacle bas, la ligne de vue ordinaire refusant une case d'arrivée qui n'est pas du sol : au contact,
    /// seuls les coins de mur gênent ; au-delà, les murs, les ennemis debout et les zones au sol qui coupent la vue.
    /// </summary>
    private bool SeesObstacle(Unit unit, Cell from, Cell obstacle)
    {
        var adjacent = from.IsAdjacentTo(obstacle);
        if (!adjacent && GroundBlocksLine(from, obstacle, null))
            return false;
        return LineOfSight.CrossedCells(from, obstacle).All(c => !_grid.BlocksSight(c) && (adjacent || !BlocksSightOf(unit, c)));
    }

    /// <summary>
    /// Le lanceur prend l'obstacle <paramref name="taken"/>, choisi à la validation : il le lance (détruit) ou le pose sur
    /// la case visée. Faux si l'obstacle n'est plus là ou plus à sa portée, ou si la case n'est plus libre (entre la
    /// validation et l'action, un trajet a pu changer les choses) : la compétence ne fait rien.
    /// </summary>
    private bool TakeSceneryFor(Unit caster, SceneryDefinition scenery, Cell aimed, Cell taken)
    {
        var position = caster.Position;
        if (_grid.TerrainAt(taken) != Terrain.LowObstacle || position.DistanceTo(taken) > scenery.Reach
            || !SeesObstacle(caster, position, taken))
            return false;
        var placed = scenery.Use == SceneryUse.Place;
        if (placed && !CanRaiseOn(caster, position, aimed))
            return false;
        TakeScenery(caster, taken, placed ? aimed : null);
        return true;
    }

    /// <summary>
    /// Le lanceur arrache l'obstacle bas de <paramref name="from"/> : lancé, il est détruit (sa case redevient du sol) ;
    /// posé sur <paramref name="to"/>, il y arrive avec ses PV et, s'il était dressé pour un temps, son échéance.
    /// </summary>
    private void TakeScenery(Unit caster, Cell from, Cell? to)
    {
        var hp = _coverHp.GetValueOrDefault(from, -1);
        if (to is null) // lancé : il est détruit
            NoteSceneryDestroyed(caster, from);
        var raised = _raisedObstacles.FindIndex(o => o.Cell == from);
        var endTick = raised >= 0 ? _raisedObstacles[raised].EndTick : (int?)null;
        ForgetRaisedObstacle(from);
        _grid.SetTerrain(from, Terrain.Floor);
        _events.Add(new SceneryTaken(CurrentTick, caster.Id, from, to));
        if (to is not { } spot)
            return;
        _grid.SetTerrain(spot, Terrain.LowObstacle);
        if (hp >= 0)
            _coverHp[spot] = hp;
        if (endTick is { } end)
            _raisedObstacles.Add((spot, end));
    }
}
