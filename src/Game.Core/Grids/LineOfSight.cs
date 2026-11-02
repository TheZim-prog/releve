namespace Game.Core.Grids;

/// <summary>
/// Ligne de vue entre deux cases, en entiers : segment du centre de la case de départ au centre
/// de la case d'arrivée. Toute case traversée peut bloquer. <b>Pas de coupe d'angle</b> : quand le segment
/// passe exactement par un coin, les deux cases qui se touchent à ce coin comptent comme traversées.
/// Le résultat est symétrique (A voit B si et seulement si B voit A).
/// </summary>
public static class LineOfSight
{
    /// <summary>
    /// Vrai si rien ne bloque la vue entre <paramref name="from"/> et <paramref name="to"/> :
    /// ni mur (case bloquée de la grille), ni case pour laquelle <paramref name="blocksSight"/> renvoie vrai
    /// (les unités, par exemple). Faux si l'une des deux extrémités est hors de la grille ou bloquée ;
    /// <paramref name="blocksSight"/> n'est pas appliqué aux extrémités (le tireur et sa cible).
    /// </summary>
    public static bool IsClear(Grid grid, Cell from, Cell to, Func<Cell, bool>? blocksSight = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.IsBlocked(from) || grid.IsBlocked(to))
            return false;

        foreach (var cell in CrossedCells(from, to))
        {
            if (grid.IsBlocked(cell) || (blocksSight?.Invoke(cell) ?? false))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Cases traversées strictement entre <paramref name="from"/> et <paramref name="to"/>, dans l'ordre,
    /// y compris les deux cases de chaque coin traversé exactement.
    /// </summary>
    public static IEnumerable<Cell> CrossedCells(Cell from, Cell to)
    {
        int nx = Math.Abs(to.X - from.X), ny = Math.Abs(to.Y - from.Y);
        int sx = Math.Sign(to.X - from.X), sy = Math.Sign(to.Y - from.Y);
        int x = from.X, y = from.Y;

        // ix, iy : nombre de lignes de grille verticales / horizontales déjà franchies.
        // Le signe de « decision » dit laquelle le segment franchit en premier ; 0 = exactement au coin.
        for (int ix = 0, iy = 0; ix < nx || iy < ny;)
        {
            var decision = (long)(1 + 2 * ix) * ny - (long)(1 + 2 * iy) * nx;
            if (decision == 0)
            {
                yield return new Cell(x + sx, y);
                yield return new Cell(x, y + sy);
                x += sx;
                y += sy;
                ix++;
                iy++;
            }
            else if (decision < 0)
            {
                x += sx;
                ix++;
            }
            else
            {
                y += sy;
                iy++;
            }

            if (x == to.X && y == to.Y)
                yield break;
            yield return new Cell(x, y);
        }
    }
}
