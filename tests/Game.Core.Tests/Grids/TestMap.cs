using Game.Core.Grids;

namespace Game.Core.Tests.Grids;

/// <summary>
/// Carte de test en texte : comme <see cref="Grid.FromRows"/>, plus 'A' (allié) et 'E' (ennemi)
/// sur des cases libres, vus par l'unité qui se déplace.
/// </summary>
internal sealed class TestMap
{
    private readonly Dictionary<Cell, Occupant> _occupants;

    public Grid Grid { get; }

    private TestMap(Grid grid, Dictionary<Cell, Occupant> occupants)
    {
        Grid = grid;
        _occupants = occupants;
    }

    public Occupant OccupantAt(Cell cell) => _occupants.GetValueOrDefault(cell);

    public static TestMap Parse(params string[] rows)
    {
        var occupants = new Dictionary<Cell, Occupant>();
        var terrain = new string[rows.Length];
        for (var y = 0; y < rows.Length; y++)
        {
            var chars = rows[y].ToCharArray();
            for (var x = 0; x < chars.Length; x++)
            {
                var occupant = chars[x] switch
                {
                    'A' => Occupant.Ally,
                    'E' => Occupant.Enemy,
                    _ => Occupant.None,
                };
                if (occupant == Occupant.None)
                    continue;

                occupants[new Cell(x, y)] = occupant;
                chars[x] = '.';
            }
            terrain[y] = new string(chars);
        }

        return new TestMap(Grid.FromRows(terrain), occupants);
    }
}
