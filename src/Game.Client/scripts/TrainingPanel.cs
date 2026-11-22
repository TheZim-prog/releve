using Game.Core.Campaign;
using Game.Core.Network;
using Game.Core.Progression;
using Godot;

namespace Game.Client;

/// <summary>
/// L'entraînement d'un héros à l'académie (affichage minimal, à polir plus tard) : ses points de stats à dépenser, son
/// arbre (les cases des paliers ouverts sont proposées tant qu'il a des points de compétence), et son équipement
/// (une version par compétence apprise, dans la limite des emplacements, et son passif). Chaque choix part aussitôt au
/// serveur, qui vérifie tout ; l'écran se met à jour quand la campagne revient.
/// </summary>
public partial class TrainingPanel : VBoxContainer
{
    private readonly HeroCatalog _catalog;
    private readonly Func<string, string> _archetypeName;
    private HeroInfo _hero;
    private Label _header = null!;
    private VBoxContainer _stats = null!;
    private Label _treeHint = null!;
    private VBoxContainer _tree = null!;
    private Label _slots = null!;
    private VBoxContainer _equipment = null!;

    public TrainingPanel(HeroCatalog catalog, Func<string, string> archetypeName, HeroInfo hero)
    {
        _catalog = catalog;
        _archetypeName = archetypeName;
        _hero = hero;
    }

    // Godot exige un constructeur sans paramètre pour les nœuds C#.
    public TrainingPanel() : this(null!, id => id, null!)
    {
    }

    public string HeroId => _hero.Id;

    public event Action<string, StatKind>? SpendPressed;
    public event Action<string, string>? LearnPressed;
    public event Action<string, IReadOnlyList<string>, string?>? EquipPressed;
    public event Action? Closed;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        _header = Ui.Text("", 18);
        AddChild(_header);
        _stats = new VBoxContainer();
        _stats.AddThemeConstantOverride("separation", 6);
        AddChild(_stats);
        AddChild(Ui.Text("Son arbre", 18));
        _treeHint = Ui.Text("", 14);
        AddChild(_treeHint);
        _tree = new VBoxContainer();
        AddChild(_tree);
        AddChild(Ui.Text("Son équipement", 18));
        _slots = Ui.Text("", 14);
        AddChild(_slots);
        _equipment = new VBoxContainer();
        _equipment.AddThemeConstantOverride("separation", 6);
        AddChild(_equipment);
        AddChild(Ui.Button("Fin de l'entraînement", () => Closed?.Invoke(), 36));
        Refresh(_hero);
    }

    public void Refresh(HeroInfo hero)
    {
        _hero = hero;
        if (hero.Sheet is not { } sheet || !_catalog.Trees.TryGetValue(sheet.Archetype, out var tree))
            return;
        _header.Text = CampaignTexts.Hero(hero, _archetypeName(hero.Archetype));
        RefreshStats(hero, sheet);

        var learnable = Training.Learnable(_catalog, sheet).Select(n => n.Id).ToHashSet();
        _treeHint.Text = hero.SkillPointsToSpend > 0
            ? "Clique une case proposée pour l'apprendre (définitif)."
            : "Plus de point de compétence : il en gagne un tous les deux niveaux.";
        SkillTreeView.Build(_tree, _catalog, tree,
            id => sheet.Learned.Contains(id) ? SkillTreeView.Case.Taken
                : learnable.Contains(id) ? SkillTreeView.Case.Open
                : SkillTreeView.Case.Closed,
            id => LearnPressed?.Invoke(hero.Id, id), takenClickable: false);
        RefreshEquipment(hero, sheet, tree);
    }

    private void RefreshStats(HeroInfo hero, HeroSheet sheet)
    {
        foreach (var child in _stats.GetChildren())
            child.QueueFree();
        _stats.AddChild(Ui.Text($"Points de stats : PV {sheet.Points.Hp}, puissance {sheet.Points.Power}, initiative {sheet.Points.Initiative}"
                                 + (hero.StatPointsToSpend > 0 ? " (définitif)" : ""), 15));
        if (hero.StatPointsToSpend <= 0)
            return;
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        foreach (var (stat, label) in new[] { (StatKind.Hp, "+1 PV"), (StatKind.Power, "+1 puissance"), (StatKind.Initiative, "+1 initiative") })
        {
            var button = Ui.Button(label, () => SpendPressed?.Invoke(hero.Id, stat), 32);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            buttons.AddChild(button);
        }
        _stats.AddChild(buttons);
    }

    /// <summary>Une ligne par compétence apprise (ses versions), puis les passifs ; un clic équipe ou retire.</summary>
    private void RefreshEquipment(HeroInfo hero, HeroSheet sheet, SkillTree tree)
    {
        foreach (var child in _equipment.GetChildren())
            child.QueueFree();
        var slots = _catalog.Rules.ActiveSlotsAt(sheet.Level);
        var full = sheet.Equipped.Count >= slots;
        _slots.Text = $"{sheet.Equipped.Count} / {slots} emplacements de compétences, plus un passif. Une compétence ou sa variante, pas les deux."
                      + (full ? " Tous pris : retire une compétence pour en équiper une autre." : "");
        foreach (var option in Training.Equippable(_catalog, sheet))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            if (option.Versions.Count == 0)
                row.AddChild(Ui.Text($"{CampaignTexts.NodeName(_catalog, tree, option.Node.Id)} : pas encore disponible", 14));
            // Le bouton où se trouve cette compétence (sous l'une ou l'autre version), ou -1.
            var slot = sheet.Equipped.ToList().FindIndex(option.Versions.Contains);
            foreach (var version in option.Versions)
            {
                var equipped = sheet.Equipped.Contains(version);
                var button = Ui.Button(CampaignTexts.NodeName(_catalog, tree, version),
                    () => EquipPressed?.Invoke(hero.Id, Toggled(sheet.Equipped, slot, version, equipped), sheet.Passive), 32);
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                // Rien de cette compétence équipé et plus de place : il faut d'abord en retirer une.
                button.Disabled = slot < 0 && full;
                if (equipped)
                    button.Modulate = Ui.Taken;
                row.AddChild(button);
            }
            _equipment.AddChild(row);
        }
        foreach (var passive in Training.EquippablePassives(_catalog, sheet))
        {
            var equipped = sheet.Passive == passive.Id;
            var button = Ui.Button(CampaignTexts.NodeName(_catalog, tree, passive.Id),
                () => EquipPressed?.Invoke(hero.Id, sheet.Equipped, equipped ? null : passive.Id), 32);
            if (equipped)
                button.Modulate = Ui.Taken;
            _equipment.AddChild(button);
        }
    }

    /// <summary>
    /// L'équipement après un clic sur <paramref name="version"/> : retirée si elle était équipée ; sinon équipée sur le
    /// bouton de l'autre version de la même compétence (<paramref name="slot"/>, l'ordre des boutons est gardé), ou en
    /// dernier si rien de cette compétence n'était équipé.
    /// </summary>
    private static List<string> Toggled(IReadOnlyList<string> equipped, int slot, string version, bool wasEquipped)
    {
        var next = equipped.ToList();
        if (wasEquipped)
            next.RemoveAt(slot);
        else if (slot >= 0)
            next[slot] = version;
        else
            next.Add(version);
        return next;
    }
}
