using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Simulation d'un combat, à ticks fixes et entièrement déterministe.
/// <para>
/// Déroulement d'un tick : les états arrivés à échéance prennent fin ; les attaques chargées arrivées à
/// échéance se déclenchent ; les commandes des joueurs soumises depuis le tick précédent sont appliquées
/// dans leur ordre d'arrivée (la première validée l'emporte en cas de conflit) ; les ordres à l'avance dont
/// l'unité peut agir partent, dans l'ordre où ils ont été donnés ; les unités IA prêtes agissent ; les jauges
/// se remplissent ; les unités IA qui passent le seuil fixent leur intention.
/// </para>
/// <para>
/// L'IA fait partie de la simulation : ses décisions sont déterministes et se rejouent d'elles-mêmes,
/// le journal des commandes ne contient donc que celles des joueurs.
/// </para>
/// <para>
/// La pause n'existe pas ici : c'est la boucle qui appelle <see cref="Tick"/> qui s'arrête.
/// L'état initial (unités, jauges) est connu du client : seuls les changements produisent des événements.
/// </para>
/// </summary>
public sealed partial class CombatSimulation
{
    private readonly List<Unit> _units = new();
    private readonly Queue<CombatCommand> _pending = new();

    // Ordres à l'avance enregistrés, dans l'ordre où ils ont été donnés (un remplacement passe en dernier).
    private readonly List<ActivateCommand> _orders = new();
    private readonly List<CombatEvent> _events = new();
    private readonly List<LoggedCommand> _commandLog = new();
    private readonly DeterministicRandom _random;
    private readonly List<Dome> _domes = new();

    // PV des obstacles bas déjà abîmés (les autres sont intacts, à LowObstacleHp).
    private readonly Dictionary<Cell, int> _coverHp = new();

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
            _units.Add(new Unit(unit, Rules));
        }
    }

    public CombatRules Rules { get; }

    /// <summary>Le terrain du combat (non modifiable hors de la simulation).</summary>
    public Grid Grid => _grid;

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

    /// <summary>
    /// Équipes encore en lice : celles (d'origine : une unité dominée compte pour la sienne) qui ont au moins une unité
    /// debout, sans compter les civils ni les gadgets. Le combat libre se termine quand il n'en reste qu'une.
    /// </summary>
    public IReadOnlySet<int> TeamsStillFighting() =>
        _units.Where(u => u.Status == UnitStatus.Active && !u.IsCivilian && u.SummonedBy is null)
            .Select(u => u.OriginalTeam)
            .ToHashSet();

    /// <summary>Met une commande en attente ; elle sera validée et appliquée au prochain tick.</summary>
    public void Submit(CombatCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not (ActivateCommand or OrderCommand or CancelOrderCommand))
            throw new ArgumentException($"Unknown command type {command.GetType().Name}.", nameof(command));
        _pending.Enqueue(command);
    }

    /// <summary>Joue un tick et renvoie les événements qu'il a produits.</summary>
    public IReadOnlyList<CombatEvent> Tick()
    {
        CurrentTick++;
        var firstEvent = _events.Count;

        ExpireStatuses();
        ReleaseCharges();

        while (_pending.TryDequeue(out var command))
        {
            _commandLog.Add(new LoggedCommand(CurrentTick, command));
            Apply(command);
        }

        ExecuteOrders();

        // Par index : une action pourrait ajouter une unité (invocation) à la liste pendant la boucle.
        for (var i = 0; i < _units.Count; i++)
        {
            var unit = _units[i];
            if (unit.IsReady && ControllerOf(unit) is { } behavior)
                RunAi(unit, behavior);
        }

        foreach (var unit in _units)
        {
            if (unit.Status == UnitStatus.Active && !unit.IsGaugeFull)
                SetGauge(unit, Math.Min(Rules.GaugeCapacity, unit.Gauge + FillAmount(unit)));
        }

        UpdateIntentions();

        return _events.GetRange(firstEvent, _events.Count - firstEvent);
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

    /// <summary>
    /// Empreinte de l'état (tick, unités, générateur aléatoire, nombre d'événements et d'ordres) : deux simulations qui
    /// devraient être identiques (serveur et client) le sont si leurs empreintes concordent à chaque tick.
    /// </summary>
    public ulong Checksum()
    {
        var hash = 14695981039346656037UL; // FNV-1a, stable d'une machine et d'une exécution à l'autre
        void Add(long value)
        {
            hash ^= (ulong)value;
            hash *= 1099511628211UL;
        }

        Add(CurrentTick);
        Add((long)_random.State);
        Add(_events.Count);
        Add(_orders.Count);
        foreach (var unit in _units)
        {
            Add(unit.Id.Value);
            Add(unit.Position.X);
            Add(unit.Position.Y);
            Add(unit.Gauge);
            Add(unit.Hp);
            Add((int)unit.Status);
            Add(unit.Team);
        }
        return hash;
    }

    /// <summary>
    /// Points de jauge gagnés ce tick : seul endroit où se décide la vitesse de la jauge. Étourdie ou en
    /// train de charger : rien. Sinon l'initiative, multipliée par les pourcentages du déséquilibre et de
    /// l'accélération (qui se combinent), avec report du reste pour être exact en moyenne.
    /// </summary>
    private int FillAmount(Unit unit)
    {
        if (unit.HasStatus(StatusKind.Stunned) || unit.Charge is not null)
            return 0;

        // Gain exact = initiative × pourcentages / échelle ; le reste de la division est reporté au tick suivant.
        long numerator = unit.Initiative, scale = 1;
        if (unit.HasStatus(StatusKind.OffBalance))
        {
            numerator *= Rules.OffBalanceFillPercent;
            scale *= 100;
        }
        if (unit.HasStatus(StatusKind.Hasted))
        {
            numerator *= Rules.HastedFillPercent;
            scale *= 100;
        }

        // Quand les états changent, l'échelle change : le reste est converti, pas perdu.
        if (unit.FillRemainderScale != scale)
        {
            unit.FillRemainder = (int)(unit.FillRemainder * scale / unit.FillRemainderScale);
            unit.FillRemainderScale = (int)scale;
        }
        var total = numerator + unit.FillRemainder;
        unit.FillRemainder = (int)(total % scale);
        return (int)(total / scale);
    }

    /// <summary>
    /// Remplit d'un coup la jauge (plafonnée à la capacité) ; l'unité peut devenir prête aussitôt.
    /// Sans effet sur une unité qui charge (sa jauge reste à 0 jusqu'au déclenchement).
    /// </summary>
    private void GainGauge(Unit unit, int amount)
    {
        var gained = Math.Min(amount, Rules.GaugeCapacity - unit.Gauge);
        if (gained <= 0 || unit.Charge is not null)
            return;
        _events.Add(new GaugeGained(CurrentTick, unit.Id, gained, unit.Gauge + gained));
        SetGauge(unit, unit.Gauge + gained);
    }

    /// <summary>Change la jauge et annonce l'unité prête si elle vient de se remplir.</summary>
    private void SetGauge(Unit unit, int gauge)
    {
        var wasReady = unit.IsReady;
        unit.Gauge = gauge;
        if (!wasReady && unit.IsReady)
            _events.Add(new UnitReady(CurrentTick, unit.Id));
    }

    /// <summary>Définition de l'unité, compatibilité avec les règles, placement.</summary>
    private void Validate(UnitSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(setup.Definition);
        setup.Definition.Validate();
        setup.Definition.ValidateAgainst(Rules);
        if (FindUnit(setup.Id) is not null)
            throw new ArgumentException($"Duplicate unit id {setup.Id}.");
        if (_grid.IsBlocked(setup.Position))
            throw new ArgumentException($"Unit {setup.Id} is on a blocked cell {setup.Position}.");
        if (UnitAt(setup.Position) is not null)
            throw new ArgumentException($"Unit {setup.Id} shares cell {setup.Position} with another unit.");
        if (setup.InitialGauge < 0 || setup.InitialGauge > Rules.GaugeCapacity)
            throw new ArgumentException($"Unit {setup.Id} has an initial gauge outside [0, {Rules.GaugeCapacity}].");
        if (setup.Points is { } points)
        {
            points.Validate();
            // Même borne que pour l'initiative de base (UnitDefinition.ValidateAgainst), points de stats compris.
            if (Rules.InitiativeWith(setup.Definition.Initiative, points) > Rules.GaugeCapacity)
                throw new ArgumentException($"Unit {setup.Id} has an initiative above the gauge capacity ({Rules.GaugeCapacity}).");
        }
    }
}
