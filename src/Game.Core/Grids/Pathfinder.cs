namespace Game.Core.Grids;

/// <summary>
/// Recherche de chemin et de cases atteignables sur une <see cref="Grid"/>.
/// Tous les pas coûtent 1 (diagonale comprise), donc un parcours en largeur suffit.
/// Résultat déterministe : l'ordre d'exploration suit <see cref="Grid.Neighbors"/>.
/// <para>
/// <c>occupantAt</c> (optionnel) donne les unités présentes, vues par celle qui se déplace
/// (sa propre case, le départ, est ignorée). On traverse un allié sans pouvoir s'arrêter sur sa case ;
/// un ennemi bloque comme un mur.
/// </para>
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// Plus court chemin de <paramref name="from"/> à <paramref name="to"/>, en cases, sans coupe d'angle.
    /// Le chemin exclut la case de départ et inclut la destination ; il est vide si <c>from == to</c>.
    /// Renvoie <c>null</c> si la destination est inaccessible, à plus de <paramref name="maxSteps"/> pas
    /// (la mobilité de l'unité), bloquée, occupée, ou si le départ est bloqué.
    /// </summary>
    public static IReadOnlyList<Cell>? FindPath(
        Grid grid, Cell from, Cell to, int maxSteps = int.MaxValue, Func<Cell, Occupant>? occupantAt = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSteps);

        if (grid.IsBlocked(from) || grid.IsBlocked(to))
            return null;
        if (from == to)
            return Array.Empty<Cell>();
        if (IsOccupied(to, occupantAt))
            return null;

        var visited = Search(grid, from, maxSteps, occupantAt, goal: to);
        if (!visited.ContainsKey(to))
            return null;

        var path = new List<Cell>(visited[to].Distance);
        for (var cell = to; cell != from; cell = visited[cell].Parent)
            path.Add(cell);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Toutes les cases où l'unité peut s'arrêter en au plus <paramref name="maxSteps"/> pas, avec leur distance
    /// (la case de départ est incluse, à distance 0 ; les cases d'alliés traversées sont exclues).
    /// Vide si le départ est bloqué.
    /// L'ordre d'énumération est garanti : distance croissante, puis ordre d'exploration.
    /// </summary>
    public static IReadOnlyDictionary<Cell, int> ReachableCells(
        Grid grid, Cell from, int maxSteps, Func<Cell, Occupant>? occupantAt = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSteps);

        var result = new OrderedDictionary<Cell, int>();
        if (grid.IsBlocked(from))
            return result;

        foreach (var (cell, node) in Search(grid, from, maxSteps, occupantAt, goal: null))
        {
            if (cell == from || !IsOccupied(cell, occupantAt))
                result[cell] = node.Distance;
        }
        return result;
    }

    private static bool IsOccupied(Cell cell, Func<Cell, Occupant>? occupantAt) =>
        occupantAt is not null && occupantAt(cell) != Occupant.None;

    // Les cases visitées, dans l'ordre de leur découverte.
    private static OrderedDictionary<Cell, (Cell Parent, int Distance)> Search(
        Grid grid, Cell from, int maxSteps, Func<Cell, Occupant>? occupantAt, Cell? goal)
    {
        var visited = new OrderedDictionary<Cell, (Cell Parent, int Distance)> { [from] = (from, 0) };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var distance = visited[current].Distance;
            if (distance == maxSteps)
                continue;

            foreach (var next in grid.Neighbors(current, occupantAt))
            {
                if (visited.ContainsKey(next))
                    continue;

                visited[next] = (current, distance + 1);
                if (next == goal)
                    return visited;
                queue.Enqueue(next);
            }
        }

        return visited;
    }
}
