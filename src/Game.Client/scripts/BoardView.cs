using Game.Core.Grids;
using Godot;

namespace Game.Client;

/// <summary>
/// Le plateau : une dalle par case, des blocs pour les murs et les obstacles bas, et des calques de surlignage au sol
/// (cases atteignables, chemin, zones). Une case (x, y) est centrée en (x, 0, y), une unité de monde par case.
/// </summary>
public partial class BoardView : Node3D
{
    private static readonly Color FloorA = new(0.32f, 0.33f, 0.36f);
    private static readonly Color FloorB = new(0.29f, 0.30f, 0.33f);
    private static readonly Color WallColor = new(0.55f, 0.52f, 0.48f);
    private static readonly Color ObstacleColor = new(0.45f, 0.36f, 0.25f);

    private static readonly PlaneMesh LayerTile = new() { Size = new Vector2(0.9f, 0.9f) };

    private readonly Dictionary<Cell, Node3D> _obstacles = new();
    private readonly Dictionary<string, (Node3D Node, HashSet<Cell> Cells, Color Color)> _layers = new();

    public static Vector3 ToWorld(Cell cell, float height = 0f) => new(cell.X, height, cell.Y);

    /// <summary>Case sous un point du sol (elle peut être hors de la grille).</summary>
    public static Cell ToCell(Vector3 point) => new(Mathf.RoundToInt(point.X), Mathf.RoundToInt(point.Z));

    public void Build(Grid grid)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var cell = new Cell(x, y);
                AddChild(Box(new Vector3(1f, 0.1f, 1f), ToWorld(cell, -0.05f), (x + y) % 2 == 0 ? FloorA : FloorB));
                switch (grid.TerrainAt(cell))
                {
                    case Terrain.Wall:
                        // Assez bas pour ne pas cacher la rangée de derrière depuis la caméra.
                        AddChild(Box(new Vector3(1f, 0.9f, 1f), ToWorld(cell, 0.45f), WallColor));
                        break;
                    case Terrain.LowObstacle:
                        var obstacle = Box(new Vector3(0.85f, 0.35f, 0.85f), ToWorld(cell, 0.175f), ObstacleColor);
                        AddChild(obstacle);
                        _obstacles[cell] = obstacle;
                        break;
                }
            }
    }

    /// <summary>Un obstacle bas détruit disparaît (la case devient du sol).</summary>
    public void RemoveObstacle(Cell cell)
    {
        if (_obstacles.Remove(cell, out var obstacle))
            obstacle.QueueFree();
    }

    /// <summary>
    /// Remplace le calque <paramref name="layer"/> par des dalles translucides sur <paramref name="cells"/>
    /// (vide : le calque disparaît). <paramref name="height"/> départage les calques superposés. Appelé à chaque image :
    /// le calque n'est reconstruit que si ses cases ou sa couleur changent.
    /// </summary>
    public void SetLayer(string layer, IEnumerable<Cell> cells, Color color, float height = 0.02f)
    {
        var set = cells.ToHashSet();
        if (_layers.TryGetValue(layer, out var current) && current.Color == color && current.Cells.SetEquals(set))
            return;
        if (_layers.Remove(layer, out var previous))
            previous.Node.QueueFree();
        var node = new Node3D { Name = layer };
        var material = Translucent(color);
        foreach (var cell in set)
            node.AddChild(new MeshInstance3D { Mesh = LayerTile, MaterialOverride = material, Position = ToWorld(cell, height) });
        AddChild(node);
        _layers[layer] = (node, set, color);
    }

    public void ClearLayer(string layer) => SetLayer(layer, Array.Empty<Cell>(), Colors.White);

    public static StandardMaterial3D Translucent(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Color color) => new()
    {
        Mesh = new BoxMesh { Size = size },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
        Position = position,
    };
}
