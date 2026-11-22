namespace Game.Core.World;

public enum MapNodeKind
{
    /// <summary>Le QG, d'où partent toutes les quêtes.</summary>
    Hq,

    /// <summary>Un lieu de mission : les quêtes du panneau s'y jouent.</summary>
    Site,

    /// <summary>Un point d'intérêt : un événement sur la route (y passer est un crochet).</summary>
    Poi,
}

/// <summary>
/// Un lieu de la carte du monde : son nom, sa place à l'écran (<see cref="X"/>, <see cref="Y"/>, de 0 à 100), son genre,
/// et pour un point d'intérêt, son événement et s'il cache sa nature (« ? ») jusqu'à la visite.
/// </summary>
public sealed record MapNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required MapNodeKind Kind { get; init; }

    /// <summary>L'événement d'un point d'intérêt (data/world/events.json).</summary>
    public string? Event { get; init; }

    /// <summary>Le point d'intérêt cache sa nature (« ? ») jusqu'à ce qu'on le visite.</summary>
    public bool Mysterious { get; init; }
}

/// <summary>
/// La carte du monde (docs/design.md 4) : le QG, les lieux de mission et les points d'intérêt, reliés par des chemins
/// (sans sens). Une seule carte pour la campagne, que les chapitres enrichiront. En données (data/world/map.json).
/// </summary>
public sealed record WorldMap
{
    public required IReadOnlyList<MapNode> Nodes { get; init; }

    /// <summary>Les chemins : chacun relie deux lieux.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Paths { get; init; }

    public MapNode Hq => Nodes.Single(n => n.Kind == MapNodeKind.Hq);

    public MapNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>Les lieux qu'un chemin relie à celui-ci, dans l'ordre de la carte.</summary>
    public IReadOnlyList<MapNode> Neighbors(string id) =>
        Paths.Where(p => p.Contains(id)).Select(p => p[0] == id ? p[1] : p[0]).Select(n => Node(n)!).ToList();

    public static WorldMap FromJson(string json)
    {
        var map = DataJson.Deserialize<WorldMap>(json);
        map.Validate();
        return map;
    }

    /// <summary>
    /// Un seul QG ; des lieux distincts aux noms non vides ; chaque point d'intérêt a un événement ; des chemins entre deux
    /// lieux connus et distincts ; tout lieu joignable depuis le QG.
    /// </summary>
    public void Validate()
    {
        if (Nodes is null || Paths is null || Nodes.Any(n => n is null) || Paths.Any(p => p is null))
            throw new ArgumentException("The world map needs nodes and paths, without null entries.");
        if (Nodes.Count(n => n.Kind == MapNodeKind.Hq) != 1)
            throw new ArgumentException("The world map needs exactly one HQ.");
        if (Nodes.Select(n => n.Id).Distinct().Count() != Nodes.Count || Nodes.Any(n => string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.Name)))
            throw new ArgumentException("World map nodes need distinct ids and names.");
        if (Nodes.FirstOrDefault(n => (n.Kind == MapNodeKind.Poi) != (n.Event is not null)) is { } odd)
            throw new ArgumentException($"World map node '{odd.Id}': a point of interest has an event, other places do not.");
        foreach (var path in Paths)
        {
            if (path.Count != 2 || path[0] == path[1] || Node(path[0]) is null || Node(path[1]) is null)
                throw new ArgumentException($"A world map path must link two distinct known places ({string.Join(", ", path)}).");
        }
        var reached = new HashSet<string> { Hq.Id };
        var frontier = new Queue<string>(reached);
        while (frontier.TryDequeue(out var id))
        {
            foreach (var next in Neighbors(id).Where(n => reached.Add(n.Id)))
                frontier.Enqueue(next.Id);
        }
        if (Nodes.FirstOrDefault(n => !reached.Contains(n.Id)) is { } lost)
            throw new ArgumentException($"World map node '{lost.Id}' cannot be reached from the HQ.");
    }
}
