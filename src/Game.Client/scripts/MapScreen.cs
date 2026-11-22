using Game.Core;
using Game.Core.Network;
using Game.Core.World;
using Godot;

namespace Game.Client;

/// <summary>
/// Le trajet vers une quête (affichage minimal, à polir plus tard) : la carte, les crochets faits et le bonus gagné, ce
/// qu'a donné le dernier événement, l'événement qui attend un choix (options avec le libellé de leur test, ou passer son
/// chemin) et les lieux où aller. Seul le joueur qui valide peut cliquer ; les autres suivent.
/// </summary>
public partial class MapScreen : Control
{
    private readonly GameContent _content;
    private readonly int _me;
    private Label _title = null!;
    private WorldMapView _map = null!;
    private Label _situation = null!;
    private Label _lastEvent = null!;
    private VBoxContainer _choices = null!;
    private Label _status = null!;

    public MapScreen(GameContent content, int me)
    {
        _content = content;
        _me = me;
    }

    // Godot exige un constructeur sans paramètre pour les nœuds C#.
    public MapScreen() : this(GameData.Load(), 0)
    {
    }

    public event Action<string>? TravelPressed;

    /// <summary>Une option de l'événement (son rang), ou <c>null</c> : passer son chemin.</summary>
    public event Action<int?>? OptionPressed;

    public event Action? LeavePressed;

    public override void _Ready()
    {
        var column = Ui.CenteredColumn(this, 680, scrolling: true);
        _title = Ui.Text("", 28, HorizontalAlignment.Center);
        column.AddChild(_title);
        _map = new WorldMapView(_content.World!);
        column.AddChild(_map);
        _situation = Ui.Text("", 15);
        column.AddChild(_situation);
        _lastEvent = Ui.Text("", 15);
        column.AddChild(_lastEvent);
        _choices = new VBoxContainer();
        _choices.AddThemeConstantOverride("separation", 6);
        column.AddChild(_choices);
        column.AddChild(Ui.Button("Quitter la partie", () => LeavePressed?.Invoke(), 32));
        _status = Ui.Text("", 15, HorizontalAlignment.Center);
        column.AddChild(_status);
    }

    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", error ? new Color(1f, 0.5f, 0.45f) : Colors.White);
    }

    public void Refresh(LobbyState lobby)
    {
        if (lobby.Campaign is not { Trip: { } trip } campaign)
            return;
        var quest = _content.Quests.TryGetValue(trip.Quest, out var q) ? q.Name : trip.Quest;
        _title.Text = $"En route : {quest}";
        _map.Refresh(campaign);

        var mine = trip.Validator == _me;
        var validator = lobby.Players.FirstOrDefault(p => p.Id == trip.Validator)?.Name;
        var bonus = TripTexts.Bonus(trip.Modifiers);
        _situation.Text = $"Crochets : {trip.Gravity} (la mission s'aggrave d'autant)."
                          + (bonus.Length > 0 ? $" Bonus pour la quête : {bonus}." : "")
                          + (mine ? " C'est toi qui décides." : validator is null ? " Personne n'est là pour décider." : $" {validator} décide.");
        _lastEvent.Text = campaign.LastEvent is { } last ? TripTexts.Outcome(_content, last) : "";

        foreach (var child in _choices.GetChildren())
            child.QueueFree();
        if (trip.Event is { } pending)
            AddEvent(pending, mine);
        else
            AddMoves(trip, campaign.ExhaustedPois, mine);
    }

    private void AddEvent(EventInfo pending, bool mine)
    {
        if (_content.Events.TryGetValue(pending.Id, out var worldEvent))
        {
            _choices.AddChild(Ui.Text(worldEvent.Title, 20));
            _choices.AddChild(Ui.Text(worldEvent.Text, 15));
        }
        for (var i = 0; i < pending.Options.Count; i++)
        {
            var option = pending.Options[i];
            var index = i;
            var test = option.Difficulty is { } difficulty ? $"  —  test {HqScreen.Difficulty(difficulty)}" : "";
            AddChoice($"{option.Label}{test}", () => OptionPressed?.Invoke(index), mine);
        }
        AddChoice("Passer son chemin (sans rien gagner ni perdre)", () => OptionPressed?.Invoke(null), mine);
    }

    private void AddMoves(TripInfo trip, IReadOnlyList<string> exhausted, bool mine)
    {
        _choices.AddChild(Ui.Text("Où aller ?", 20));
        foreach (var id in trip.Moves)
        {
            if (_content.World!.Node(id) is not { } node)
                continue;
            var what = id == trip.Site ? "la mission : le combat commence"
                : node.Kind == MapNodeKind.Site ? "le lieu d'une autre quête : on ne fait qu'y passer"
                : "un crochet : +1 gravité";
            var name = node.Mysterious && !exhausted.Contains(id) ? "?" : node.Name;
            AddChoice($"Aller à {name} ({what})", () => TravelPressed?.Invoke(id), mine);
        }
    }

    private void AddChoice(string text, Action pressed, bool enabled)
    {
        var button = Ui.Button(text, pressed, 36);
        button.Disabled = !enabled;
        _choices.AddChild(button);
    }
}
