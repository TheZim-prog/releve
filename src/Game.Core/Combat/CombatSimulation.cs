using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Simulation d'un combat, à ticks fixes et entièrement déterministe.
/// <para>
/// Déroulement d'un tick : les états arrivés à échéance prennent fin ; les commandes soumises depuis
/// le tick précédent sont appliquées dans leur ordre d'arrivée (la première validée l'emporte en cas
/// de conflit) ; puis les jauges se remplissent.
/// </para>
/// <para>
/// La pause n'existe pas ici : c'est la boucle qui appelle <see cref="Tick"/> qui s'arrête.
/// L'état initial (unités, jauges) est connu du client : seuls les changements produisent des événements.
/// </para>
/// </summary>
public sealed class CombatSimulation
{
    private readonly List<Unit> _units = new();
    private readonly Queue<CombatCommand> _pending = new();
    private readonly List<CombatEvent> _events = new();
    private readonly List<LoggedCommand> _commandLog = new();
    private readonly DeterministicRandom _random;

    // Copie privée : le décor ne doit changer que par la simulation, sinon le rejeu diverge.
    // Le client connaît la grille initiale ; ses changements futurs passeront par des événements.
    private readonly Grid _grid;

    public CombatSimulation(CombatSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(setup.Rules);
        ArgumentNullException.ThrowIfNull(setup.Grid);
        ArgumentNullException.ThrowIfNull(setup.Units);
        setup.Rules.Validate();

        Rules = setup.Rules;
        _grid = setup.Grid.Clone();
        _random = new DeterministicRandom(setup.Seed);
        Units = _units.AsReadOnly();
        EventLog = _events.AsReadOnly();
        CommandLog = _commandLog.AsReadOnly();

        foreach (var unit in setup.Units)
        {
            Validate(unit);
            _units.Add(new Unit(unit, Rules.GaugeCapacity));
        }
    }

    public CombatRules Rules { get; }

    /// <summary>Numéro du dernier tick joué (0 avant le premier).</summary>
    public int CurrentTick { get; private set; }

    /// <summary>Unités (retirées comprises), dans l'ordre de mise en place (ordre de traitement des jauges).</summary>
    public IReadOnlyList<Unit> Units { get; }

    public IReadOnlyList<CombatEvent> EventLog { get; }

    /// <summary>Commandes appliquées, avec leur tick : avec l'état initial, de quoi rejouer le combat.</summary>
    public IReadOnlyList<LoggedCommand> CommandLog { get; }

    public Unit? FindUnit(UnitId id) => _units.Find(u => u.Id == id);

    /// <summary>Unité présente sur la case (active ou à terre ; une unité retirée n'occupe plus rien).</summary>
    public Unit? UnitAt(Cell cell) => _units.Find(u => u.Position == cell && u.Status != UnitStatus.Removed);

    /// <summary>Met une commande en attente ; elle sera validée et appliquée au prochain tick.</summary>
    public void Submit(CombatCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not ActivateCommand)
            throw new ArgumentException($"Unknown command type {command.GetType().Name}.", nameof(command));
        _pending.Enqueue(command);
    }

    /// <summary>Joue un tick et renvoie les événements qu'il a produits.</summary>
    public IReadOnlyList<CombatEvent> Tick()
    {
        CurrentTick++;
        var firstEvent = _events.Count;

        ExpireStatuses();

        while (_pending.TryDequeue(out var command))
        {
            _commandLog.Add(new LoggedCommand(CurrentTick, command));
            Apply(command);
        }

        foreach (var unit in _units)
        {
            if (unit.Status == UnitStatus.Active && !unit.IsGaugeFull)
                SetGauge(unit, Math.Min(Rules.GaugeCapacity, unit.Gauge + FillAmount(unit)));
        }

        return _events.GetRange(firstEvent, _events.Count - firstEvent);
    }

    /// <summary>
    /// Points de jauge gagnés ce tick : seul endroit où se décide la vitesse de la jauge
    /// (initiative, étourdi, déséquilibré ; plus tard l'hypervitesse…).
    /// </summary>
    private int FillAmount(Unit unit)
    {
        if (unit.HasStatus(StatusKind.Stunned))
            return 0;
        if (!unit.HasStatus(StatusKind.OffBalance))
            return unit.Initiative;

        var hundredths = (long)unit.Initiative * Rules.OffBalanceFillPercent + unit.FillRemainder;
        unit.FillRemainder = (int)(hundredths % 100);
        return (int)(hundredths / 100);
    }

    private void ExpireStatuses()
    {
        foreach (var unit in _units)
        {
            if (unit.StatusEndTicks.Count == 0)
                continue;

            var expired = unit.StatusEndTicks.Where(s => s.Value <= CurrentTick).Select(s => s.Key).ToList();
            foreach (var status in expired)
                EndStatus(unit, status);
        }
    }

    private void EndStatus(Unit unit, StatusKind status)
    {
        var wasReady = unit.IsReady;
        unit.ClearStatus(status);
        if (status == StatusKind.OffBalance)
            unit.FillRemainder = 0;
        _events.Add(new StatusEnded(CurrentTick, unit.Id, status));
        if (!wasReady && unit.IsReady)
            _events.Add(new UnitReady(CurrentTick, unit.Id));
    }

    /// <summary>
    /// Rejoue un combat depuis son état initial et son journal de commandes, jusqu'au tick <paramref name="untilTick"/>.
    /// </summary>
    public static CombatSimulation Replay(CombatSetup setup, IEnumerable<LoggedCommand> commandLog, int untilTick)
    {
        var simulation = new CombatSimulation(setup);
        var commands = new Queue<LoggedCommand>(commandLog);

        while (simulation.CurrentTick < untilTick)
        {
            var nextTick = simulation.CurrentTick + 1;
            while (commands.TryPeek(out var logged) && logged.Tick <= nextTick)
            {
                if (logged.Tick < nextTick)
                    throw new ArgumentException($"Command log is not ordered by tick (tick {logged.Tick} found at tick {nextTick}).", nameof(commandLog));
                simulation.Submit(commands.Dequeue().Command);
            }
            simulation.Tick();
        }

        return simulation;
    }

    private void Apply(CombatCommand command)
    {
        switch (command)
        {
            case ActivateCommand activate:
                ApplyActivate(activate);
                break;
            default:
                throw new ArgumentException($"Unknown command type {command.GetType().Name}.", nameof(command));
        }
    }

    private void ApplyActivate(ActivateCommand command)
    {
        // Validation complète d'abord : la commande est atomique, rien n'est appliqué si une partie échoue.
        var unit = FindUnit(command.Unit);
        if (unit is null)
        {
            Reject(command, RejectionReason.UnknownUnit);
            return;
        }
        if (unit.Status != UnitStatus.Active)
        {
            Reject(command, RejectionReason.Incapacitated);
            return;
        }
        if (unit.HasStatus(StatusKind.Stunned))
        {
            Reject(command, RejectionReason.Stunned);
            return;
        }
        if (!unit.IsReady)
        {
            Reject(command, RejectionReason.NotReady);
            return;
        }
        if (command.MoveTo is null && command.Skill is null)
        {
            Reject(command, RejectionReason.Empty);
            return;
        }

        var position = unit.Position;
        IReadOnlyList<Cell>? path = null;
        if (command.MoveTo is { } destination)
        {
            if (destination == unit.Position)
            {
                Reject(command, RejectionReason.InvalidDestination);
                return;
            }

            path = Pathfinder.FindPath(_grid, unit.Position, destination, unit.Mobility, cell => OccupantFor(unit, cell));
            if (path is null)
            {
                Reject(command, RejectionReason.Unreachable);
                return;
            }
            position = destination;
        }

        (SkillDefinition Skill, Unit Target)? action = null;
        if (command.Skill is { } use)
        {
            var skill = unit.FindSkill(use.SkillId);
            if (skill is null)
            {
                Reject(command, RejectionReason.UnknownSkill);
                return;
            }
            if (CurrentTick < unit.CooldownEndTick(skill.Id))
            {
                Reject(command, RejectionReason.OnCooldown);
                return;
            }

            var target = FindUnit(use.Target);
            if (target is null || target.Status != UnitStatus.Active || target.Team == unit.Team)
            {
                Reject(command, RejectionReason.InvalidTarget);
                return;
            }
            if (!IsWithinRange(position, target.Position, skill.Range))
            {
                Reject(command, RejectionReason.OutOfRange);
                return;
            }
            if (!HasLineOfSight(unit, position, target.Position))
            {
                Reject(command, RejectionReason.NoLineOfSight);
                return;
            }
            action = (skill, target);
        }

        // Application.
        unit.Gauge = 0; // l'unité a agi : sa jauge n'est plus pleine, même si elle repart à 100 %
        unit.OpportunityAttacksLeft = unit.OpportunityAttacksPerCycle; // rechargées quand l'unité rejoue

        // Tombée ou étourdie en chemin par une attaque d'opportunité : le reste du trajet et l'action sont
        // annulés. Étourdie, elle a quand même bougé : sa jauge repart comme après un déplacement seul.
        if (path is not null && !MoveAlong(unit, path))
        {
            if (unit.Status == UnitStatus.Active)
                SetGauge(unit, Rules.GaugeAt(Rules.MoveOnlyRestartPercent));
            return;
        }

        var restartPercent = Rules.MoveOnlyRestartPercent;
        if (action is { } validAction)
        {
            var (skill, target) = validAction;
            unit.StartCooldown(skill.Id, CurrentTick + Rules.MillisecondsToTicks(skill.CooldownMs));
            _events.Add(new SkillUsed(CurrentTick, unit.Id, skill.Id, target.Id));
            ResolveHit(skill, target);
            restartPercent = skill.Weight == SkillWeight.Heavy
                ? Rules.HeavyActionRestartPercent
                : Rules.LightActionRestartPercent;
        }
        SetGauge(unit, Rules.GaugeAt(restartPercent));
    }

    /// <summary>
    /// Déplace l'unité pas à pas ; chaque pas qui sort de la portée d'un ennemi peut déclencher son attaque
    /// d'opportunité (dans l'ordre des unités). Renvoie faux si l'unité est tombée ou étourdie en chemin.
    /// </summary>
    private bool MoveAlong(Unit unit, IReadOnlyList<Cell> path)
    {
        var segmentStart = unit.Position;
        var segment = new List<Cell>();
        var lastFreeCell = unit.Position;

        void FlushSegment()
        {
            if (segment.Count == 0)
                return;
            _events.Add(new UnitMoved(CurrentTick, unit.Id, segmentStart, segment));
            segmentStart = unit.Position;
            segment = new List<Cell>();
        }

        foreach (var next in path)
        {
            foreach (var enemy in _units)
            {
                if (!CanMakeOpportunityAttack(enemy, unit, next))
                    continue;

                FlushSegment();
                var attack = enemy.BasicAttack!;
                enemy.OpportunityAttacksLeft--;
                _events.Add(new OpportunityAttack(CurrentTick, enemy.Id, unit.Id, attack.Id));
                ResolveHit(attack, unit, lastFreeCell);
                if (unit.Status != UnitStatus.Active)
                    return false;
                if (unit.HasStatus(StatusKind.Stunned))
                {
                    if (IsOccupiedByOther(unit, unit.Position))
                        unit.Position = lastFreeCell;
                    _events.Add(new UnitStopped(CurrentTick, unit.Id, unit.Position));
                    return false;
                }
            }

            unit.Position = next;
            segment.Add(next);
            if (!IsOccupiedByOther(unit, next))
                lastFreeCell = next;
        }

        FlushSegment();
        return true;
    }

    /// <summary>
    /// <paramref name="enemy"/> peut punir <paramref name="mover"/> qui va en <paramref name="next"/> : le pas sort
    /// de sa portée, et c'est un ennemi debout, non étourdi, avec une attaque de base et une attaque d'opportunité restante.
    /// </summary>
    private bool CanMakeOpportunityAttack(Unit enemy, Unit mover, Cell next) =>
        enemy != mover
        && enemy.Team != mover.Team
        && enemy.Status == UnitStatus.Active
        && !enemy.HasStatus(StatusKind.Stunned)
        && enemy.BasicAttack is not null
        && enemy.OpportunityAttacksLeft > 0
        && IsInMeleeReach(enemy, mover.Position)
        && !IsInMeleeReach(enemy, next);

    /// <summary>Case que l'unité peut frapper au contact : adjacente, sans coupe d'angle par un mur.</summary>
    private bool IsInMeleeReach(Unit unit, Cell cell) => _grid.CanStep(unit.Position, cell);

    private bool IsOccupiedByOther(Unit unit, Cell cell) =>
        _units.Any(u => u != unit && u.Status != UnitStatus.Removed && u.Position == cell);

    /// <summary>
    /// Effets d'un coup sur sa cible : dégâts (s'il y en a), puis états si elle est encore debout.
    /// <paramref name="fallbackCell"/> : où tombe la cible si elle est à terre sur une case qu'elle partage
    /// (seulement possible en plein déplacement, en traversant un allié).
    /// </summary>
    private void ResolveHit(SkillDefinition skill, Unit target, Cell? fallbackCell = null)
    {
        if (skill.DealsDamage)
            Damage(target, _random.NextInt(skill.MinDamage, skill.MaxDamage), fallbackCell);
        if (target.Status != UnitStatus.Active)
            return;
        foreach (var effect in skill.Effects)
            ApplyStatus(target, effect);
    }

    /// <summary>Distance en cases (diagonale = 1), sans tenir compte des obstacles.</summary>
    private static bool IsWithinRange(Cell from, Cell to, int range)
    {
        var distance = from.DistanceTo(to);
        return distance > 0 && distance <= range;
    }

    /// <summary>
    /// Vue de <paramref name="from"/> (case d'arrivée de l'attaquant) vers <paramref name="to"/>.
    /// Au contact : pas de coupe d'angle, mais seul le décor compte (règle du corps à corps).
    /// Au-delà : ligne de vue, bloquée par les murs et les ennemis debout de l'attaquant.
    /// </summary>
    private bool HasLineOfSight(Unit attacker, Cell from, Cell to)
    {
        if (from.IsAdjacentTo(to))
            return _grid.CanStep(from, to);
        return LineOfSight.IsClear(_grid, from, to, cell => BlocksSightOf(attacker, cell));
    }

    private void Damage(Unit target, int amount, Cell? fallbackCell)
    {
        target.Hp = Math.Max(0, target.Hp - amount);
        _events.Add(new UnitDamaged(CurrentTick, target.Id, amount, target.Hp));
        if (target.Hp > 0)
            return;

        // La jauge est gelée à sa valeur : seules les unités actives la remplissent.
        // Le statut change d'abord, pour que la fin d'un étourdissement n'annonce pas l'unité prête.
        target.Status = target.OnDefeat == DefeatOutcome.Downed ? UnitStatus.Downed : UnitStatus.Removed;

        // Une unité vaincue perd ses états, annoncés avant sa chute (rien n'arrive plus après).
        foreach (var status in target.StatusEndTicks.Keys.ToList())
            EndStatus(target, status);

        if (target.Status == UnitStatus.Removed)
        {
            _events.Add(new UnitRemoved(CurrentTick, target.Id));
            return;
        }

        // Une unité par case : à terre en traversant un allié, elle tombe sur la dernière case libre.
        if (IsOccupiedByOther(target, target.Position))
            target.Position = fallbackCell ?? throw new InvalidOperationException($"Unit {target.Id} fell on an occupied cell.");
        _events.Add(new UnitDowned(CurrentTick, target.Id, target.Position));
    }

    /// <summary>Applique un état ; s'il est déjà là, garde la plus longue des deux durées.</summary>
    private void ApplyStatus(Unit target, StatusEffect effect)
    {
        var endTick = CurrentTick + Rules.MillisecondsToTicks(effect.DurationMs);
        if (target.StatusEndTicks.TryGetValue(effect.Status, out var currentEnd))
            endTick = Math.Max(endTick, currentEnd);

        target.SetStatus(effect.Status, endTick);
        _events.Add(new StatusApplied(CurrentTick, target.Id, effect.Status, endTick));
    }

    /// <summary>Change la jauge et annonce l'unité prête si elle vient de se remplir.</summary>
    private void SetGauge(Unit unit, int gauge)
    {
        var wasReady = unit.IsReady;
        unit.Gauge = gauge;
        if (!wasReady && unit.IsReady)
            _events.Add(new UnitReady(CurrentTick, unit.Id));
    }

    // Qui fait obstacle à qui. Les deux règles diffèrent volontairement (docs/design.md, section 3) :
    // un héros à terre occupe toujours sa case pour le déplacement, mais on tire par-dessus lui.

    /// <summary>
    /// Pour le déplacement : toute unité présente (debout ou à terre), alliée ou ennemie, vue par
    /// <paramref name="mover"/>. Public pour l'aperçu des cases atteignables (<see cref="Pathfinder"/>).
    /// </summary>
    public Occupant OccupantFor(Unit mover, Cell cell)
    {
        var other = UnitAt(cell);
        if (other is null || other == mover)
            return Occupant.None;
        return other.Team == mover.Team ? Occupant.Ally : Occupant.Enemy;
    }

    /// <summary>Pour la ligne de vue : seuls les ennemis debout bloquent.</summary>
    private bool BlocksSightOf(Unit viewer, Cell cell) =>
        UnitAt(cell) is { Status: UnitStatus.Active } other && other.Team != viewer.Team;

    private void Reject(CombatCommand command, RejectionReason reason) =>
        _events.Add(new CommandRejected(CurrentTick, command, reason));

    private void Validate(UnitSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (FindUnit(setup.Id) is not null)
            throw new ArgumentException($"Duplicate unit id {setup.Id}.");
        if (_grid.IsBlocked(setup.Position))
            throw new ArgumentException($"Unit {setup.Id} is on a blocked cell {setup.Position}.");
        if (UnitAt(setup.Position) is not null)
            throw new ArgumentException($"Unit {setup.Id} shares cell {setup.Position} with another unit.");
        if (setup.Initiative < 0 || setup.Mobility < 0)
            throw new ArgumentException($"Unit {setup.Id} has negative initiative or mobility.");
        // Au-delà, la jauge se remplirait en moins d'un tick ; borner évite aussi tout débordement.
        if (setup.Initiative > Rules.GaugeCapacity)
            throw new ArgumentException($"Unit {setup.Id} has an initiative above the gauge capacity.");
        if (setup.InitialGauge < 0 || setup.InitialGauge > Rules.GaugeCapacity)
            throw new ArgumentException($"Unit {setup.Id} has an initial gauge outside [0, {Rules.GaugeCapacity}].");
        if (setup.MaxHp <= 0)
            throw new ArgumentException($"Unit {setup.Id} needs positive max HP.");
        if (!Enum.IsDefined(setup.OnDefeat))
            throw new ArgumentException($"Unit {setup.Id} has an unknown defeat outcome.");
        SkillDefinition.ValidateAll(setup.Skills);
        if (setup.BasicAttack is not null && setup.Skills.All(s => s.Id != setup.BasicAttack))
            throw new ArgumentException($"Unit {setup.Id}: basic attack '{setup.BasicAttack}' is not one of its skills.");
        if (setup.OpportunityAttacksPerCycle < 0)
            throw new ArgumentException($"Unit {setup.Id} has a negative number of opportunity attacks.");
    }
}
