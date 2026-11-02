namespace Game.Core.Grids;

/// <summary>Ce qu'il y a sur une case de la grille (hors unités).</summary>
public enum Terrain : byte
{
    Floor,

    /// <summary>Bloque le déplacement, la vue et le saut.</summary>
    Wall,

    /// <summary>Obstacle bas (ex. une voiture) : bloque le déplacement, mais pas la vue ; on peut le sauter.</summary>
    LowObstacle,
}

/// <summary>
/// Grille de cases carrées. Une case est libre, un mur ou un obstacle bas. Hors de la grille, tout est
/// considéré comme un mur.
/// Vue de l'extérieur de Game.Core, une grille est immuable : seule la simulation change le décor,
/// sinon un combat ne pourrait plus être rejoué à l'identique.
///
/// Règles de déplacement (docs/design.md, section 3) : la diagonale coûte 1 case,
/// mais il n'y a pas de coupe d'angle (voir <see cref="CanStep"/>).
/// </summary>
public sealed class Grid
{
    // Ordre fixe, partie du déterminisme : orthogonales (haut, droite, bas, gauche)
    // puis diagonales (haut-droite, bas-droite, bas-gauche, haut-gauche).
    private static readonly (int Dx, int Dy)[] NeighborOffsets =
    {
        (0, -1), (1, 0), (0, 1), (-1, 0),
        (1, -1), (1, 1), (-1, 1), (-1, -1),
    };

    private readonly Terrain[] _terrain;

    public int Width { get; }
    public int Height { get; }

    public Grid(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _terrain = new Terrain[checked(width * height)];
    }

    /// <summary>
    /// Construit une grille depuis des lignes de texte : '.' = libre, '#' = mur, 'o' = obstacle bas.
    /// Toutes les lignes doivent avoir la même longueur. La première ligne est Y = 0.
    /// </summary>
    public static Grid FromRows(params string[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Length == 0)
            throw new ArgumentException("At least one row is required.", nameof(rows));
        if (Array.IndexOf(rows, null) is var nullRow and >= 0)
            throw new ArgumentException($"Row {nullRow} is null.", nameof(rows));

        var grid = new Grid(rows[0].Length, rows.Length);
        for (var y = 0; y < rows.Length; y++)
        {
            if (rows[y].Length != grid.Width)
                throw new ArgumentException($"Row {y} has length {rows[y].Length}, expected {grid.Width}.", nameof(rows));

            for (var x = 0; x < rows[y].Length; x++)
            {
                grid.SetTerrain(new Cell(x, y), rows[y][x] switch
                {
                    '.' => Terrain.Floor,
                    '#' => Terrain.Wall,
                    'o' => Terrain.LowObstacle,
                    var other => throw new ArgumentException($"Unknown character '{other}' at ({x},{y}).", nameof(rows)),
                });
            }
        }

        return grid;
    }

    /// <summary>Copie indépendante (le décor d'une copie peut changer sans toucher l'original).</summary>
    internal Grid Clone()
    {
        var copy = new Grid(Width, Height);
        Array.Copy(_terrain, copy._terrain, _terrain.Length);
        return copy;
    }

    public bool Contains(Cell cell) =>
        cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

    /// <summary>Terrain de la case ; hors de la grille, un mur.</summary>
    public Terrain TerrainAt(Cell cell) => Contains(cell) ? _terrain[Index(cell)] : Terrain.Wall;

    /// <summary>Vrai si on ne peut pas se tenir ni passer sur la case : hors de la grille, mur ou obstacle bas.</summary>
    public bool IsBlocked(Cell cell) => TerrainAt(cell) != Terrain.Floor;

    public bool IsWalkable(Cell cell) => !IsBlocked(cell);

    /// <summary>Vrai si la case bloque la vue (tirs, zones) et le saut : un mur ou hors de la grille.</summary>
    public bool BlocksSight(Cell cell) => TerrainAt(cell) == Terrain.Wall;

    /// <summary>
    /// La vue passe-t-elle d'une case à sa voisine ? Pas de mur sur la case d'arrivée ni, en diagonale, sur l'un des
    /// deux coins frôlés (pas de coupe d'angle). Les obstacles bas ne gênent pas.
    /// </summary>
    public bool CanSeeStep(Cell from, Cell to) =>
        from.IsAdjacentTo(to) && !BlocksSight(to)
        && (!from.IsDiagonalTo(to) || (!BlocksSight(new Cell(from.X, to.Y)) && !BlocksSight(new Cell(to.X, from.Y))));

    internal void SetTerrain(Cell cell, Terrain terrain)
    {
        if (!Contains(cell))
            throw new ArgumentOutOfRangeException(nameof(cell), cell, "Cell is outside the grid.");
        _terrain[Index(cell)] = terrain;
    }

    /// <summary>Raccourci : mur ou sol.</summary>
    internal void SetBlocked(Cell cell, bool blocked) => SetTerrain(cell, blocked ? Terrain.Wall : Terrain.Floor);

    /// <summary>
    /// Vrai si une unité peut passer de <paramref name="from"/> à <paramref name="to"/> en un pas :
    /// cases adjacentes (diagonale comprise), toutes deux libres, et, pour une diagonale,
    /// <b>sans coupe d'angle</b> : les deux cases orthogonales que le pas frôle doivent être libres.
    /// <para>
    /// <paramref name="occupantAt"/> (optionnel) indique les unités présentes, vues par celle qui se déplace :
    /// un ennemi bloque comme un mur (sa case et les diagonales qui la frôlent), un allié se traverse.
    /// Le pas ne dit pas si l'on peut <i>s'arrêter</i> sur <paramref name="to"/> : voir <see cref="Pathfinder"/>.
    /// </para>
    /// </summary>
    public bool CanStep(Cell from, Cell to, Func<Cell, Occupant>? occupantAt = null)
    {
        if (!from.IsAdjacentTo(to) || IsBlocked(from) || !IsPassable(to, occupantAt))
            return false;

        if (from.IsDiagonalTo(to))
            return IsPassable(new Cell(from.X, to.Y), occupantAt) && IsPassable(new Cell(to.X, from.Y), occupantAt);

        return true;
    }

    /// <summary>Cases où l'on peut faire un pas depuis <paramref name="from"/>, dans un ordre fixe.</summary>
    public IEnumerable<Cell> Neighbors(Cell from, Func<Cell, Occupant>? occupantAt = null)
    {
        foreach (var (dx, dy) in NeighborOffsets)
        {
            var to = new Cell(from.X + dx, from.Y + dy);
            if (CanStep(from, to, occupantAt))
                yield return to;
        }
    }

    private bool IsPassable(Cell cell, Func<Cell, Occupant>? occupantAt) =>
        IsWalkable(cell) && occupantAt?.Invoke(cell) != Occupant.Enemy;

    private int Index(Cell cell) => cell.Y * Width + cell.X;
}
