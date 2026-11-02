namespace Game.Core.Grids;

/// <summary>Coordonnées entières d'une case. X vers la droite, Y vers le bas.</summary>
public readonly record struct Cell(int X, int Y)
{
    /// <summary>
    /// Distance en cases, diagonale comprise (une diagonale = 1 case).
    /// Ignore les obstacles : c'est la distance « à vol d'oiseau » sur la grille.
    /// </summary>
    public int DistanceTo(Cell other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public bool IsAdjacentTo(Cell other) => DistanceTo(other) == 1;

    public bool IsDiagonalTo(Cell other) =>
        DistanceTo(other) == 1 && X != other.X && Y != other.Y;

    public override string ToString() => $"({X},{Y})";
}
