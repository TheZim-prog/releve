using Game.Core;
using Game.Core.Campaign;
using Game.Core.Network;
using Game.Core.Progression;
using Godot;

namespace Game.Client;

/// <summary>
/// L'académie (affichage minimal, à polir plus tard) : les héros du joueur, chacun à entraîner (cf.
/// <see cref="TrainingPanel"/>), et tant que son duo n'est pas au complet, la création d'un héros : nom, archétype, et
/// ses points de compétence de départ dans l'arbre (seules les cases des paliers ouverts sont proposées). Le serveur
/// vérifie aussi tout.
/// </summary>
public partial class AcademyScreen : Control
{
    private readonly GameContent _content;
    private readonly HeroCatalog _catalog;
    private readonly List<string> _archetypes;
    private readonly List<string> _chosen = new();
    private string _archetype;
    private LobbyState? _lobby;
    private VBoxContainer _heroes = null!;
    private VBoxContainer _trainingHost = null!;
    private TrainingPanel? _training;
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
    public event Action<string, StatKind>? SpendPressed;
    public event Action<string, string>? LearnPressed;
    public event Action<string, IReadOnlyList<string>, string?>? EquipPressed;
    public event Action? BackPressed;

    public override void _Ready()
    {
        // Défilante : l'arbre et l'équipement d'un héros ne tiennent pas toujours dans la fenêtre.
        var column = Ui.CenteredColumn(this, 660, scrolling: true);
        column.AddChild(Ui.Text("Académie", 32, HorizontalAlignment.Center));
        _heroes = new VBoxContainer();
        _heroes.AddThemeConstantOverride("separation", 6);
        column.AddChild(_heroes);
        _trainingHost = new VBoxContainer();
        column.AddChild(_trainingHost);

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

        column.AddChild(Ui.Button("Retour au QG", () => BackPressed?.Invoke(), 36));
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
    /// Les héros du joueur, l'entraînement en cours, et le formulaire seulement s'il a encore de la place dans son duo
    /// (et n'entraîne personne). <paramref name="recruited"/> : un de ses héros vient d'être recruté (formulaire vidé).
    /// </summary>
    public void Refresh(LobbyState lobby, bool recruited = false)
    {
        _lobby = lobby;
        var mine = lobby.Campaign?.Heroes.Where(h => h.Mine).ToList() ?? new List<HeroInfo>();
        RefreshHeroes(mine);
        if (_training is not null)
        {
            if (mine.FirstOrDefault(h => h.Id == _training.HeroId && !h.IsDead) is { } trained)
                _training.Refresh(trained);
            else
                CloseTraining();
        }

        var full = mine.Count(h => !h.IsDead) >= _catalog.Rules.HeroesPerPlayer;
        _form.Visible = !full && _training is null;
        if (recruited)
        {
            _name.Text = "";
            _chosen.Clear();
            RebuildTree();
            SetStatus($"Héros recruté. {(full ? "Ton duo est au complet." : "")}".Trim());
        }
        else if (full && _training is null)
        {
            SetStatus("Ton duo est au complet : entraîne tes héros.");
        }
    }

    private void RefreshHeroes(IReadOnlyList<HeroInfo> mine)
    {
        foreach (var child in _heroes.GetChildren())
            child.QueueFree();
        _heroes.AddChild(Ui.Text(mine.Count == 0 ? "Tu n'as pas encore de héros." : "Tes héros", 20));
        foreach (var hero in mine)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var line = Ui.Text(CampaignTexts.Hero(hero, ArchetypeName(hero.Archetype)), 15);
            line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(line);
            if (!hero.IsDead && hero.Sheet is not null)
                row.AddChild(Ui.Button("Entraîner", () => OpenTraining(hero), 32));
            _heroes.AddChild(row);
        }
    }

    /// <summary>Ouvre l'entraînement de ce héros, s'il est au joueur (outil de test : --train=).</summary>
    public void Train(string heroId)
    {
        if (_lobby?.Campaign?.Heroes.FirstOrDefault(h => h.Id == heroId && h.Mine && !h.IsDead && h.Sheet is not null) is { } hero)
            OpenTraining(hero);
    }

    private void OpenTraining(HeroInfo hero)
    {
        CloseTraining();
        _training = new TrainingPanel(_catalog, ArchetypeName, hero);
        // Chaque demande efface l'éventuel refus de la précédente.
        _training.SpendPressed += (id, stat) =>
        {
            SetStatus("");
            SpendPressed?.Invoke(id, stat);
        };
        _training.LearnPressed += (id, skill) =>
        {
            SetStatus("");
            LearnPressed?.Invoke(id, skill);
        };
        _training.EquipPressed += (id, equipped, passive) =>
        {
            SetStatus("");
            EquipPressed?.Invoke(id, equipped, passive);
        };
        _training.Closed += () =>
        {
            CloseTraining();
            if (_lobby is not null)
                Refresh(_lobby);
        };
        _trainingHost.AddChild(_training);
        _form.Visible = false;
        _heroes.Visible = false; // le panneau a son propre en-tête
        SetStatus("");
    }

    private void CloseTraining()
    {
        _training?.QueueFree();
        _training = null;
        _heroes.Visible = true;
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

    /// <summary>L'arbre de l'archétype choisi pour la création : un clic prend ou rend une case.</summary>
    private void RebuildTree()
    {
        if (!_catalog.Trees.TryGetValue(_archetype, out var tree))
            return;
        var points = _catalog.Rules.SkillPointsAt(1);
        var learnable = Academy.Learnable(_catalog, _archetype, _chosen).Select(n => n.Id).ToHashSet();
        SkillTreeView.Build(_tree, _catalog, tree,
            id => _chosen.Contains(id) ? SkillTreeView.Case.Taken
                : learnable.Contains(id) ? SkillTreeView.Case.Open
                : SkillTreeView.Case.Closed,
            Toggle, takenClickable: true);
        _points.Text = $"Compétences de départ : {_chosen.Count} / {points} (un palier s'ouvre quand on a assez pris dans les précédents de la branche)";
        _recruit.Disabled = _chosen.Count != points;
    }

    /// <summary>Prend la case, ou la rend avec celles qu'elle ouvrait.</summary>
    private void Toggle(string id)
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
    }
}
