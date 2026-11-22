using Game.Core;
using Game.Core.Campaign;
using Game.Core.Network;
using Game.Core.Progression;
using Godot;

namespace Game.Client;

/// <summary>
/// L'académie (affichage minimal, à polir plus tard) : les héros du joueur, et tant que son duo n'est pas au complet,
/// la création d'un héros : nom, archétype, et ses points de compétence de départ dans l'arbre (seules les cases des
/// paliers ouverts sont proposées). Le serveur vérifie aussi tout.
/// </summary>
public partial class AcademyScreen : Control
{
    private readonly GameContent _content;
    private readonly HeroCatalog _catalog;
    private readonly List<string> _archetypes;
    private readonly List<string> _chosen = new();
    private string _archetype;
    private Label _mine = null!;
    private VBoxContainer _form = null!;
    private LineEdit _name = null!;
    private VBoxContainer _tree = null!;
    private Label _points = null!;
    private Button _recruit = null!;
    private Label _status = null!;

    public AcademyScreen(GameContent content)
    {
        _content = content;
        _catalog = content.Heroes ?? throw new ArgumentException("This content has no heroes catalog.", nameof(content));
        _archetypes = _catalog.Trees.Keys.OrderBy(a => content.Units[a].Name).ToList();
        _archetype = _archetypes.FirstOrDefault() ?? "";
    }

    // Godot exige un constructeur sans paramètre pour les nœuds C#.
    public AcademyScreen() : this(GameData.Load())
    {
    }

    public event Action<Recruit>? RecruitPressed;
    public event Action? BackPressed;

    public override void _Ready()
    {
        var column = Ui.CenteredColumn(this, 640);
        column.AddChild(Ui.Text("Académie", 32, HorizontalAlignment.Center));
        _mine = Ui.Text("", 16);
        column.AddChild(_mine);

        _form = new VBoxContainer();
        _form.AddThemeConstantOverride("separation", 8);
        column.AddChild(_form);
        _form.AddChild(Ui.Text("Nouveau héros", 20));
        _name = Ui.Field(_form, "Son nom", "", "ex. Rempart");
        _name.MaxLength = Academy.MaxNameLength;
        _form.AddChild(Ui.Text("Son archétype", 14));
        _form.AddChild(ArchetypePicker());
        _points = Ui.Text("", 15);
        _form.AddChild(_points);
        _tree = new VBoxContainer();
        _form.AddChild(_tree);
        _recruit = Ui.Button("Recruter", () => RecruitPressed?.Invoke(new Recruit(_name.Text.Trim(), _archetype, _chosen.ToList())));
        _form.AddChild(_recruit);

        column.AddChild(Ui.Button("Retour au salon", () => BackPressed?.Invoke(), 36));
        _status = Ui.Text("", 15, HorizontalAlignment.Center);
        column.AddChild(_status);
        RebuildTree();
    }

    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", error ? new Color(1f, 0.5f, 0.45f) : Colors.White);
    }

    /// <summary>
    /// Les héros du joueur, et le formulaire seulement s'il a encore de la place dans son duo. <paramref name="recruited"/> :
    /// un de ses héros vient d'être recruté (formulaire vidé, pour le suivant).
    /// </summary>
    public void Refresh(LobbyState lobby, bool recruited = false)
    {
        var mine = lobby.Campaign?.Heroes.Where(h => h.Mine).ToList() ?? new List<HeroInfo>();
        _mine.Text = mine.Count == 0
            ? "Tu n'as pas encore de héros."
            : "Tes héros :\n" + string.Join("\n", mine.Select(h => CampaignTexts.Hero(h, ArchetypeName(h.Archetype))));
        var alive = mine.Count(h => !h.IsDead);
        _form.Visible = alive < _catalog.Rules.HeroesPerPlayer;
        if (recruited)
        {
            _name.Text = "";
            _chosen.Clear();
            RebuildTree();
        }
        var full = _form.Visible ? "" : "Ton duo est au complet.";
        if (recruited)
            SetStatus($"Héros recruté. {full}".Trim());
        else if (!_form.Visible)
            SetStatus(full);
    }

    private string ArchetypeName(string archetype) => _content.Units.TryGetValue(archetype, out var unit) ? unit.Name : archetype;

    private OptionButton ArchetypePicker()
    {
        var picker = new OptionButton { CustomMinimumSize = new Vector2(0, 36), FocusMode = FocusModeEnum.None };
        picker.AddThemeFontSizeOverride("font_size", 16);
        foreach (var archetype in _archetypes)
            picker.AddItem(ArchetypeName(archetype));
        picker.ItemSelected += index =>
        {
            _archetype = _archetypes[(int)index];
            _chosen.Clear();
            RebuildTree();
        };
        return picker;
    }

    /// <summary>L'arbre de l'archétype choisi : une ligne par palier de chaque branche, une case par compétence ou passif.</summary>
    private void RebuildTree()
    {
        foreach (var child in _tree.GetChildren())
            child.QueueFree();
        if (!_catalog.Trees.TryGetValue(_archetype, out var tree))
            return;
        var points = _catalog.Rules.SkillPointsAt(1);
        var learnable = Academy.Learnable(_catalog, _archetype, _chosen).Select(n => n.Id).ToHashSet();
        foreach (var branch in tree.Branches)
        {
            _tree.AddChild(Ui.Text(branch.Name, 16));
            for (var rank = 0; rank < branch.Tiers.Count; rank++)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);
                var tier = branch.Tiers[rank];
                foreach (var active in tier.Actives)
                    row.AddChild(NodeButton(active.Skill, SkillName(tree, active.Skill), learnable));
                foreach (var passive in tier.Passives)
                    row.AddChild(NodeButton(passive.Id, $"{passive.Name} (passif{(passive.Planned ? ", à venir" : "")})", learnable));
                _tree.AddChild(row);
            }
        }
        _points.Text = $"Compétences de départ : {_chosen.Count} / {points} (un palier s'ouvre quand on a assez pris dans les précédents de la branche)";
        _recruit.Disabled = _chosen.Count != points;
    }

    private string SkillName(SkillTree tree, string id) =>
        _catalog.Skills.TryGetValue(id, out var skill) && skill.Name is { } name ? name
        : tree.Planned.TryGetValue(id, out var planned) ? $"{planned} (à venir)"
        : id;

    private Button NodeButton(string id, string label, IReadOnlySet<string> learnable)
    {
        var chosen = _chosen.Contains(id);
        var button = Ui.Button(label, () =>
        {
            if (_chosen.Remove(id))
            {
                var kept = Academy.Prune(_catalog, _archetype, _chosen);
                _chosen.Clear();
                _chosen.AddRange(kept);
            }
            else
            {
                _chosen.Add(id);
            }
            RebuildTree();
        }, 34);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.Disabled = !chosen && !learnable.Contains(id);
        if (chosen)
            button.Modulate = new Color(0.6f, 1f, 0.6f);
        return button;
    }
}
