using Game.Core.Network;
using Game.Core.World;
using Godot;

namespace Game.Client;

/// <summary>
/// La carte du monde, dessinée (affichage minimal, à polir plus tard) : le QG, les lieux de mission, et les points
/// d'intérêt découverts, reliés par leurs chemins. Un point mystérieux reste un « ? » tant qu'il n'est pas visité ; un
/// point épuisé est grisé. Pendant un trajet : le chemin parcouru, le lieu où est le groupe, sa destination, et les lieux
/// où il peut aller.
/// </summary>
public partial class WorldMapView : Control
{
    private const float Margin = 40;
    private static readonly Color Background = new(0.12f, 0.14f, 0.18f);
    private static readonly Color PathColor = new(0.4f, 0.42f, 0.48f);
    private static readonly Color TravelledColor = new(1f, 0.8f, 0.3f);
    private static readonly Color MoveColor = new(0.5f, 1f, 0.5f);

    private readonly WorldMap _map;
    private CampaignInfo? _campaign;

    public WorldMapView(WorldMap map)
    {
        _map = map;
        CustomMinimumSize = new Vector2(640, 360);
    }

    // Godot exige un constructeur sans paramètre pour les nœuds C#.
    public WorldMapView() : this(new WorldMap { Nodes = Array.Empty<MapNode>(), Paths = Array.Empty<IReadOnlyList<string>>() })
    {
    }

    public void Refresh(CampaignInfo campaign)
    {
        _campaign = campaign;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (_campaign is not { } campaign)
            return;
        var trip = campaign.Trip;
        var shown = _map.Nodes.Where(n => n.Kind != MapNodeKind.Poi || campaign.KnownPois.Contains(n.Id)).ToDictionary(n => n.Id);
        var travelled = trip is null
            ? new HashSet<(string, string)>()
            : trip.Path.Zip(trip.Path.Skip(1)).SelectMany(p => new[] { (p.First, p.Second), (p.Second, p.First) }).ToHashSet();

        foreach (var path in _map.Paths.Where(p => shown.ContainsKey(p[0]) && shown.ContainsKey(p[1])))
        {
            var done = travelled.Contains((path[0], path[1]));
            DrawLine(At(shown[path[0]]), At(shown[path[1]]), done ? TravelledColor : PathColor, done ? 4 : 2, antialiased: true);
        }
        foreach (var node in shown.Values)
            DrawNode(node, campaign, trip);
    }

    private void DrawNode(MapNode node, CampaignInfo campaign, TripInfo? trip)
    {
        var at = At(node);
        var exhausted = campaign.ExhaustedPois.Contains(node.Id);
        if (trip?.Moves.Contains(node.Id) == true)
            DrawCircle(at, 17, MoveColor);
        if (trip?.Current == node.Id)
            DrawCircle(at, 17, TravelledColor);
        var color = node.Kind switch
        {
            MapNodeKind.Hq => new Color(0.4f, 0.6f, 1f),
            MapNodeKind.Site => node.Id == trip?.Site ? new Color(1f, 0.35f, 0.3f) : new Color(0.75f, 0.45f, 0.4f),
            _ => exhausted ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.85f, 0.75f, 0.4f),
        };
        DrawCircle(at, 12, color);

        // Un point mystérieux garde son secret jusqu'à la visite (le groupe y est, ou y est passé).
        var hidden = node.Mysterious && !exhausted && trip?.Current != node.Id;
        var label = hidden ? "?" : node.Name + (node.Id == trip?.Site ? " (la mission)" : "");
        var font = ThemeDB.FallbackFont;
        DrawString(font, at + new Vector2(-100, 32), label, HorizontalAlignment.Center, 200, 14, exhausted ? Colors.Gray : Colors.White);
    }

    private Vector2 At(MapNode node) =>
        new(Margin + node.X / 100f * (Size.X - 2 * Margin), Margin + node.Y / 100f * (Size.Y - 2 * Margin));
}
