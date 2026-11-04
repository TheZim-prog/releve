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

    private readonly Dictionary<UnitId, UnitView> _views = new();
    private readonly Dictionary<UnitId, IReadOnlyList<Cell>> _domes = new();

    private GameData _data = null!;
    private CombatSimulation _sim = null!;
    private BoardView _board = null!;
    private Hud _hud = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _ghostView = null!;

    // Unités déjà commandées depuis le dernier tick (leur ordre attend d'être appliqué, par exemple pendant la pause).
    private readonly HashSet<UnitId> _ordered = new();

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
    // l'affichage sur une capture).
    private string? _screenshotPath;
    private double _screenshotIn;
    private Cell? _hoverOverride;

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
            MaterialOverride = BoardView.Translucent(new Color(1, 1, 1, 0.35f)),
            Visible = false,
        };
        AddChild(_ghostView);
        _hud = new Hud();
        AddChild(_hud);
        _hud.ActionPressed += ArmAction;
        _hud.HeroPressed += SelectIfControllable;
        StartCombat();
        ApplyDevArguments();
    }

    private void ApplyDevArguments()
    {
        string? Arg(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];
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
        _sim = new CombatSimulation(scenario.ToSetup(_data.Rules, _data.Units));
        _board = new BoardView();
        AddChild(_board);
        _board.Build(_sim.Grid);

        var center = new Vector3((_sim.Grid.Width - 1) / 2f, 0, (_sim.Grid.Height - 1) / 2f);
        // Le plateau tient en largeur (16/9) avec une marge, et descend un peu pour laisser la place au HUD du haut.
        _camera.Size = Math.Max(_sim.Grid.Width * 9f / 16f + 4.5f, _sim.Grid.Height + 6.5f);
        var focus = center + new Vector3(0, 0, -0.8f);
        _camera.LookAtFromPosition(focus + new Vector3(0, 30f, 14f), focus, Vector3.Up);

        _accumulator = 0;
        _paused = false;
        _won = null;
        _ordered.Clear();
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
            _ordered.Clear();

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
                if (Selected is { } unit && _ghost is { } ghost)
                    TrySubmit(new ActivateCommand(unit.Id, ghost));
                break;
            case Key.R:
                if (Selected is { } reviver)
                    ArmAction(reviver.Skills.Count);
                break;
        }
    }

    /// <summary>
    /// Arme l'action n° <paramref name="index"/> du héros choisi : une compétence (sauf en recharge), ou relever (juste
    /// après la dernière compétence, comme le dernier bouton).
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
        var skill = unit.Skills[index];
        if (unit.CooldownEndTick(skill.Id) > _sim.CurrentTick)
            _hud.Log($"{skill.DisplayName} : {Texts.Rejection(RejectionReason.OnCooldown)}");
        else
            _aiming = new Aiming(skill);
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
        if (_ghost == cell)
            TrySubmit(new ActivateCommand(selectedUnit.Id, cell));
        else if (cell == selectedUnit.Position)
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
        var heroes = _sim.Units.Where(u => u.IsReady && IsControllable(u) && !_ordered.Contains(u.Id)).ToList();
        if (heroes.Count == 0)
            return;
        var current = heroes.FindIndex(u => u.Id == _selected);
        Select(heroes[(current + 1) % heroes.Count].Id);
    }

    /// <summary>Soumet la commande si la simulation l'accepterait ; sinon, dit pourquoi.</summary>
    private void TrySubmit(ActivateCommand command)
    {
        if (_ordered.Contains(command.Unit))
        {
            _hud.Log("Ordre déjà donné : il sera appliqué au prochain tick");
            return;
        }
        if (_sim.Check(command) is { } reason)
        {
            _hud.Log($"Impossible : {Texts.Rejection(reason)}");
            return;
        }
        _sim.Submit(command);
        _ordered.Add(command.Unit);
        ClearPlan(keepSelection: true);
    }

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

        if (unit is not null && _aiming is null)
        {
            var cells = Reachable(unit);
            reachable = cells.Keys.Where(c => c != unit.Position);
            var destination = _ghost ?? (_hovered is { } h && cells.ContainsKey(h) ? h : null);
            if (destination is { } d && d != unit.Position)
            {
                path = _sim.PathFor(unit.Id, d) ?? [];
                ghostCell = d;
            }
            hint = unit.IsReady ? "Choisis une case (fantôme), puis une compétence (1-9) ou confirme le déplacement"
                : "Jauge pas pleine : tu peux préparer, mais pas encore agir";
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
        _board.SetLayer("path", path, new Color(1f, 1f, 0.4f, 0.45f), 0.025f);
        _board.SetLayer("aim", aim, new Color(1f, 0.55f, 0.1f, 0.5f), 0.03f);
        _board.SetLayer("target", target, new Color(1f, 0.2f, 0.6f, 0.55f), 0.035f);
        _ghostView.Visible = ghostCell is not null;
        if (ghostCell is { } g)
            _ghostView.Position = BoardView.ToWorld(g, 0.65f);
        _hud.SetHint(hint, hintColor);
    }

    /// <summary>Ce que donnerait l'action armée sur la case survolée : cases touchées, cible, et un message.</summary>
    private (IEnumerable<Cell> Aim, IEnumerable<Cell> Target, string Hint, Color Color) AimPreview(Unit unit, Aiming aiming, Cell origin)
    {
        var none = Array.Empty<Cell>();
        var prefix = aiming.Kind == AimKind.UnitThenCell && aiming.FirstTarget is null ? $"{aiming.Name} : choisis l'unité"
            : aiming.Kind == AimKind.UnitThenCell ? $"{aiming.Name} : choisis la case" : aiming.Name;
        if (_hovered is not { } cell)
            return (none, none, prefix, Colors.White);

        var first = aiming.FirstTarget is { } firstId ? _sim.FindUnit(firstId) : null;
        var firstCells = first is null ? none : new[] { first.Position };
        if (aiming.CommandFor(unit.Id, _ghost, cell, _sim.UnitAt(cell)?.Id) is not { } command)
            return (none, firstCells, prefix, Colors.White);

        var reason = _sim.Check(command);
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
        return (aim, target, $"{prefix}{damage} : clic pour lancer", new Color(0.6f, 1f, 0.6f));
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
            _hud.SetSelection(ready > 0 ? $"{ready} héros prêt(s) : choisis-en un (barre du bas, F1-F6 ou Tab)" : "Aucun héros prêt",
                Array.Empty<ActionButton>());
            return;
        }

        var header = $"{unit.Definition.Name} — {unit.Hp}/{unit.MaxHp} PV — jauge {100 * unit.Gauge / _data.Rules.GaugeCapacity} %"
                     + (_ordered.Contains(unit.Id) ? " — ORDRE DONNÉ" : unit.IsReady ? " — PRÊT" : "");
        if (unit.StatusEndTicks.Count > 0)
            header += "\n" + string.Join(", ", unit.StatusEndTicks.Keys.Select(Texts.Status));

        var actions = new List<ActionButton>();
        for (var i = 0; i < unit.Skills.Count; i++)
        {
            var skill = unit.Skills[i];
            var cooldown = unit.CooldownEndTick(skill.Id) - _sim.CurrentTick;
            var damage = skill.DealsDamage ? $", {skill.MinDamage}-{skill.MaxDamage}" : "";
            var weight = skill.Weight == SkillWeight.Heavy ? "lourde" : "légère";
            var state = cooldown > 0 ? $"   [recharge {cooldown / (double)_data.Rules.TicksPerSecond:0.0} s]" : "";
            var key = i < 9 ? $"{i + 1}  " : "    ";
            actions.Add(new ActionButton($"{key}{skill.DisplayName} ({weight}, portée {skill.Range}{damage}){state}",
                Enabled: cooldown <= 0, Armed: _aiming?.Skill == skill));
        }
        actions.Add(new ActionButton("R  Relever un allié au contact", Enabled: true, Armed: _aiming?.Interaction is not null));
        _hud.SetSelection(header, actions);
    }

    private HeroCard HeroCardOf(Unit hero)
    {
        // Un héros dominé ou confus n'obéit plus : jamais « prêt » pour le joueur.
        var ready = hero.IsReady && IsControllable(hero) && !_ordered.Contains(hero.Id);
        var state = hero.Status == UnitStatus.Downed ? "À TERRE"
            : _ordered.Contains(hero.Id) ? "ordre donné"
            : ready ? "PRÊT"
            : hero.Charge is not null ? "charge…"
            : string.Join(", ", hero.StatusEndTicks.Keys.Select(Texts.Status));
        return new HeroCard(hero.Id, hero.Definition.Name, (float)hero.Hp / hero.MaxHp, $"{hero.Hp}/{hero.MaxHp} PV",
            Math.Min(1f, (float)hero.Gauge / _data.Rules.GaugeCapacity), Ready: ready, Selected: hero.Id == _selected, State: state);
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
