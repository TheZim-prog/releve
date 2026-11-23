using Game.Core;
using Game.Core.Network;
using Godot;

namespace Game.Client;

/// <summary>
/// Les groupes du QG (affichage minimal, à polir plus tard) : le sien (quitter, ou lancer pour le chef), sinon ceux qui
/// se forment (à rejoindre) ; ceux qui sont partis ; et le panneau de quêtes, où l'on en accepte une pour former un
/// groupe (cf. docs/design.md 4.3).
/// </summary>
public partial class GroupPanel : VBoxContainer
{
    private readonly GameContent _content;
    private readonly int _me;

    public GroupPanel(GameContent content, int me)
    {
        _content = content;
        _me = me;
        AddThemeConstantOverride("separation", 6);
    }

    // Godot exige un constructeur sans paramètre pour les nœuds C#.
    public GroupPanel() : this(GameData.Load(), 0)
    {
    }

    public event Action<string>? QuestAccepted;
    public event Action<int>? GroupJoined;
    public event Action? GroupLeft;
    public event Action? StartPressed;

    public void Refresh(LobbyState lobby, CampaignInfo campaign)
    {
        foreach (var child in GetChildren())
            child.QueueFree();
        var me = lobby.Players.FirstOrDefault(p => p.Id == _me);
        var mine = campaign.Groups.FirstOrDefault(g => g.Id == campaign.MyGroup);

        AddChild(Ui.Text("Groupes", 20));
        if (mine is not null)
            AddMine(mine, lobby, me?.Name);
        foreach (var group in campaign.Groups.Where(g => g != mine))
            AddOther(group, joinable: mine is null);
        if (campaign.Groups.Count == 0)
            AddChild(Ui.Text("Aucun groupe : accepte une quête du panneau pour en former un.", 15));

        AddChild(Ui.Text("Panneau de quêtes", 20));
        foreach (var quest in campaign.Board)
        {
            var id = quest.Id;
            var button = Ui.Button($"Accepter : {quest.Name}  —  {HqScreen.Difficulty(quest.Difficulty)}, "
                                   + $"{CampaignTexts.Duration(quest.DurationHours)}, {quest.Xp} XP{Materials(quest)}, jusqu'à {quest.Slots} héros",
                () => QuestAccepted?.Invoke(id), 38);
            button.Disabled = mine is not null; // déjà dans un groupe
            button.TooltipText = quest.Description;
            AddChild(button);
        }
        if (campaign.Board.Count == 0)
            AddChild(Ui.Text("Aucune quête au panneau à ta date : attends un peu, ou rejoins un groupe.", 15));
    }

    private void AddMine(GroupInfo group, LobbyState lobby, string? myName)
    {
        var quest = QuestName(group.Quest);
        var description = _content.Quests.TryGetValue(group.Quest, out var definition) ? definition.Description : "";
        AddChild(Ui.Text($"Ton groupe : {quest} ({group.Members.Count}/{group.Slots}). {description}", 15));
        AddChild(Ui.Text("Avec : " + string.Join(", ", group.Members.Select(name => name + Deployment(lobby, name))), 15));
        var leader = group.Leader == myName;
        if (group.Phase != GroupPhase.Forming)
            return;
        var ready = group.Members.All(name => lobby.Players.Any(p => p.Name == name && p.Deployed is not null));
        if (leader)
        {
            var start = Ui.Button(ready ? "Lancer la quête" : "Lancer la quête (chacun doit choisir son héros)", () => StartPressed?.Invoke(), 46);
            start.Disabled = !ready;
            AddChild(start);
        }
        else
        {
            AddChild(Ui.Text($"{group.Leader} lance la quête quand le groupe est prêt.", 15));
        }
        AddChild(Ui.Button(leader ? "Dissoudre le groupe (la quête revient au panneau)" : "Quitter le groupe", () => GroupLeft?.Invoke(), 34));
    }

    private void AddOther(GroupInfo group, bool joinable)
    {
        var who = $"{QuestName(group.Quest)} — {string.Join(", ", group.Members)} ({group.Members.Count}/{group.Slots})";
        if (group.Phase != GroupPhase.Forming)
        {
            AddChild(Ui.Text($"{who} : {(group.Phase == GroupPhase.Travelling ? "en route" : "en mission")}.", 15));
            return;
        }
        var id = group.Id;
        var button = Ui.Button($"Rejoindre : {who}", () => GroupJoined?.Invoke(id), 36);
        button.Disabled = !joinable || group.Members.Count >= group.Slots;
        AddChild(button);
    }

    /// <summary>« avec Rempart », ou « pas encore de héros » : le héros que ce joueur déploie.</summary>
    private static string Deployment(LobbyState lobby, string name) =>
        lobby.Players.FirstOrDefault(p => p.Name == name)?.Deployed is { } hero
            ? $" ({lobby.Campaign?.Heroes.FirstOrDefault(h => h.Id == hero)?.Name ?? hero})"
            : " (pas encore de héros)";

    private string QuestName(string id) => _content.Quests.TryGetValue(id, out var quest) ? quest.Name : id;

    private static string Materials(QuestInfo quest) =>
        quest.Materials.Values.Sum() is var total and > 0 ? $", {total} matériaux" : "";
}
