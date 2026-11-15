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

    /// <summary>
    /// Celle des 8 directions qui pointe le mieux vers <paramref name="other"/>, alignée ou non (pour pousser une unité
    /// « loin de soi ») ; <c>null</c> si c'est la même case. Droite quand un axe domine nettement (plus de 12/5 de
    /// l'autre, soit un angle de moins de 22,5° environ), diagonale sinon.
    /// </summary>
    public (int X, int Y)? NearestDirectionTo(Cell other)
    {
        int dx = other.X - X, dy = other.Y - Y;
        if (this == other)
            return null;
        int ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (5 * ax > 12 * ay)
            return (Math.Sign(dx), 0);
        if (5 * ay > 12 * ax)
            return (0, Math.Sign(dy));
        return (Math.Sign(dx), Math.Sign(dy));
    }

    public override string ToString() => $"({X},{Y})";
}
