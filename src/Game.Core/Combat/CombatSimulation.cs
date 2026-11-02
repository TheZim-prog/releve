using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Simulation d'un combat, à ticks fixes et entièrement déterministe.
/// <para>
/// Déroulement d'un tick : les commandes soumises depuis le tick précédent sont appliquées dans leur
/// ordre d'arrivée (la première validée l'emporte en cas de conflit), puis les jauges se remplissent.
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

    // Copie privée : le décor ne doit changer que par la simulation, sinon le rejeu diverge.
    // Le client connaît la grille initiale ; ses changements futurs passeront par des événements.
    private readonly Grid _grid;

    public CombatSimulation(CombatRules rules, Grid grid, IEnumerable<UnitSetup> units)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(units);
        rules.Validate();

        Rules = rules;
        _grid = grid.Clone();
        Units = _units.AsReadOnly();
        EventLog = _events.AsReadOnly();
        CommandLog = _commandLog.AsReadOnly();

        foreach (var setup in units)
        {
            Validate(setup);
            _units.Add(new Unit(setup, rules.GaugeCapacity));
        }
    }

    public CombatRules Rules { get; }

    /// <summary>Numéro du dernier tick joué (0 avant le premier).</summary>
    public int CurrentTick { get; private set; }

    /// <summary>Unités, dans l'ordre de mise en place (ordre de traitement des jauges).</summary>
    public IReadOnlyList<Unit> Units { get; }

    public IReadOnlyList<CombatEvent> EventLog { get; }

    /// <summary>Commandes appliquées, avec leur tick : avec l'état initial, de quoi rejouer le combat.</summary>
    public IReadOnlyList<LoggedCommand> CommandLog { get; }

    public Unit? FindUnit(UnitId id) => _units.Find(u => u.Id == id);

    public Unit? UnitAt(Cell cell) => _units.Find(u => u.Position == cell);

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

        while (_pending.TryDequeue(out var command))
        {
            _commandLog.Add(new LoggedCommand(CurrentTick, command));
            Apply(command);
        }

        foreach (var unit in _units)
        {
            if (!unit.IsReady)
                SetGauge(unit, Math.Min(Rules.GaugeCapacity, unit.Gauge + unit.Initiative));
        }

        return _events.GetRange(firstEvent, _events.Count - firstEvent);
    }

    /// <summary>
    /// Rejoue un combat depuis son état initial et son journal de commandes, jusqu'au tick <paramref name="untilTick"/>.
    /// </summary>
    public static CombatSimulation Replay(
        CombatRules rules, Grid grid, IEnumerable<UnitSetup> units, IEnumerable<LoggedCommand> commandLog, int untilTick)
    {
        var simulation = new CombatSimulation(rules, grid, units);
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
        var unit = FindUnit(command.Unit);
        if (unit is null)
        {
            Reject(command, RejectionReason.UnknownUnit);
            return;
        }
        if (!unit.IsReady)
        {
            Reject(command, RejectionReason.NotReady);
            return;
        }
        if (command.MoveTo is not { } destination)
        {
            Reject(command, RejectionReason.Empty);
            return;
        }
        if (destination == unit.Position)
        {
            Reject(command, RejectionReason.InvalidDestination);
            return;
        }

        var path = Pathfinder.FindPath(_grid, unit.Position, destination, unit.Mobility, cell => OccupantFor(unit, cell));
        if (path is null)
        {
            Reject(command, RejectionReason.Unreachable);
            return;
        }

        var from = unit.Position;
        unit.Position = destination;
        unit.Gauge = 0; // l'unité a agi : sa jauge n'est plus pleine, même si elle repart à 100 %
        _events.Add(new UnitMoved(CurrentTick, unit.Id, from, path));
        SetGauge(unit, Rules.MoveOnlyRestartGauge);
    }

    /// <summary>Change la jauge et annonce l'unité prête si elle vient de se remplir.</summary>
    private void SetGauge(Unit unit, int gauge)
    {
        var wasReady = unit.IsReady;
        unit.Gauge = gauge;
        if (!wasReady && unit.IsReady)
            _events.Add(new UnitReady(CurrentTick, unit.Id));
    }

    private Occupant OccupantFor(Unit mover, Cell cell)
    {
        var other = UnitAt(cell);
        if (other is null || other == mover)
            return Occupant.None;
        return other.Team == mover.Team ? Occupant.Ally : Occupant.Enemy;
    }

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
    }
}
