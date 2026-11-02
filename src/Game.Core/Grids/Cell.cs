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

    /// <summary>
    /// Direction (l'une des 8, composantes -1, 0 ou 1) vers une case alignée (même ligne, même colonne ou diagonale),
    /// ou <c>null</c> si elle ne l'est pas ou si c'est la même case.
    /// </summary>
    public (int X, int Y)? DirectionTo(Cell other)
    {
        int dx = other.X - X, dy = other.Y - Y;
        var aligned = dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy);
        return aligned && this != other ? (Math.Sign(dx), Math.Sign(dy)) : null;
    }

    public override string ToString() => $"({X},{Y})";
}
