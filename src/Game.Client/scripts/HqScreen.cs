using Game.Core;
using Game.Core.Campaign;
using Game.Core.Network;
using Godot;

namespace Game.Client;

/// <summary>
/// Le QG d'une partie avec campagne (affichage minimal, à polir plus tard) : ta date, la réputation du groupe, les
/// groupes et le panneau de quêtes (<see cref="GroupPanel"/>), le héros que tu déploies, les joueurs, l'attente,
/// l'académie.
/// </summary>
public partial class HqScreen : Control
{
    private readonly GameContent _content;
    private readonly int _me;
    private readonly string _hostInfo;
    private Label _campaign = null!;
    private GroupPanel _groups = null!;
    private VBoxContainer _heroes = null!;
    private Label _players = null!;
    private Label _status = null!;

    public HqScreen(GameContent content, int me, string hostInfo)
    {
        _content = content;
        _me = me;
        _hostInfo = hostInfo;
    }

    // Godot exige un constructeur sans paramètre pour les nœuds C#.
    public HqScreen() : this(GameData.Load(), 0, "")
    {
    }

    public event Action<string>? QuestAccepted;
    public event Action<int>? GroupJoined;
    public event Action? GroupLeft;
    public event Action<string?>? HeroDeployed;
    public event Action? StartPressed;
    public event Action? WaitPressed;
    public event Action? AcademyPressed;
    public event Action? LeavePressed;

    /// <summary>Ce jeu sait ouvrir l'académie (son contenu a des arbres de compétences).</summary>
    public bool AcademyAvailable { get; set; } = true;

    public override void _Ready()
    {
        var column = Ui.CenteredColumn(this, 680, scrolling: true);
        column.AddChild(Ui.Text("QG", 32, HorizontalAlignment.Center));
        if (_hostInfo.Length > 0)
            column.AddChild(Ui.Text(_hostInfo, 14, HorizontalAlignment.Center));
        _campaign = Ui.Text("", 16, HorizontalAlignment.Center);
        column.AddChild(_campaign);

        _groups = new GroupPanel(_content, _me);
        _groups.QuestAccepted += quest => QuestAccepted?.Invoke(quest);
        _groups.GroupJoined += group => GroupJoined?.Invoke(group);
        _groups.GroupLeft += () => GroupLeft?.Invoke();
        _groups.StartPressed += () => StartPressed?.Invoke();
        column.AddChild(_groups);

        column.AddChild(Ui.Text("Ton héros pour la quête", 20));
        _heroes = new VBoxContainer();
        _heroes.AddThemeConstantOverride("separation", 6);
        column.AddChild(_heroes);
        _players = Ui.Text("", 15);
        column.AddChild(_players);

        column.AddChild(Ui.Button("Attendre une demi-journée (tes héros se reposent)", () => WaitPressed?.Invoke(), 36));
        if (AcademyAvailable)
            column.AddChild(Ui.Button("Académie (créer et entraîner tes héros)", () => AcademyPressed?.Invoke(), 36));
        column.AddChild(Ui.Button("Quitter le QG", () => LeavePressed?.Invoke(), 36));
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
        if (lobby.Campaign is not { } campaign)
            return;
        _campaign.Text = $"Ta date : {CampaignTexts.Date(campaign.Hour)}. "
                         + $"Réputation du groupe : {campaign.Tier} ({campaign.Reputation:+0;-0;0}). {CampaignTexts.Pot(campaign.Materials)}"
                         + (campaign.LastQuest is { } last ? $"\n{CampaignTexts.LastQuest(last)}" : "")
                         + (campaign.LastEvent is { } road ? $"\nEn route : {TripTexts.Outcome(_content, road)}" : "")
                         + (CampaignTexts.Memorial(campaign.Heroes) is { Length: > 0 } memorial ? $"\n{memorial}" : "");
        _groups.Refresh(lobby, campaign);
        RefreshHeroes(lobby, campaign);
    }

    private void RefreshHeroes(LobbyState lobby, CampaignInfo campaign)
    {
        foreach (var child in _heroes.GetChildren())
            child.QueueFree();
        var deployed = lobby.Players.FirstOrDefault(p => p.Id == _me)?.Deployed;
        var mine = campaign.Heroes.Where(h => h.Mine && !h.IsDead).ToList();
        if (mine.Count == 0)
            _heroes.AddChild(Ui.Text("Tu n'as pas encore de héros : crée-les à l'académie.", 15));
        foreach (var hero in mine)
        {
            var id = hero.Id;
            var isDeployed = id == deployed;
            var button = Ui.Button(CampaignTexts.Hero(hero, ArchetypeName(hero.Archetype)),
                () => HeroDeployed?.Invoke(isDeployed ? null : id), 36);
            if (isDeployed)
                button.Modulate = Ui.Taken;
            _heroes.AddChild(button);
        }

        _players.Text = "Les joueurs : " + string.Join(", ", lobby.Players.Select(p =>
            p.Name + (p.Id == _me ? " (toi)" : "") + $" ({CampaignTexts.Date(p.Hour)})" + (p.Connected ? "" : " — déconnecté")));
    }

    private string ArchetypeName(string archetype) => _content.Units.TryGetValue(archetype, out var unit) ? unit.Name : archetype;

    public static string Difficulty(QuestDifficulty difficulty) => difficulty switch
    {
        QuestDifficulty.VeryEasy => "très facile",
        QuestDifficulty.Easy => "facile",
        QuestDifficulty.Hard => "difficile",
        QuestDifficulty.VeryHard => "très difficile",
        _ => difficulty.ToString(),
    };
}
