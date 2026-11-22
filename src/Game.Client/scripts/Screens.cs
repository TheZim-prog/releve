using Game.Core.Combat;
using Game.Core.Network;
using Godot;

namespace Game.Client;

/// <summary>Briques d'interface communes aux écrans (menu, salon).</summary>
internal static class Ui
{
    /// <summary>Un panneau centré sur fond sombre, qui contient une colonne.</summary>
    public static VBoxContainer CenteredColumn(Control screen, int width)
    {
        screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(new ColorRect { Color = new Color(0.08f, 0.09f, 0.12f), AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        screen.AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(column);
        return column;
    }

    public static Label Text(string text, int size = 16, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    public static Button Button(string text, Action pressed, int height = 42)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, height), FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += pressed;
        return button;
    }

    public static LineEdit Field(VBoxContainer column, string label, string value, string placeholder = "")
    {
        column.AddChild(Text(label, 14));
        var field = new LineEdit { Text = value, PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 36) };
        field.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(field);
        return field;
    }
}

/// <summary>Écran d'accueil : le scénario, puis solo, héberger une partie, ou en rejoindre une (nom, adresse, port).</summary>
public partial class MenuScreen : Control
{
    private LineEdit _name = null!;
    private LineEdit _address = null!;
    private LineEdit _port = null!;
    private Label _status = null!;
    private readonly List<Button> _buttons = new();
    private OptionButton _scenarioPicker = null!;

    /// <summary>Le joueur a choisi un autre scénario (pour le solo et la partie qu'il hébergerait).</summary>
    public event Action<string>? ScenarioChosen;

    public event Action? SoloPressed;
    public event Action<string, int>? HostPressed;
    public event Action<string, string, int>? JoinPressed;

    public string PlayerName { get; set; } = "";
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = Protocol.DefaultPort;

    /// <summary>Scénarios proposés (identifiant et nom affiché), et celui qui est choisi.</summary>
    public IReadOnlyList<(string Id, string Name)> Scenarios { get; set; } = Array.Empty<(string, string)>();

    public string ScenarioId { get; set; } = "";

    /// <summary>Version du jeu (code et contenu), affichée en bas : deux joueurs doivent avoir la même pour jouer ensemble.</summary>
    public string GameVersion { get; set; } = "";

    public override void _Ready()
    {
        var column = Ui.CenteredColumn(this, 420);
        column.AddChild(Ui.Text("Relève", 40, HorizontalAlignment.Center));
        column.AddChild(Ui.Text("Prototype du combat", 16, HorizontalAlignment.Center));
        column.AddChild(Ui.Text("Scénario (solo et partie hébergée)", 14));
        _scenarioPicker = ScenarioPicker();
        column.AddChild(_scenarioPicker);
        _buttons.Add(Ui.Button("Solo (tu commandes toute l'équipe)", () => SoloPressed?.Invoke()));
        column.AddChild(_buttons[^1]);

        column.AddChild(new HSeparator());
        column.AddChild(Ui.Text("En groupe", 20));
        _name = Ui.Field(column, "Ton nom", PlayerName, "ex. Théo");
        _port = Ui.Field(column, "Port", Port.ToString());
        _buttons.Add(Ui.Button("Héberger la partie", () =>
        {
            if (Validate() is { } port)
                HostPressed?.Invoke(_name.Text.Trim(), port);
        }));
        column.AddChild(_buttons[^1]);
        _address = Ui.Field(column, "Adresse de l'hôte (Tailscale)", Address, "100.x.y.z ou nom de la machine");
        _buttons.Add(Ui.Button("Rejoindre", () =>
        {
            if (Validate() is { } port && _address.Text.Trim() is { Length: > 0 } address)
                JoinPressed?.Invoke(_name.Text.Trim(), address, port);
            else if (_address.Text.Trim().Length == 0)
                SetStatus("Indique l'adresse de l'hôte.", error: true);
        }));
        column.AddChild(_buttons[^1]);
        _status = Ui.Text("", 15, HorizontalAlignment.Center);
        column.AddChild(_status);
        var version = Ui.Text($"Version {GameVersion}", 13, HorizontalAlignment.Center);
        version.Modulate = new Color(1, 1, 1, 0.5f);
        column.AddChild(version);
    }

    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", error ? new Color(1f, 0.5f, 0.45f) : Colors.White);
    }

    private OptionButton ScenarioPicker()
    {
        var picker = new OptionButton { CustomMinimumSize = new Vector2(0, 36), FocusMode = FocusModeEnum.None };
        picker.AddThemeFontSizeOverride("font_size", 16);
        for (var i = 0; i < Scenarios.Count; i++)
        {
            picker.AddItem(Scenarios[i].Name);
            if (Scenarios[i].Id == ScenarioId)
                picker.Selected = i;
        }
        picker.ItemSelected += index =>
        {
            ScenarioId = Scenarios[(int)index].Id;
            ScenarioChosen?.Invoke(ScenarioId);
        };
        return picker;
    }

    /// <summary>Pendant une connexion : boutons et choix du scénario désactivés.</summary>
    public void SetBusy(bool busy)
    {
        _scenarioPicker.Disabled = busy;
        foreach (var button in _buttons)
            button.Disabled = busy;
    }

    private int? Validate()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || name.Length > Protocol.MaxNameLength)
        {
            SetStatus($"Choisis un nom (1 à {Protocol.MaxNameLength} caractères).", error: true);
            return null;
        }
        if (!int.TryParse(_port.Text.Trim(), out var port) || port is <= 0 or > 65535)
        {
            SetStatus("Port invalide.", error: true);
            return null;
        }
        PlayerName = name;
        Address = _address.Text.Trim();
        Port = port;
        return port;
    }
}

/// <summary>
/// Salon : la liste des héros (libre, pris par qui, le sien), les joueurs et leur connexion, et le lancement. Un héros
/// pris par un autre joueur ne peut pas être choisi (le serveur le refuse aussi).
/// </summary>
public partial class LobbyScreen : Control
{
    private readonly IReadOnlyList<UnitSetup> _heroes;
    private readonly int _me;
    private readonly string _hostInfo;
    private VBoxContainer _heroList = null!;
    private Label _players = null!;
    private Label _campaign = null!;
    private Button _academy = null!;
    private Label _status = null!;
    private Button _start = null!;

    public LobbyScreen(IReadOnlyList<UnitSetup> heroes, int me, string hostInfo)
    {
        _heroes = heroes;
        _me = me;
        _hostInfo = hostInfo;
    }

    // Godot exige un constructeur sans paramètre pour les nœuds C#.
    public LobbyScreen() : this(Array.Empty<UnitSetup>(), 0, "")
    {
    }

    public event Action<UnitId?>? HeroPicked;
    public event Action? StartPressed;
    public event Action? LeavePressed;
    public event Action? AcademyPressed;

    /// <summary>Ce jeu sait ouvrir l'académie (son contenu a des arbres de compétences).</summary>
    public bool AcademyAvailable { get; set; }

    public override void _Ready()
    {
        var column = Ui.CenteredColumn(this, 520);
        column.AddChild(Ui.Text("Salon", 32, HorizontalAlignment.Center));
        if (_hostInfo.Length > 0)
            column.AddChild(Ui.Text(_hostInfo, 14, HorizontalAlignment.Center));
        _campaign = Ui.Text("", 16, HorizontalAlignment.Center);
        column.AddChild(_campaign);
        _academy = Ui.Button("Académie (tes héros)", () => AcademyPressed?.Invoke(), 36);
        _academy.Visible = false;
        column.AddChild(_academy);
        column.AddChild(Ui.Text("Choisis ton héros (les autres seront joués par l'IA) :", 16));
        _heroList = new VBoxContainer();
        _heroList.AddThemeConstantOverride("separation", 6);
        column.AddChild(_heroList);
        _players = Ui.Text("", 15);
        column.AddChild(_players);
        _start = Ui.Button("Lancer le combat", () => StartPressed?.Invoke(), 48);
        column.AddChild(_start);
        column.AddChild(Ui.Button("Quitter le salon", () => LeavePressed?.Invoke(), 36));
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
        foreach (var child in _heroList.GetChildren())
            child.QueueFree();
        foreach (var hero in _heroes)
        {
            var owner = lobby.Players.FirstOrDefault(p => p.Hero == hero.Id);
            var isMine = owner?.Id == _me;
            var label = hero.Definition.Name + (owner is null ? "  —  libre" : isMine ? "  —  toi (clic pour le laisser)" : $"  —  pris par {owner.Name}");
            var id = hero.Id;
            var button = Ui.Button(label, () => HeroPicked?.Invoke(isMine ? null : id), 38);
            button.Disabled = owner is not null && !isMine; // pris par un autre : impossible de le choisir
            if (isMine)
                button.Modulate = new Color(0.6f, 1f, 0.6f);
            _heroList.AddChild(button);
        }

        _players.Text = "Joueurs : " + string.Join(", ", lobby.Players.Select(p =>
            p.Name + (p.Id == _me ? " (toi)" : "") + (p.Hero is null ? " — pas encore de héros" : "") + (p.Connected ? "" : " — déconnecté")));
        _campaign.Text = lobby.Campaign is { } campaign
            ? $"Réputation du groupe : {campaign.Tier} ({campaign.Reputation:+0;-0;0})"
              + (campaign.LastMission is { } last ? $", dernière mission {last:+0;-0;0}" : "")
            : "";
        _academy.Visible = AcademyAvailable && lobby.Campaign?.Heroes is not null;
        var missing = lobby.Players.Count(p => p.Hero is null);
        _start.Disabled = missing > 0;
        _start.Text = missing > 0 ? $"Lancer le combat (en attente de {missing} choix)" : "Lancer le combat";
    }
}
