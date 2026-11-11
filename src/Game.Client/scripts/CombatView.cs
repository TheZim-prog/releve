using Game.Core;
using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Network;
using Godot;

namespace Game.Client;

/// <summary>
/// Affichage et commandes d'un combat, solo ou en réseau (<see cref="ICombatSession"/>). Cette scène ne fait
/// qu'afficher l'état de la simulation et ses événements, et envoyer les ordres du joueur, prévérifiés par elle
/// (prévisualisation comprise). L'état continu (positions, jauges, PV) est lu dans la simulation, comme un instantané ;
/// les événements servent au journal, aux effets et au décor.
/// </summary>
public partial class CombatView : Node3D
{
    private const int PlayerTeam = CombatLaunch.PlayerTeam;

    private static readonly Dictionary<string, Color> UnitColors = new()
    {
        ["colossus"] = new(0.35f, 0.45f, 0.9f),
        ["speedster"] = new(1f, 0.85f, 0.2f),
        ["fighter"] = new(0.3f, 0.75f, 0.4f),
        ["energetic"] = new(0.95f, 0.5f, 0.15f),
        ["tech"] = new(0.3f, 0.85f, 0.85f),
        ["telepath"] = new(0.7f, 0.4f, 0.95f),
        ["brawler"] = new(0.8f, 0.2f, 0.2f),
        ["gunner"] = new(0.9f, 0.4f, 0.4f),
        ["brute"] = new(0.55f, 0.08f, 0.08f),
        ["kingpin"] = new(0.45f, 0.05f, 0.3f),
    };

    private static readonly Color Civilian = new(0.92f, 0.92f, 0.85f);
    private static readonly Color HeroGadget = new(0.6f, 0.95f, 1f);
    private static readonly Color EnemyDefault = new(0.8f, 0.25f, 0.25f);

    // Fantôme et chemin : à peine visibles au simple survol, francs une fois la case choisie (déplacement prévu).
    private static readonly StandardMaterial3D GhostHovered = BoardView.Translucent(new Color(1, 1, 1, 0.18f));
    private static readonly StandardMaterial3D GhostPlaced = BoardView.Translucent(new Color(0.55f, 0.85f, 1f, 0.7f));
    private static readonly Color PathHovered = new(1f, 1f, 0.4f, 0.18f);
    private static readonly Color PathPlaced = new(1f, 1f, 0.4f, 0.55f);
    private static readonly Color AimColor = new(1f, 0.55f, 0.1f, 0.5f);
    private static readonly Color TargetColor = new(1f, 0.2f, 0.6f, 0.55f);
    private static readonly Color OrderHint = new(0.55f, 0.85f, 1f);

    private readonly Dictionary<UnitId, UnitView> _views = new();
    private readonly Dictionary<UnitId, IReadOnlyList<Cell>> _domes = new();

    private ICombatSession _session = null!;
    private GameContent _data = null!;
    private CombatSimulation _sim = null!;
    private BoardView _board = null!;
    private Hud _hud = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _ghostView = null!;

    // Ordres en attente des unités que le joueur commande (pas ceux des coéquipiers), relus une fois par image.
    private IReadOnlyList<ActivateCommand> _orders = Array.Empty<ActivateCommand>();
    private readonly List<MeshInstance3D> _orderGhosts = new();
    private (int Tick, IReadOnlyList<ActivateCommand> Orders)? _shownOrders;

    // Vitesse d'écoulement du temps de combat, en % (affichage seulement : les règles ne changent pas). Plus lente par
    // défaut en solo, où un seul joueur commande toute l'équipe.
    private static readonly int[] Speeds = [50, 75, 100];
    private int _speed = 1;

    private bool _showIntentions;
    private bool? _won;
    private bool _frozen;

    private UnitId? _selected;
    private Cell? _ghost;
    private Aiming? _aiming;
    private Cell? _hovered;

    // Outils de développement (arguments après « -- », cf. Main) : --select=<id>, --skill=<n>, --hover=x,y et
    // --intentions=on simulent une sélection, une compétence armée, le survol d'une case et les intentions révélées
    // (pour vérifier l'affichage sur une capture).
    private Cell? _hoverOverride;

    /// <summary>Le joueur veut quitter le combat (F10).</summary>
    public event Action? LeaveRequested;

    /// <summary>La simulation ne suit plus celle du serveur (réseau) : il faut se reconnecter.</summary>
    public event Action<string>? Desynced;

    /// <summary>À appeler avant d'ajouter la vue à l'arbre.</summary>
    public void Setup(ICombatSession session) => _session = session;

    /// <summary>
    /// Fige la vue (plus de ticks ni d'ordres) avec un message, en attendant que <see cref="Main"/> la remplace :
    /// simulation désynchronisée ou connexion perdue, reconnexion en cours.
    /// </summary>
    public void Freeze(string message)
    {
        _frozen = true;
        _hud.SetBanner(message, Colors.White);
    }

    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.08f, 0.09f, 0.12f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.45f,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -30, 0), ShadowEnabled = true });
        // Projection orthogonale : vue plongeante fixe sans déformation (les capsules restent droites sur les bords).
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal };
        AddChild(_camera);
        _ghostView = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.3f },
            MaterialOverride = GhostHovered,
            Visible = false,
        };
        AddChild(_ghostView);
        _hud = new Hud();
        AddChild(_hud);
        _hud.ActionPressed += OnActionPressed;
        _hud.HeroPressed += SelectIfControllable;
        StartCombat();
        ApplyDevArguments();
    }

    private void ApplyDevArguments()
    {
        var Arg = Main.DevArg;
        if (Arg("intentions") == "on")
            _showIntentions = true;
        if (Arg("select") is { } select && int.TryParse(select, out var id))
            _selected = new UnitId(id);
        if (Arg("skill") is { } skill && int.TryParse(skill, out var index) && Selected is { } unit && index >= 1 && index <= unit.Skills.Count)
            _aiming = new Aiming(unit.Skills[index - 1]);
        if (Arg("hover")?.Split(',') is [var x, var y] && int.TryParse(x, out var hx) && int.TryParse(y, out var hy))
            _hoverOverride = new Cell(hx, hy);
    }

    private void StartCombat()
    {
        foreach (var view in _views.Values)
            view.QueueFree();
        _views.Clear();
        _domes.Clear();
        _board?.QueueFree();

        _data = _session.Content;
        _sim = _session.Simulation;
        // Combat rejoint en cours (reconnexion) : les dômes encore levés. Le décor détruit est déjà dans la grille.
        foreach (var e in _sim.EventLog)
            TrackDome(e);
        _board = new BoardView();
        AddChild(_board);
        _board.Build(_sim.Grid);

        var center = new Vector3((_sim.Grid.Width - 1) / 2f, 0, (_sim.Grid.Height - 1) / 2f);
        // Le plateau tient en largeur (16/9), entre les cartes des héros (à gauche) et la rangée d'actions (en bas).
        _camera.Size = Math.Max(_sim.Grid.Width * 9f / 16f + 3f, _sim.Grid.Height + 5f);
        var focus = center + new Vector3(0, 0, 0.9f); // un peu plus haut à l'écran : la rangée d'actions occupe le bas
        _camera.LookAtFromPosition(focus + new Vector3(0, 30f, 14f), focus, Vector3.Up);

        _won = null;
        _shownOrders = null;
        // En réseau, le joueur n'a qu'un héros : il est choisi d'emblée.
        _selected = _session is NetworkCombat { MyHero: { } mine } ? mine : null;
        _ghost = null;
        _aiming = null;
        _hud.ClearLog();
        _hud.SetBanner("", Colors.White);
        if (_data.Scenarios.TryGetValue(_session.ScenarioId, out var scenario))
            _hud.Log(scenario.Name);
    }

    public override void _Process(double delta)
    {
        if (_frozen)
            return;
        IReadOnlyList<CombatEvent> events;
        try
        {
            events = _session.Advance(_session.IsNetwork ? delta : delta * Speeds[_speed] / 100);
        }
        catch (DesyncException e)
        {
            Freeze("Resynchronisation…");
            Desynced?.Invoke(e.Message);
            return;
        }
        foreach (var e in events)
            Handle(e);
        foreach (var notice in _session.TakeNotices())
            _hud.Log(notice);
        _won = _session.Outcome;
        _orders = _session.PlannedOrders().Where(o => _sim.FindUnit(o.Unit) is { } unit && IsControllable(unit)).ToList();
        if (_won is { } won)
            _hud.SetBanner(won ? "Victoire !" : "Défaite…", won ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f));

        // Souris sur l'interface : aucune case survolée (le clic irait à l'interface, pas au plateau).
        _hovered = _hoverOverride ?? (GetViewport().GuiGetHoveredControl() is null ? HoveredCell() : null);
        foreach (var unit in _sim.Units)
        {
            if (!_views.TryGetValue(unit.Id, out var view))
            {
                view = new UnitView();
                AddChild(view);
                view.Setup(unit, ColorOf(unit), small: unit.SummonedBy is not null);
                _views[unit.Id] = view;
            }
            view.Sync(unit, _data.Rules.GaugeCapacity, unit.Id == _selected, _hovered == unit.Position, IntentionText(unit), delta);
        }

        if (Selected is not { Status: UnitStatus.Active })
            ClearPlan(keepSelection: Selected is not null);
        UpdateOverlays();
        UpdateHud();
    }

    /// <summary>Réaction de l'affichage à un événement : journal, chiffres flottants, décor, dômes.</summary>
    private void Handle(CombatEvent e)
    {
        if (Texts.Describe(e, _sim) is { } line)
            _hud.Log(line);
        switch (e)
        {
            // Ordre parti : il ne peut plus être changé, le héros est lâché (sauf si on lui prépare déjà la suite).
            // Refusé au départ, il reste choisi : sa jauge est pleine, le joueur rejoue.
            case OrderExecuted x when x.Unit == _selected && _ghost is null && _aiming is null:
                _selected = null;
                break;
            case UnitDamaged d:
                Float(d.Unit, $"-{d.Amount}", new Color(1f, 0.35f, 0.3f));
                break;
            case UnitHealed h:
                Float(h.Unit, $"+{h.Amount}", new Color(0.4f, 1f, 0.5f));
                break;
            case HitBlocked b:
                Float(b.Unit, "bloqué", new Color(0.5f, 0.75f, 1f));
                break;
            case DomeRaised or DomeEnded:
                TrackDome(e);
                break;
            case CoverDestroyed c:
                _board.RemoveObstacle(c.Cell);
                break;
        }
    }

    /// <summary>Dômes levés : suivis par leurs événements, en direct comme en relisant le journal (reconnexion).</summary>
    private void TrackDome(CombatEvent e)
    {
        if (e is DomeRaised raised)
            _domes[raised.Owner] = raised.Cells;
        else if (e is DomeEnded ended)
            _domes.Remove(ended.Owner);
    }
}
