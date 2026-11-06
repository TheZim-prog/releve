using Game.Core.Combat;
using Game.Core.Grids;
using Godot;

namespace Game.Client;

/// <summary>
/// Combat libre en solo. La simulation (Game.Core) tourne dans ce processus, à ses ticks fixes ; cette scène ne fait
/// qu'afficher son état et ses événements, et lui soumettre les commandes du joueur, validées par elle (prévisualisation
/// comprise). Le joueur commande tous les héros.
/// <para>
/// Prototype solo : l'état continu (positions, jauges, PV) est lu directement dans la simulation, comme un instantané ;
/// les événements servent au journal, aux effets et au décor. En réseau, ces instantanés viendront du serveur.
/// </para>
/// </summary>
public partial class CombatView : Node3D
{
    private const string ScenarioId = "street";
    private const int PlayerTeam = 0;
    private const int MaxTicksPerFrame = 5;

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

    private GameData _data = null!;
    private CombatSimulation _sim = null!;
    private BoardView _board = null!;
    private Hud _hud = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _ghostView = null!;

    // Fantômes des ordres en attente (les ordres eux-mêmes vivent dans la simulation : PlannedOrders).
    private readonly List<MeshInstance3D> _orderGhosts = new();
    private (int Tick, IReadOnlyList<ActivateCommand> Orders)? _shownOrders;

    // Vitesse d'écoulement du temps de combat, en % (affichage seulement : les règles ne changent pas). Plus lente par
    // défaut en solo, où un seul joueur commande toute l'équipe.
    private static readonly int[] Speeds = [50, 75, 100];
    private int _speed = 1;

    private double _accumulator;
    private bool _paused;
    private bool _showIntentions;
    private bool? _won;

    private UnitId? _selected;
    private Cell? _ghost;
    private Aiming? _aiming;
    private Cell? _hovered;

    // Outils de développement (arguments après « -- ») : --screenshot=fichier.png capture l'écran après quelques
    // secondes (--at=<s>, 4 par défaut) puis quitte ; --select=<id>, --skill=<n>, --hover=x,y et --intentions=on
    // simulent une sélection, une compétence armée, le survol d'une case et les intentions révélées (pour vérifier
    // l'affichage sur une capture) ; --ai-heroes=on fait jouer les héros par l'IA.
    private string? _screenshotPath;
    private double _screenshotIn;
    private Cell? _hoverOverride;

    // --ai-heroes=on : les héros sont confiés à l'IA de base (pour regarder un combat se jouer seul).
    private bool _aiHeroes;

    public override void _Ready()
    {
        _data = GameData.Load();
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
        _aiHeroes = DevArg("ai-heroes") == "on";
        StartCombat();
        ApplyDevArguments();
    }

    private static string? DevArg(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];

    private void ApplyDevArguments()
    {
        var Arg = DevArg;
        _screenshotPath = Arg("screenshot");
        _screenshotIn = double.TryParse(Arg("at"), System.Globalization.CultureInfo.InvariantCulture, out var at) ? at : 4;
        if (Arg("intentions") == "on")
            _showIntentions = true;
        if (Arg("select") is { } select && int.TryParse(select, out var id))
            _selected = new UnitId(id);
        if (Arg("skill") is { } skill && int.TryParse(skill, out var index) && Selected is { } unit && index >= 1 && index <= unit.Skills.Count)
            _aiming = new Aiming(unit.Skills[index - 1]);
        if (Arg("hover")?.Split(',') is [var x, var y] && int.TryParse(x, out var hx) && int.TryParse(y, out var hy))
            _hoverOverride = new Cell(hx, hy);
    }

    private void TakeScreenshotWhenDue(double delta)
    {
        if (_screenshotPath is null || (_screenshotIn -= delta) > 0)
            return;
        GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        GetTree().Quit();
    }

    private void StartCombat()
    {
        foreach (var view in _views.Values)
            view.QueueFree();
        _views.Clear();
        _domes.Clear();
        _board?.QueueFree();

        var scenario = GameData.LoadScenario(ScenarioId);
        var units = _aiHeroes
            ? _data.Units.ToDictionary(u => u.Key, u => u.Value.Behavior is null ? u.Value with { Behavior = Behaviors.Basic } : u.Value)
            : _data.Units;
        _sim = new CombatSimulation(scenario.ToSetup(_data.Rules, units));
        _board = new BoardView();
        AddChild(_board);
        _board.Build(_sim.Grid);

        var center = new Vector3((_sim.Grid.Width - 1) / 2f, 0, (_sim.Grid.Height - 1) / 2f);
        // Le plateau tient en largeur (16/9), entre les cartes des héros (à gauche) et la rangée d'actions (en bas).
        _camera.Size = Math.Max(_sim.Grid.Width * 9f / 16f + 3f, _sim.Grid.Height + 5f);
        var focus = center + new Vector3(0, 0, 0.9f); // un peu plus haut à l'écran : la rangée d'actions occupe le bas
        _camera.LookAtFromPosition(focus + new Vector3(0, 30f, 14f), focus, Vector3.Up);

        _accumulator = 0;
        _paused = false;
        _won = null;
        _shownOrders = null;
        _selected = null;
        _ghost = null;
        _aiming = null;
        _hud.ClearLog();
        _hud.SetBanner("", Colors.White);
        _hud.Log(scenario.Name);
    }

    public override void _Process(double delta)
    {
        if (!_paused && _won is null)
            RunTicks(delta * Speeds[_speed] / 100);

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
        TakeScreenshotWhenDue(delta);
    }

    private void RunTicks(double delta)
    {
        var tickDuration = 1.0 / _data.Rules.TicksPerSecond;
        _accumulator = Math.Min(_accumulator + delta, tickDuration * MaxTicksPerFrame);
        while (_accumulator >= tickDuration && _won is null)
        {
            _accumulator -= tickDuration;
            foreach (var e in _sim.Tick())
                Handle(e);

            // Combat libre : il se termine quand il ne reste qu'un camp (règle du cœur).
            var teams = _sim.TeamsStillFighting();
            if (!teams.Contains(PlayerTeam))
                _won = false;
            else if (teams.Count == 1)
                _won = true;
        }
        if (_won is { } won)
            _hud.SetBanner(won ? "Victoire !" : "Défaite…", won ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f));
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
            case DomeRaised d:
                _domes[d.Owner] = d.Cells;
                break;
            case DomeEnded d:
                _domes.Remove(d.Owner);
                break;
            case CoverDestroyed c:
                _board.RemoveObstacle(c.Cell);
                break;
        }
    }

    // --- Entrées ---

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Click();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                Cancel();
                break;
        }
    }

    private void HandleKey(InputEventKey input)
    {
        // Chiffres et touches F lus par position (sur un clavier AZERTY, la rangée du haut donne & é " ' …).
        var physical = input.PhysicalKeycode;
        if (physical is >= Key.Key1 and <= Key.Key9)
        {
            ArmAction((int)(physical - Key.Key1));
            return;
        }
        if (physical is >= Key.F1 and <= Key.F6)
        {
            if (_hud.HeroAt((int)(physical - Key.F1)) is { } hero)
                SelectIfControllable(hero);
            return;
        }

        // Vitesse : les deux touches à droite du 0 (« ) = » en AZERTY, « - = » en QWERTY), ou le pavé numérique.
        if (physical is Key.Equal || input.Keycode is Key.KpAdd)
        {
            _speed = Math.Min(_speed + 1, Speeds.Length - 1);
            return;
        }
        if (physical is Key.Minus || input.Keycode is Key.KpSubtract)
        {
            _speed = Math.Max(_speed - 1, 0);
            return;
        }

        switch (input.Keycode)
        {
            case Key.Space:
                _paused = !_paused;
                break;
            case Key.I:
                _showIntentions = !_showIntentions;
                break;
            case Key.H:
                _hud.ShowHelp = !_hud.ShowHelp;
                break;
            case Key.F9: // F1-F6 choisissent les héros
                StartCombat();
                break;
            case Key.Escape:
                Cancel();
                break;
            case Key.Tab:
                SelectNextReady();
                break;
            case Key.Enter or Key.KpEnter:
                MoveOnly();
                break;
            case Key.R:
                if (Selected is { } reviver)
                    ArmAction(reviver.Skills.Count);
                break;
        }
    }

    /// <summary>Bouton d'action n° i : compétence i, puis relever, puis déplacement seul (le dernier).</summary>
    private void OnActionPressed(int index)
    {
        if (Selected is { } unit && index == unit.Skills.Count + 1)
            MoveOnly();
        else
            ArmAction(index);
    }

    /// <summary>
    /// Déplacement sans action, vers le fantôme. Toujours explicite (Entrée ou bouton) : un second clic sur la case ne
    /// suffit pas, pour qu'un double-clic ne fasse jamais perdre l'attaque prévue.
    /// </summary>
    private void MoveOnly()
    {
        if (Selected is { } unit && _ghost is { } ghost)
            TrySubmit(new ActivateCommand(unit.Id, ghost));
    }

    /// <summary>
    /// Arme l'action n° <paramref name="index"/> du héros choisi : une compétence (même en recharge : l'ordre attendra
    /// qu'elle soit prête), ou relever (juste après la dernière compétence, comme le dernier bouton).
    /// </summary>
    private void ArmAction(int index)
    {
        if (Selected is not { } unit)
            return;
        if (index == unit.Skills.Count)
        {
            _aiming = new Aiming(null, InteractionKind.Revive);
            return;
        }
        if (index >= unit.Skills.Count)
            return;
        _aiming = new Aiming(unit.Skills[index]);
    }

    /// <summary>Choisit un héros depuis la barre ou F1-F6, seulement s'il obéit au joueur (pas dominé ni confus).</summary>
    private void SelectIfControllable(UnitId id)
    {
        if (_sim.FindUnit(id) is { } unit && IsControllable(unit))
            Select(id);
    }

    private void Click()
    {
        if (_won is not null || _hovered is not { } cell)
            return;
        var clicked = _sim.UnitAt(cell);

        if (_aiming is { } aiming && Selected is { } unit)
        {
            if (aiming.Kind == AimKind.UnitThenCell && aiming.FirstTarget is null)
            {
                if (clicked is not null)
                    _aiming = aiming with { FirstTarget = clicked.Id };
                return;
            }
            if (aiming.CommandFor(unit.Id, _ghost, cell, clicked?.Id) is { } command)
                TrySubmit(command);
            return;
        }

        if (clicked is not null && clicked.Id != _selected && IsControllable(clicked))
        {
            Select(clicked.Id);
            return;
        }
        if (Selected is not { } selectedUnit)
            return;
        if (cell == selectedUnit.Position)
            _ghost = null;
        else if (Reachable(selectedUnit).ContainsKey(cell))
            _ghost = cell;
    }

    private void Cancel()
    {
        if (_aiming is { FirstTarget: not null } aiming)
            _aiming = aiming with { FirstTarget = null };
        else if (_aiming is not null)
            _aiming = null;
        else if (_ghost is not null)
            _ghost = null;
        else if (_selected is { } id && HasOrder(id))
            _sim.Submit(new CancelOrderCommand(id));
        else
            _selected = null;
    }

    private void Select(UnitId id)
    {
        _selected = id;
        ClearPlan(keepSelection: true);
    }

    private void SelectNextReady()
    {
        var heroes = _sim.Units.Where(u => u.IsReady && IsControllable(u) && !HasOrder(u.Id)).ToList();
        if (heroes.Count == 0)
            return;
        var current = heroes.FindIndex(u => u.Id == _selected);
        Select(heroes[(current + 1) % heroes.Count].Id);
    }

    /// <summary>
    /// Donne l'ordre (à la simulation, qui le fera partir dès que le héros pourra agir : tout de suite s'il est prêt) si
    /// rien d'autre que le temps ne l'en empêche ; sinon, dit pourquoi. Il remplace l'ordre du héros pas encore parti.
    /// </summary>
    private void TrySubmit(ActivateCommand command)
    {
        if (_sim.Check(command, ignoreTiming: true) is { } reason)
        {
            _hud.Log($"Impossible : {Texts.Rejection(reason)}");
            return;
        }
        _sim.Submit(new OrderCommand(command));
        // Le héros reste choisi tant que son ordre n'est pas parti (clic droit pour l'annuler après une erreur de clic) ;
        // l'ordre s'affiche figé et plus rien ne suit la souris.
        ClearPlan(keepSelection: true);
    }

    private bool HasOrder(UnitId unit) => _sim.PlannedOrders().Any(o => o.Unit == unit);

    private void ClearPlan(bool keepSelection)
    {
        if (!keepSelection)
            _selected = null;
        _ghost = null;
        _aiming = null;
    }

    private bool IsControllable(Unit unit) => _sim.IsPlayerControlled(unit.Id);

    private Unit? Selected => _selected is { } id ? _sim.FindUnit(id) : null;

    private IReadOnlyDictionary<Cell, int> Reachable(Unit unit) => _sim.ReachableCells(unit.Id);

    private Cell? HoveredCell()
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var direction = _camera.ProjectRayNormal(mouse);
        if (direction.Y > -1e-4f)
            return null;
        var cell = BoardView.ToCell(origin + direction * (-origin.Y / direction.Y));
        return _sim.Grid.Contains(cell) ? cell : null;
    }

    // --- Prévisualisation ---

    private void UpdateOverlays()
    {
        _board.SetLayer("charges", _sim.Units.Where(u => u.Charge?.Zone is not null).SelectMany(u => u.Charge!.Zone!),
            new Color(1f, 0.15f, 0.1f, 0.45f), 0.01f);
        _board.SetLayer("domes", _domes.Values.SelectMany(c => c), new Color(0.3f, 0.6f, 1f, 0.35f), 0.015f);

        var unit = Selected is { Status: UnitStatus.Active } selected ? selected : null;
        var from = _ghost ?? unit?.Position;
        IEnumerable<Cell> reachable = Array.Empty<Cell>(), path = Array.Empty<Cell>(), range = Array.Empty<Cell>(),
            aim = Array.Empty<Cell>(), target = Array.Empty<Cell>();
        Cell? ghostCell = _ghost;
        var hint = "";
        var hintColor = Colors.White;

        if (unit is not null && _aiming is null && _ghost is null && OrderOf(unit.Id) is { } order)
        {
            // Ordre donné, pas encore parti : seul l'ordre figé s'affiche, rien ne suit la souris. S'il est devenu
            // impossible entre-temps, on le dit (il sera annulé au départ s'il l'est encore).
            (hint, hintColor) = _sim.Check(order, ignoreTiming: true) is { } broken
                ? ($"Ordre donné, mais impossible pour l'instant ({Texts.Rejection(broken)}) : clic droit / Échap pour l'annuler",
                    new Color(1f, 0.5f, 0.45f))
                : ("Ordre donné : clic droit / Échap pour l'annuler, ou prépare-en un autre pour le remplacer", OrderHint);
        }
        else if (unit is not null && _aiming is null)
        {
            var cells = Reachable(unit);
            reachable = cells.Keys.Where(c => c != unit.Position);
            var destination = _ghost ?? (_hovered is { } h && cells.ContainsKey(h) ? h : null);
            if (destination is { } d && d != unit.Position)
            {
                path = _sim.PathFor(unit.Id, d) ?? [];
                ghostCell = d;
            }
            hint = !unit.IsReady ? "Jauge pas pleine : prépare ton ordre, il partira dès qu'elle sera pleine"
                : _ghost is null ? "Clique une case pour prévoir un déplacement, ou choisis directement une compétence (1-9)"
                : "Déplacement prévu : choisis une compétence (1-9), ou Entrée pour te déplacer seulement";
            if (_ghost is not null)
                hintColor = OrderHint;
        }
        else if (unit is not null && _aiming is { } aiming && from is { } origin)
        {
            if (aiming.Skill is { } skill && aiming.Kind != AimKind.Self)
            {
                // Portée de l'étape en cours : la cible, puis, en deux temps, la case (autour de la cible pour une
                // télékinésie, du lanceur pour le dépôt d'un sauvetage).
                var (center, radius) = aiming.FirstTarget is { } first && _sim.FindUnit(first) is { } chosen
                    ? skill.ForcedMove is { } move ? (chosen.Position, move.Distance) : (origin, skill.RescueDropRadius ?? skill.Range)
                    : (origin, skill.Range);
                range = Cells(c => c.DistanceTo(center) <= radius && c != center);
            }
            (aim, target, hint, hintColor) = AimPreview(unit, aiming, origin);
        }

        _board.SetLayer("reachable", reachable, new Color(0.3f, 0.6f, 1f, 0.25f), 0.02f);
        _board.SetLayer("range", range, new Color(1f, 1f, 1f, 0.15f), 0.02f);
        _board.SetLayer("path", path, _ghost is null ? PathHovered : PathPlaced, 0.025f);
        _board.SetLayer("aim", aim, AimColor, 0.03f);
        _board.SetLayer("target", target, TargetColor, 0.035f);
        _ghostView.Visible = ghostCell is not null;
        _ghostView.MaterialOverride = _ghost is null ? GhostHovered : GhostPlaced;
        if (ghostCell is { } g)
            _ghostView.Position = BoardView.ToWorld(g, 0.65f);
        var orders = _sim.PlannedOrders();
        ShowOrders(orders);
        if (hint.Length == 0 && orders.Count > 0 && _paused)
            (hint, hintColor) = ("Ordre donné : il partira dès que le héros pourra agir (Espace : reprendre)", OrderHint);
        _hud.SetHint(hint, hintColor);
    }

    private ActivateCommand? OrderOf(UnitId unit) => _sim.PlannedOrders().FirstOrDefault(o => o.Unit == unit);

    /// <summary>
    /// Ordres donnés, pas encore partis (jauge à remplir, ou pause) : fantôme, chemin et zone visée, figés, quel que soit
    /// le héros choisi. L'ordre d'un héros auquel on en prépare un autre reste affiché : c'est lui qui partira tant que
    /// le nouveau n'est pas donné. Recalculé seulement quand les ordres ou le tick changent.
    /// </summary>
    private void ShowOrders(IReadOnlyList<ActivateCommand> orders)
    {
        if (_shownOrders is { } shown && shown.Tick == _sim.CurrentTick && shown.Orders.SequenceEqual(orders))
            return;
        _shownOrders = (_sim.CurrentTick, orders);

        var ghosts = new List<Cell>();
        var path = new List<Cell>();
        var aim = new List<Cell>();
        var target = new List<Cell>();
        foreach (var command in orders)
        {
            var id = command.Unit;
            if (_sim.FindUnit(id) is not { } unit)
                continue;
            if (command.MoveTo is { } moveTo)
            {
                ghosts.Add(moveTo);
                path.AddRange(_sim.PathFor(id, moveTo) ?? []);
            }
            if (command.Skill is { } use)
            {
                var preview = _sim.PreviewAction(id, command.MoveTo ?? unit.Position, use);
                aim.AddRange(preview.Cells ?? []);
                if (preview.Destination is { } destination)
                    aim.Add(destination);
                if (preview.Target is { } t && _sim.FindUnit(t) is { } struck)
                    target.Add(struck.Position);
            }
            else if (command.Interaction is { } interaction && _sim.FindUnit(interaction.Target) is { } other)
            {
                target.Add(other.Position);
            }
        }

        _board.SetLayer("order-path", path, PathPlaced, 0.026f);
        _board.SetLayer("order-aim", aim, AimColor, 0.031f);
        _board.SetLayer("order-target", target, TargetColor, 0.036f);
        while (_orderGhosts.Count < ghosts.Count)
        {
            var ghost = new MeshInstance3D { Mesh = _ghostView.Mesh, MaterialOverride = GhostPlaced };
            AddChild(ghost);
            _orderGhosts.Add(ghost);
        }
        for (var i = 0; i < _orderGhosts.Count; i++)
        {
            _orderGhosts[i].Visible = i < ghosts.Count;
            if (i < ghosts.Count)
                _orderGhosts[i].Position = BoardView.ToWorld(ghosts[i], 0.65f);
        }
    }

    /// <summary>Ce que donnerait l'action armée sur la case survolée : cases touchées, cible, et un message.</summary>
    private (IEnumerable<Cell> Aim, IEnumerable<Cell> Target, string Hint, Color Color) AimPreview(Unit unit, Aiming aiming, Cell origin)
    {
        var none = Array.Empty<Cell>();
        var name = _ghost is null ? aiming.Name : $"Déplacement + {aiming.Name}";
        var prefix = aiming.Kind == AimKind.UnitThenCell && aiming.FirstTarget is null ? $"{name} : choisis l'unité"
            : aiming.Kind == AimKind.UnitThenCell ? $"{name} : choisis la case" : name;
        if (_hovered is not { } cell)
            return (none, none, prefix, Colors.White);

        var first = aiming.FirstTarget is { } firstId ? _sim.FindUnit(firstId) : null;
        var firstCells = first is null ? none : new[] { first.Position };
        if (aiming.CommandFor(unit.Id, _ghost, cell, _sim.UnitAt(cell)?.Id) is not { } command)
            return (none, firstCells, prefix, Colors.White);

        // Une seule validation complète si le héros peut agir ; sinon, une seconde sans le temps pour trouver le vrai obstacle.
        var now = _sim.Check(command);
        var waiting = now is { } n && n.OnlyNeedsTime();
        var reason = waiting ? _sim.Check(command, ignoreTiming: true) : now;
        IEnumerable<Cell> aim = none, target = firstCells;
        if (command.Skill is { } use)
        {
            var preview = _sim.PreviewAction(unit.Id, origin, use);
            aim = preview.Cells ?? none;
            if (preview.Target is { } t && _sim.FindUnit(t) is { } struck)
                target = target.Append(struck.Position);
            if (preview.Destination is { } destination)
                aim = aim.Append(destination);
        }
        else if (_sim.UnitAt(cell) is { } other)
        {
            target = new[] { other.Position };
        }

        if (reason is { } r)
            return (aim, target, $"{prefix} : {Texts.Rejection(r)}", new Color(1f, 0.5f, 0.45f));
        var damage = aiming.Skill is { DealsDamage: true } s ? $" ({s.MinDamage}-{s.MaxDamage} dégâts)" : "";
        var when = waiting ? "clic : partira dès que possible" : "clic pour lancer";
        return (aim, target, $"{prefix}{damage} : {when}", new Color(0.6f, 1f, 0.6f));
    }

    private IEnumerable<Cell> Cells(Func<Cell, bool> predicate)
    {
        for (var y = 0; y < _sim.Grid.Height; y++)
            for (var x = 0; x < _sim.Grid.Width; x++)
                if (_sim.Grid.IsWalkable(new Cell(x, y)) && predicate(new Cell(x, y)))
                    yield return new Cell(x, y);
    }

    // --- Affichage ---

    private void UpdateHud()
    {
        var seconds = _sim.CurrentTick / (double)_data.Rules.TicksPerSecond;
        var status = $"Temps : {seconds:0.0} s   Vitesse : {Speeds[_speed]} %{(_paused ? "   — PAUSE —" : "")}";
        _hud.SetStatus(status + (_showIntentions ? "\nIntentions ennemies révélées" : ""));
        if (_won is null)
            _hud.SetBanner(_paused ? "PAUSE" : "", Colors.White);

        _hud.SetHeroes(_sim.Units
            .Where(u => u.OriginalTeam == PlayerTeam && u.SummonedBy is null && !u.IsCivilian)
            .Select(HeroCardOf)
            .ToList());

        if (Selected is not { } unit)
        {
            var ready = _sim.Units.Count(u => u.IsReady && IsControllable(u));
            _hud.SetSelection(ready > 0 ? $"{ready} héros prêt(s) : choisis-en un (cartes à gauche, F1-F6 ou Tab)" : "Aucun héros prêt",
                Array.Empty<ActionButton>());
            return;
        }

        var header = $"{unit.Definition.Name} — {unit.Hp}/{unit.MaxHp} PV — jauge {100 * unit.Gauge / _data.Rules.GaugeCapacity} %"
                     + (HasOrder(unit.Id) ? " — ORDRE DONNÉ" : unit.IsReady ? " — PRÊT" : "")
                     + (_ghost is not null ? " — déplacement prévu" : "");
        if (unit.StatusEndTicks.Count > 0)
            header += "\n" + string.Join(", ", unit.StatusEndTicks.Keys.Select(Texts.Status));

        var actions = new List<ActionButton>();
        for (var i = 0; i < unit.Skills.Count; i++)
        {
            var skill = unit.Skills[i];
            var cooldown = unit.CooldownEndTick(skill.Id) - _sim.CurrentTick;
            var damage = skill.DealsDamage ? $", {skill.MinDamage}-{skill.MaxDamage}" : "";
            var weight = skill.Weight == SkillWeight.Heavy ? "lourde" : "légère";
            var details = cooldown > 0
                ? $"recharge {cooldown / (double)_data.Rules.TicksPerSecond:0.0} s"
                : $"{weight}, portée {skill.Range}{damage}";
            var key = i < 9 ? $"{i + 1}  " : "";
            actions.Add(new ActionButton($"{key}{skill.DisplayName}\n{details}",
                Enabled: true, Armed: _aiming?.Skill == skill, Cooldown: CooldownLeft(unit, skill)));
        }
        actions.Add(new ActionButton("R  Relever\nun allié au contact", Enabled: true, Armed: _aiming?.Interaction is not null));
        actions.Add(new ActionButton($"Entrée  Déplacement seul\n{(_ghost is null ? "choisis d'abord une case" : "vers le fantôme")}",
            Enabled: _ghost is not null, Armed: false));
        _hud.SetSelection(header, actions);
    }

    private HeroCard HeroCardOf(Unit hero)
    {
        // Un héros dominé ou confus n'obéit plus : jamais « prêt » pour le joueur.
        var ready = hero.IsReady && IsControllable(hero) && !HasOrder(hero.Id);
        var state = hero.Status == UnitStatus.Downed ? "À TERRE"
            : HasOrder(hero.Id) ? (hero.IsReady ? "ordre donné" : "ordre en attente")
            : ready ? "PRÊT"
            : hero.Charge is not null ? "charge…"
            : string.Join(", ", hero.StatusEndTicks.Keys.Select(Texts.Status));
        return new HeroCard(hero.Id, hero.Definition.Name, (float)hero.Hp / hero.MaxHp, $"{hero.Hp}/{hero.MaxHp} PV",
            Math.Min(1f, (float)hero.Gauge / _data.Rules.GaugeCapacity), Ready: ready, Selected: hero.Id == _selected, State: state,
            SkillReadiness: hero.Skills.Select(s => 1f - CooldownLeft(hero, s)).ToList());
    }

    /// <summary>Part de la recharge de la compétence qu'il reste à attendre : 1 juste après usage, 0 quand elle est prête.</summary>
    private float CooldownLeft(Unit unit, SkillDefinition skill)
    {
        var total = _data.Rules.MillisecondsToTicks(skill.CooldownMs);
        var left = unit.CooldownEndTick(skill.Id) - _sim.CurrentTick;
        return total <= 0 || left <= 0 ? 0f : Math.Min(1f, (float)left / total);
    }

    private string? IntentionText(Unit unit)
    {
        if (!_showIntentions || unit.Intention is not { } intention || unit.Status != UnitStatus.Active)
            return null;
        if (intention.SkillId is null && intention.Target == unit.Id)
            return "→ fuit";
        var target = _sim.FindUnit(intention.Target)?.Definition.Name ?? "?";
        var skill = intention.SkillId is { } id ? unit.FindSkill(id)?.DisplayName : null;
        return skill is null ? $"→ {target}" : $"→ {skill} sur {target}";
    }

    private void Float(UnitId unit, string text, Color color)
    {
        if (!_views.TryGetValue(unit, out var view))
            return;
        var label = new Label3D
        {
            Text = text,
            Modulate = color,
            FontSize = 56,
            PixelSize = 0.006f,
            OutlineSize = 10,
            NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = view.Position + new Vector3(0, 1.4f, 0),
        };
        AddChild(label);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(label, "position", label.Position + new Vector3(0, 1f, 0), 1.0);
        tween.TweenProperty(label, "modulate:a", 0.0, 1.0);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    private static Color ColorOf(Unit unit)
    {
        if (unit.IsCivilian)
            return Civilian;
        if (unit.SummonedBy is not null)
            return unit.OriginalTeam == PlayerTeam ? HeroGadget : EnemyDefault;
        return UnitColors.TryGetValue(unit.Definition.Id, out var color) ? color : EnemyDefault;
    }
}
