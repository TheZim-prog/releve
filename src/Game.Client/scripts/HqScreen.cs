using Game.Core;
using Game.Core.Campaign;
using Game.Core.Network;
using Godot;

namespace Game.Client;

/// <summary>
/// Le QG d'une partie avec campagne (affichage minimal, à polir plus tard) : la réputation du groupe, le panneau de
/// quêtes (n'importe qui choisit celle du groupe), le héros que chacun déploie (ou reste au QG), l'académie, et le
/// lancement de la quête.
/// </summary>
public partial class HqScreen : Control
{
    private readonly GameContent _content;
    private readonly int _me;
    private readonly string _hostInfo;
    private Label _campaign = null!;
    private VBoxContainer _board = null!;
    private Label _quest = null!;
    private VBoxContainer _heroes = null!;
    private Label _group = null!;
    private Button _start = null!;
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

    public event Action<string?>? QuestChosen;
    public event Action<string?>? HeroDeployed;
    public event Action? StartPressed;
    public event Action? AcademyPressed;
    public event Action? LeavePressed;

    /// <summary>Ce jeu sait ouvrir l'académie (son contenu a des arbres de compétences).</summary>
    public bool AcademyAvailable { get; set; } = true;

    public override void _Ready()
    {
        var column = Ui.CenteredColumn(this, 620);
        column.AddChild(Ui.Text("QG", 32, HorizontalAlignment.Center));
        if (_hostInfo.Length > 0)
            column.AddChild(Ui.Text(_hostInfo, 14, HorizontalAlignment.Center));
        _campaign = Ui.Text("", 16, HorizontalAlignment.Center);
        column.AddChild(_campaign);

        column.AddChild(Ui.Text("Panneau de quêtes", 20));
        _board = new VBoxContainer();
        _board.AddThemeConstantOverride("separation", 6);
        column.AddChild(_board);
        _quest = Ui.Text("", 15);
        column.AddChild(_quest);

        column.AddChild(Ui.Text("Ton héros pour la quête", 20));
        _heroes = new VBoxContainer();
        _heroes.AddThemeConstantOverride("separation", 6);
        column.AddChild(_heroes);
        _group = Ui.Text("", 15);
        column.AddChild(_group);

        _start = Ui.Button("Lancer la quête", () => StartPressed?.Invoke(), 48);
        column.AddChild(_start);
        if (AcademyAvailable)
            column.AddChild(Ui.Button("Académie (créer tes héros)", () => AcademyPressed?.Invoke(), 36));
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
        RefreshBoard(campaign);
        RefreshHeroes(lobby, campaign);

        var going = lobby.Players.Count(p => p.Deployed is not null);
        _start.Disabled = campaign.ChosenQuest is null || going == 0;
        _start.Text = campaign.ChosenQuest is null ? "Lancer la quête (choisissez-en une)"
            : going == 0 ? "Lancer la quête (personne ne part)"
            : $"Lancer la quête ({going} héros)";
    }

    private void RefreshBoard(CampaignInfo campaign)
    {
        foreach (var child in _board.GetChildren())
            child.QueueFree();
        foreach (var quest in campaign.Board)
        {
            var chosen = quest.Id == campaign.ChosenQuest;
            var id = quest.Id;
            var button = Ui.Button($"{quest.Name}  —  {Difficulty(quest.Difficulty)}, {CampaignTexts.Duration(quest.DurationHours)}, {quest.Xp} XP{Materials(quest)}",
                () => QuestChosen?.Invoke(chosen ? null : id), 38);
            if (chosen)
                button.Modulate = Ui.Taken;
            _board.AddChild(button);
        }
        _quest.Text = campaign.Board.FirstOrDefault(q => q.Id == campaign.ChosenQuest) is { } picked
            ? $"{picked.Description} (jusqu'à {picked.Slots} héros)"
            // Choisie par un joueur plus avancé : elle n'est pas encore à ce panneau-ci (partir y fera sauter).
            : campaign.ChosenQuest is { } other && _content.Quests.TryGetValue(other, out var ahead)
                ? $"Quête choisie : {ahead.Name} (pas encore parue à ta date : partir t'y fera sauter). {ahead.Description}"
            : campaign.Board.Count == 0 ? "Aucune quête au panneau pour l'instant."
            : "Aucune quête choisie : n'importe qui peut en choisir une.";
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

        var names = campaign.Heroes.ToDictionary(h => h.Id, h => h.Name);
        _group.Text = "Le groupe : " + string.Join(", ", lobby.Players.Select(p =>
            p.Name + (p.Id == _me ? " (toi)" : "") + $" ({CampaignTexts.Date(p.Hour)})"
            + (p.Deployed is { } heroId ? $" avec {names.GetValueOrDefault(heroId, heroId)}" : " — reste au QG")
            + (p.Connected ? "" : " — déconnecté")));
    }

    private string ArchetypeName(string archetype) => _content.Units.TryGetValue(archetype, out var unit) ? unit.Name : archetype;

    private static string Materials(QuestInfo quest) =>
        quest.Materials.Values.Sum() is var total and > 0 ? $", {total} matériaux" : "";

    public static string Difficulty(QuestDifficulty difficulty) => difficulty switch
    {
        QuestDifficulty.VeryEasy => "très facile",
        QuestDifficulty.Easy => "facile",
        QuestDifficulty.Hard => "difficile",
        QuestDifficulty.VeryHard => "très difficile",
        _ => difficulty.ToString(),
    };
}
