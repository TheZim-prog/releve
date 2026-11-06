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
public sealed class CombatSimulation
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
            _units.Add(new Unit(unit, Rules.GaugeCapacity));
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

    /// <summary>Met une commande en attente ; elle sera validée et appliquée au prochain tick.</summary>
    public void Submit(CombatCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not (ActivateCommand or OrderCommand or CancelOrderCommand))
            throw new ArgumentException($"Unknown command type {command.GetType().Name}.", nameof(command));
        _pending.Enqueue(command);
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
    /// Ordres à l'avance tels que le joueur les a demandés : ceux enregistrés, plus les ordres et annulations soumis
    /// mais pas encore appliqués (pendant la pause, par exemple). Pour l'affichage ; rien n'est changé.
    /// </summary>
    public IReadOnlyList<ActivateCommand> PlannedOrders()
    {
        var orders = new List<ActivateCommand>(_orders);
        foreach (var command in _pending)
        {
            if (command is not (OrderCommand or CancelOrderCommand))
                continue;
            orders.RemoveAll(o => o.Unit == command.Unit);
            if (command is OrderCommand order)
                orders.Add(order.Activation);
        }
        return orders;
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
    /// Une unité IA prête exécute son intention. Si elle est absente ou devenue impossible (cible vaincue,
    /// compétence en recharge, cible hors d'atteinte), l'unité en choisit une nouvelle ; si celle-ci non plus
    /// ne peut pas être exécutée maintenant, l'unité se rapproche de sa cible, ou attend jauge pleine.
    /// </summary>
    private void RunAi(Unit unit, IUnitBehavior behavior)
    {
        // Une unité en fuite déjà au bord quitte le combat au lieu d'agir.
        if (behavior == Behaviors.FleeInstance && _grid.IsEdge(unit.Position))
        {
            Escape(unit);
            return;
        }

        var command = unit.Intention is { } intention ? behavior.Execute(this, unit, intention) : null;
        if (command is null)
        {
            SetIntention(unit, behavior.ChooseIntention(this, unit));
            if (unit.Intention is { } fresh)
                command = behavior.Execute(this, unit, fresh) ?? behavior.Approach(this, unit, fresh);
        }
        if (command is null)
            return;

        // Même validation que pour un joueur : le comportement ne contourne aucune règle.
        var (plan, reason) = ValidateActivation(command);
        if (plan is null)
            throw new InvalidOperationException($"{behavior.GetType().Name} produced an invalid command ({reason}) for unit {unit.Id}: {command}.");
        ApplyActivation(plan);

        // En fuite (déjà, ou depuis une attaque d'opportunité en chemin) et arrivée au bord : elle s'enfuit aussitôt.
        if (unit.Status == UnitStatus.Active && ControllerOf(unit) == Behaviors.FleeInstance && _grid.IsEdge(unit.Position))
            Escape(unit);
    }

    /// <summary>
    /// Les unités IA dont la jauge passe le seuil fixent leur intention (une fois par cycle). Une intention dont
    /// la cible est tombée est remplacée aussitôt ; « hors d'atteinte » ne se juge qu'à jauge pleine (RunAi).
    /// </summary>
    private void UpdateIntentions()
    {
        var threshold = Rules.GaugeAt(Rules.IntentionThresholdPercent);
        foreach (var unit in _units)
        {
            if (unit.Status != UnitStatus.Active || unit.Charge is not null || ControllerOf(unit) is not { } behavior)
                continue;

            var needsIntention = unit.Intention is null
                ? unit.Gauge >= threshold
                : FindUnit(unit.Intention.Target) is not { Status: UnitStatus.Active };
            if (needsIntention)
                SetIntention(unit, behavior.ChooseIntention(this, unit));
        }
    }

    /// <summary>
    /// Qui commande l'unité : <c>null</c> pour un joueur, sinon le comportement d'IA. Une unité confuse agit seule
    /// (elle attaque l'unité la plus proche) ; une unité dominée est commandée comme celle qui la domine (par les
    /// joueurs, ou par l'IA de base) ; une unité en fuite ne fait que courir ; sinon, son propre comportement.
    /// </summary>
    internal IUnitBehavior? ControllerOf(Unit unit)
    {
        // Un civil ne fait que fuir, même confus (il n'a rien pour attaquer).
        if (unit.IsCivilian)
            return Behaviors.FleeInstance;
        if (unit.HasStatus(StatusKind.Confused))
            return Behaviors.ConfusedInstance;
        if (unit.DominatedBy is null && unit.IsFleeing)
            return Behaviors.FleeInstance;
        var behavior = unit.DominatedBy is { } dominator ? FindUnit(dominator)?.Behavior : unit.Behavior;
        return behavior is null ? null : Behaviors.Find(behavior);
    }

    private void SetIntention(Unit unit, Intention? intention)
    {
        if (intention == unit.Intention)
            return;
        unit.Intention = intention;
        _events.Add(new IntentionChosen(CurrentTick, unit.Id, intention));
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
        _events.Add(new StatusEnded(CurrentTick, unit.Id, status));

        // Fin d'une confusion ou d'une domination : l'unité retrouve son camp et ses propres décisions ; ce qu'elle
        // avait lancé sous contrôle (intention, charge) est perdu.
        if (status is StatusKind.Confused or StatusKind.Dominated)
        {
            SetIntention(unit, null);
            InterruptCharge(unit);
        }
        if (status == StatusKind.Dominated)
        {
            unit.DominatedBy = null;
            ChangeTeam(unit, unit.OriginalTeam);
        }

        if (!wasReady && unit.IsReady)
            _events.Add(new UnitReady(CurrentTick, unit.Id));
    }

    private void ChangeTeam(Unit unit, int team)
    {
        if (unit.Team == team)
            return;
        unit.Team = team;
        _events.Add(new TeamChanged(CurrentTick, unit.Id, team));
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
            case OrderCommand order:
                GiveOrder(order);
                break;
            case CancelOrderCommand cancel:
                if (_orders.RemoveAll(o => o.Unit == cancel.Unit) > 0)
                    _events.Add(new OrderCancelled(CurrentTick, cancel.Unit));
                break;
            default:
                throw new ArgumentException($"Unknown command type {command.GetType().Name}.", nameof(command));
        }
    }

    private void ApplyActivate(ActivateCommand command)
    {
        var (plan, reason) = ValidatePlayerCommand(command);
        if (plan is null)
            Reject(command, reason!.Value);
        else
            ApplyActivation(plan);
    }

    /// <summary>Enregistre un ordre à l'avance, sauf s'il est impossible pour une autre raison que le temps.</summary>
    private void GiveOrder(OrderCommand order)
    {
        if (ValidatePlayerCommand(order.Activation, ignoreTiming: true).Reason is { } reason)
        {
            Reject(order, reason);
            return;
        }
        _orders.RemoveAll(o => o.Unit == order.Unit);
        _orders.Add(order.Activation);
        _events.Add(new OrderGiven(CurrentTick, order.Unit, order.Activation));
    }

    /// <summary>
    /// Les ordres à l'avance dont l'unité peut agir partent, dans l'ordre où ils ont été donnés (le premier validé
    /// l'emporte). Ceux qui n'attendent que du temps restent ; ceux devenus impossibles sont retirés et refusés.
    /// </summary>
    private void ExecuteOrders()
    {
        if (_orders.Count == 0)
            return;
        foreach (var order in _orders.ToList())
        {
            var (plan, reason) = ValidatePlayerCommand(order);
            if (reason is { } waiting && waiting.OnlyNeedsTime())
                continue;
            _orders.Remove(order);
            if (plan is null)
            {
                Reject(order, reason!.Value);
                continue;
            }
            _events.Add(new OrderExecuted(CurrentTick, order.Unit));
            ApplyActivation(plan);
        }
    }

    /// <summary>Commande d'un joueur : refusée pour une unité commandée par l'IA, sinon validée comme toute activation.</summary>
    private (ActivationPlan? Plan, RejectionReason? Reason) ValidatePlayerCommand(ActivateCommand command, bool ignoreTiming = false) =>
        FindUnit(command.Unit) is { } commanded && ControllerOf(commanded) is not null
            ? (null, RejectionReason.NotControllable)
            : ValidateActivation(command, ignoreTiming);

    /// <summary>L'unité obéit-elle aux joueurs (ni IA, ni confuse, ni dominée par l'IA, ni civil) ?</summary>
    public bool IsPlayerControlled(UnitId unit) => FindUnit(unit) is { } found && ControllerOf(found) is null;

    /// <summary>
    /// Prévisualisation : la commande d'un joueur serait-elle acceptée dans l'état actuel ? <c>null</c> si oui, sinon la
    /// raison du refus. Rien n'est changé. (Le prochain tick peut encore changer la donne : fin d'un état ou d'une
    /// recharge, commande d'un autre joueur validée avant.)
    /// </summary>
    /// <param name="ignoreTiming">
    /// Pour un ordre donné à l'avance : ne pas refuser parce que l'unité ne peut pas agir <em>maintenant</em> (raisons
    /// où il suffit d'attendre, cf. <see cref="RejectionReasons.OnlyNeedsTime"/>) ; tout le reste est vérifié.
    /// </param>
    public RejectionReason? Check(ActivateCommand command, bool ignoreTiming = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ValidatePlayerCommand(command, ignoreTiming).Reason;
    }

    /// <summary>Cases où l'unité peut aller par un déplacement (sa case comprise), avec leur distance à pied.</summary>
    public IReadOnlyDictionary<Cell, int> ReachableCells(UnitId unitId) =>
        FindUnit(unitId) is { } unit
            ? Pathfinder.ReachableCells(_grid, unit.Position, unit.Mobility, cell => OccupantFor(unit, cell))
            : new Dictionary<Cell, int>();

    /// <summary>Chemin qu'emprunterait un déplacement de l'unité vers <paramref name="destination"/>, ou <c>null</c>.</summary>
    public IReadOnlyList<Cell>? PathFor(UnitId unitId, Cell destination) =>
        FindUnit(unitId) is { } unit ? PathFor(unit, destination) : null;

    private IReadOnlyList<Cell>? PathFor(Unit unit, Cell destination) =>
        Pathfinder.FindPath(_grid, unit.Position, destination, unit.Mobility, cell => OccupantFor(unit, cell));

    /// <summary>
    /// Équipes encore en lice : celles (d'origine : une unité dominée compte pour la sienne) qui ont au moins une unité
    /// debout, sans compter les civils ni les gadgets. Le combat libre se termine quand il n'en reste qu'une.
    /// </summary>
    public IReadOnlySet<int> TeamsStillFighting() =>
        _units.Where(u => u.Status == UnitStatus.Active && !u.IsCivilian && u.SummonedBy is null)
            .Select(u => u.OriginalTeam)
            .ToHashSet();

    /// <summary>
    /// Prévisualisation d'une action de <paramref name="unitId"/> lancée depuis <paramref name="from"/> (sa case, ou celle
    /// du fantôme) : ce qu'elle viserait (unité, cases de la zone ou de la course, case d'arrivée ou de dépôt), ou la
    /// raison du refus. Ne regarde ni la jauge ni la recharge (voir <see cref="Check"/> pour la commande entière).
    /// </summary>
    public ActionPreview PreviewAction(UnitId unitId, Cell from, SkillUse use)
    {
        ArgumentNullException.ThrowIfNull(use);
        if (FindUnit(unitId) is not { } unit)
            return new ActionPreview(RejectionReason.UnknownUnit);
        if (unit.FindSkill(use.SkillId) is not { } skill)
            return new ActionPreview(RejectionReason.UnknownSkill);
        var (action, reason) = PlanAction(unit, from, skill, use);
        return action is null
            ? new ActionPreview(reason)
            : new ActionPreview(null, action.Target?.Id, action.Zone, action.Destination);
    }

    /// <summary>Activation validée, prête à être appliquée telle quelle.</summary>
    internal sealed record ActivationPlan(Unit Unit, IReadOnlyList<Cell>? Path, PlannedAction? Action,
        PlannedInteraction? Interaction = null);

    /// <summary>
    /// Validation complète d'une activation, sans rien changer : la commande est atomique, rien n'est appliqué
    /// si une partie échoue. Sert aux commandes des joueurs comme à l'IA.
    /// </summary>
    internal (ActivationPlan? Plan, RejectionReason? Reason) ValidateActivation(ActivateCommand command, bool ignoreTiming = false)
    {
        var unit = FindUnit(command.Unit);
        if (unit is null)
            return (null, RejectionReason.UnknownUnit);
        if (unit.Status != UnitStatus.Active)
            return (null, RejectionReason.Incapacitated);
        if (!ignoreTiming && unit.HasStatus(StatusKind.Stunned))
            return (null, RejectionReason.Stunned);
        if (!ignoreTiming && unit.Charge is not null)
            return (null, RejectionReason.Charging);
        if (!ignoreTiming && !unit.IsReady)
            return (null, RejectionReason.NotReady);
        if (command.MoveTo is null && command.Skill is null && command.Interaction is null)
            return (null, RejectionReason.Empty);
        if (command.Skill is not null && command.Interaction is not null)
            return (null, RejectionReason.TwoActions);

        var position = unit.Position;
        IReadOnlyList<Cell>? path = null;
        if (command.MoveTo is { } destination)
        {
            if (destination == unit.Position)
                return (null, RejectionReason.InvalidDestination);
            path = PathFor(unit, destination);
            if (path is null)
                return (null, RejectionReason.Unreachable);
            position = destination;
        }

        PlannedAction? action = null;
        if (command.Skill is { } use)
        {
            var skill = unit.FindSkill(use.SkillId);
            if (skill is null)
                return (null, RejectionReason.UnknownSkill);
            if (!ignoreTiming && CurrentTick < unit.CooldownEndTick(skill.Id))
                return (null, RejectionReason.OnCooldown);
            var (planned, rejection) = PlanAction(unit, position, skill, use);
            if (rejection is not null)
                return (null, rejection);
            action = planned;
        }

        PlannedInteraction? interaction = null;
        if (command.Interaction is { } interact)
        {
            var (planned, rejection) = PlanInteraction(unit, position, interact);
            if (rejection is not null)
                return (null, rejection);
            interaction = planned;
        }

        return (new ActivationPlan(unit, path, action, interaction), null);
    }

    /// <summary>Interaction validée : son type et l'unité au contact visée.</summary>
    internal sealed record PlannedInteraction(InteractionKind Kind, Unit Target);

    /// <summary>
    /// Validation d'une interaction depuis <paramref name="position"/> (case d'arrivée). Relever : un allié à terre,
    /// au contact (sans coupe d'angle par un mur, comme le corps à corps).
    /// </summary>
    private (PlannedInteraction? Interaction, RejectionReason? Reason) PlanInteraction(Unit unit, Cell position, InteractionUse use)
    {
        switch (use.Kind)
        {
            case InteractionKind.Revive:
                if (FindUnit(use.Target) is not { Status: UnitStatus.Downed } target || target.Team != unit.Team)
                    return (null, RejectionReason.InvalidTarget);
                if (!IsInMeleeReach(position, target.Position))
                    return (null, RejectionReason.OutOfRange);
                return (new PlannedInteraction(use.Kind, target), null);
            default: // valeur inconnue envoyée par un client : refusée, pas une erreur du serveur
                return (null, RejectionReason.InvalidTarget);
        }
    }

    /// <summary>
    /// Relève un allié à terre : debout avec une part de ses PV max (au moins 1), jauge vide (sans reste de
    /// remplissage d'avant sa chute). Ses attaques d'opportunité se rechargeront quand il rejouera. Sa chute est
    /// terminée : la marque « sauvé » (qui vaut pour une chute) est effacée.
    /// </summary>
    private void Revive(Unit unit, Unit target)
    {
        target.Status = UnitStatus.Active;
        target.Hp = Math.Max(1, (int)((long)target.MaxHp * Rules.ReviveHpPercent / 100));
        target.WasRescued = false;
        target.FillRemainder = 0;
        target.FillRemainderScale = 1;
        SetGauge(target, 0);
        _events.Add(new UnitRevived(CurrentTick, target.Id, unit.Id, target.Hp));
    }

    private void ApplyActivation(ActivationPlan plan)
    {
        ApplyActivationCore(plan);
        DismissIfExhausted(plan.Unit);
    }

    /// <summary>
    /// Unités à activations limitées (ex. balise) : épuisées, elles disparaissent, mais après leur dernière action
    /// (une attaque chargée en cours se déclenche d'abord).
    /// </summary>
    private void DismissIfExhausted(Unit unit)
    {
        if (unit.Definition.MaxActivations is { } max && unit.Activations >= max
            && unit.Status == UnitStatus.Active && unit.Charge is null)
            Dismiss(unit);
    }

    private void ApplyActivationCore(ActivationPlan plan)
    {
        var (unit, path, action, interaction) = plan;
        unit.Intention = null; // l'unité agit : son intention éventuelle est consommée
        RemoveDome(unit); // et son dôme éventuel tombe (un nouveau peut être posé par cette action)
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

        // Interagir (poids en données). Seul un joueur interagit : si l'unité ne lui obéit plus après son trajet
        // (prise de contrôle en chemin), l'interaction est annulée, comme un déplacement seul.
        if (interaction is not null)
        {
            if (ControllerOf(unit) is not null)
            {
                SetGauge(unit, Rules.GaugeAt(Rules.MoveOnlyRestartPercent));
                return;
            }
            switch (interaction.Kind)
            {
                case InteractionKind.Revive:
                    Revive(unit, interaction.Target);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown interaction {interaction.Kind}.");
            }
            SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(Rules.ReviveWeight)));
            return;
        }

        // Passée en fuite en chemin (attaque d'opportunité) : elle ne fait plus que courir, l'action est annulée.
        if (action is null || ControllerOf(unit) == Behaviors.FleeInstance)
        {
            SetGauge(unit, Rules.GaugeAt(Rules.MoveOnlyRestartPercent));
            return;
        }

        var actionSkill = action.Skill;
        unit.Activations++; // un « déclenchement » : une compétence utilisée (pas un déplacement seul, ni une action annulée)
        unit.StartCooldown(actionSkill.Id, CurrentTick + Rules.MillisecondsToTicks(actionSkill.CooldownMs));
        if (actionSkill.IsCharged)
        {
            // Immobilisée, jauge à 0 jusqu'au déclenchement (ReleaseCharges). Les dégâts sont fixés maintenant :
            // c'est l'attaque que le joueur a lancée et vue au sol.
            var charge = new PendingCharge(actionSkill, action.Target?.Id, action.Zone,
                CurrentTick + Rules.MillisecondsToTicks(actionSkill.ChargeMs), DamagePercentOf(unit), action.Origin);
            unit.Charge = charge;
            _events.Add(new ChargeStarted(CurrentTick, unit.Id, actionSkill.Id, charge.Target, charge.Zone, charge.ReleaseTick));
            return;
        }

        Execute(unit, actionSkill, action.Target, action.Zone, DamagePercentOf(unit), action.Destination, action.Origin);
        SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(actionSkill)));
    }

    /// <summary>Multiplicateur des dégâts infligés par l'unité, en % (surpuissant ou non).</summary>
    private int DamagePercentOf(Unit unit) => unit.HasStatus(StatusKind.Empowered) ? Rules.EmpoweredDamagePercent : 100;

    /// <summary>
    /// Action validée : sur une unité (<paramref name="Target"/>) ou sur des cases (<paramref name="Zone"/>) ;
    /// <paramref name="Destination"/> : case où la télékinésie pose sa cible ; <paramref name="Origin"/> : d'où part une
    /// zone ordinaire (centre d'un carré, lanceur pour une ligne), pour la couverture.
    /// </summary>
    internal sealed record PlannedAction(SkillDefinition Skill, Unit? Target, IReadOnlyList<Cell>? Zone, Cell? Destination = null,
        Cell? Origin = null);

    /// <summary>
    /// Sauvetage, depuis <paramref name="position"/> : un allié <b>à terre</b> (ou un civil debout) à <see cref="SkillDefinition.Range"/> pas au
    /// plus, à pied (chemin réel jusqu'à son contact, sans coupe d'angle) ; une case de dépôt libre, autre que celle du
    /// lanceur, au plus à <see cref="SkillDefinition.RescueDropRadius"/> et en vue ; et une case d'arrivée pour le lanceur
    /// (<see cref="RescuerEnd"/>).
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanRescue(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        // Un allié à terre, ou un civil (debout : à 0 PV, il est perdu).
        if (use.Target is not { } allyId || use.Cell is not { } drop || FindUnit(allyId) is not { } ally || ally == unit
            || !(ally.IsCivilian ? ally.Status == UnitStatus.Active : ally.Team == unit.Team && ally.Status == UnitStatus.Downed))
            return (null, RejectionReason.InvalidTarget);

        // Range pas jusqu'à l'allié : Range - 1 jusqu'à une case à son contact, puis le dernier pas sur lui.
        bool ReachesAlly(Cell cell) =>
            cell.IsAdjacentTo(ally.Position) && CanStepIgnoringDestination(unit, cell, ally.Position, downedBlockCorners: true);
        var reaches = ReachesAlly(position)
            || Pathfinder.ReachableCells(_grid, position, skill.Range - 1, cell => OccupantFor(unit, cell)).Keys.Any(ReachesAlly);
        if (!reaches)
            return (null, RejectionReason.OutOfRange);

        if (drop == ally.Position || drop == position || position.DistanceTo(drop) > skill.RescueDropRadius!.Value
            || !IsFreeForRescue(unit, ally, drop) || !HasLineOfSight(unit, position, drop))
            return (null, RejectionReason.InvalidDestination);
        if (RescuerEnd(unit, ally, position, drop, skill.RescueDropRadius.Value) is null)
            return (null, RejectionReason.InvalidDestination);
        return (new PlannedAction(skill, ally, null, drop), null);
    }

    /// <summary>Case libre une fois le sauvetage fait : le lanceur et l'allié la quittent.</summary>
    private bool IsFreeForRescue(Unit rescuer, Unit ally, Cell cell) =>
        _grid.IsWalkable(cell) && (UnitAt(cell) is not { } occupant || occupant == rescuer || occupant == ally);

    /// <summary>
    /// Où finit le lanceur d'un sauvetage : à côté du dépôt, sur sa case s'il y est déjà, sinon sur la première case
    /// voisine du dépôt (ordre fixe) qui est libre, dans le rayon et en vue depuis <paramref name="position"/>, et
    /// accessible depuis le dépôt sans coupe d'angle.
    /// </summary>
    private Cell? RescuerEnd(Unit rescuer, Unit ally, Cell position, Cell drop, int radius)
    {
        if (position.IsAdjacentTo(drop) && CanStepIgnoringDestination(rescuer, drop, position, downedBlockCorners: true))
            return position;
        return _grid.Neighbors(drop)
            .Where(c => c != drop && IsFreeForRescue(rescuer, ally, c) && position.DistanceTo(c) <= radius
                        && HasLineOfSight(rescuer, position, c)
                        && CanStepIgnoringDestination(rescuer, drop, c, downedBlockCorners: true))
            .Select(c => (Cell?)c)
            .FirstOrDefault();
    }

    /// <summary>Sauvetage (sans attaque d'opportunité) : l'allié est posé sur le dépôt, puis le lanceur à côté.</summary>
    private void Rescue(Unit rescuer, SkillDefinition skill, Unit ally, Cell drop)
    {
        var end = RescuerEnd(rescuer, ally, rescuer.Position, drop, skill.RescueDropRadius!.Value)!.Value;
        _events.Add(new SkillUsed(CurrentTick, rescuer.Id, skill.Id, ally.Id));
        _events.Add(new UnitRescued(CurrentTick, ally.Id, rescuer.Id));
        if (!ally.IsCivilian) // la marque compte pour la règle de campagne des chutes des héros
            ally.WasRescued = true;
        Displace(ally, drop);
        if (rescuer.Position != end)
            Displace(rescuer, end);
    }

    /// <summary>
    /// Mouvements spéciaux, depuis <paramref name="position"/>. <see cref="PlannedAction.Destination"/> : où finit le
    /// lanceur. Percée : <see cref="PlannedAction.Zone"/> = la course (arrivée comprise). Saut : la zone autour de
    /// l'atterrissage. Charge : <see cref="PlannedAction.Target"/> = l'unité percutée.
    /// </summary>
    private (PlannedAction? Action, RejectionReason? Reason) PlanSelfMove(
        Unit unit, Cell position, SkillDefinition skill, SkillUse use, SelfMoveType selfMove)
    {
        switch (selfMove)
        {
            case SelfMoveType.Pierce:
            {
                if (use.Cell is not { } aim || use.Target is not null || position.DirectionTo(aim) is not { } step)
                    return (null, RejectionReason.InvalidTarget);
                // Course à travers les unités (les civils traversés ne sont pas touchés), arrêtée par les murs et les
                // obstacles bas (sans coupe d'angle par le décor).
                var course = new List<Cell>();
                var previous = position;
                for (var i = 0; i < skill.Range; i++)
                {
                    var next = new Cell(previous.X + step.X, previous.Y + step.Y);
                    if (!_grid.CanStep(previous, next))
                        break;
                    course.Add(next);
                    previous = next;
                }
                // Arrivée : la dernière case libre de la course.
                var end = course.FindLastIndex(c => UnitAt(c) is not { } occupant || occupant == unit);
                if (end < 0)
                    return (null, RejectionReason.Unreachable);
                return (new PlannedAction(skill, null, course.GetRange(0, end + 1), course[end]), null);
            }

            case SelfMoveType.Leap:
            {
                if (use.Cell is not { } landing || use.Target is not null)
                    return (null, RejectionReason.InvalidTarget);
                if (!IsWithinRange(position, landing, skill.Range))
                    return (null, RejectionReason.OutOfRange);
                if (!_grid.IsWalkable(landing) || (UnitAt(landing) is { } occupant && occupant != unit))
                    return (null, RejectionReason.InvalidDestination);
                // Par-dessus les unités et les obstacles bas, pas les murs.
                if (!LineOfSight.IsClear(_grid, position, landing))
                    return (null, RejectionReason.NoLineOfSight);
                var zone = skill.Zone!.Cells(_grid, landing, landing);
                return zone is null
                    ? (null, RejectionReason.InvalidTarget)
                    : (new PlannedAction(skill, null, zone, landing), null);
            }

            case SelfMoveType.Rush:
            {
                if (use.Target is not { } targetId || use.Cell is not null || FindUnit(targetId) is not { } target
                    || target.Status != UnitStatus.Active || !IsRightSide(unit, target, skill)
                    || position.DirectionTo(target.Position) is not { } step)
                    return (null, RejectionReason.InvalidTarget);
                if (!IsWithinRange(position, target.Position, skill.Range))
                    return (null, RejectionReason.OutOfRange);

                // Fonce jusqu'à la première unité debout de la ligne, par-dessus les unités à terre et les civils ; le
                // décor (ou un coin bouché) l'arrête sans qu'il touche personne. Il finit sur la dernière case libre de
                // sa course ; si elle n'est pas au contact de l'unité (une case occupée juste devant), il ne la frappe pas.
                Unit? struck = null;
                var lastFree = position;
                var previous = position;
                for (var i = 0; i < skill.Range; i++)
                {
                    var next = new Cell(previous.X + step.X, previous.Y + step.Y);
                    if (!CanStepIgnoringDestination(unit, previous, next, downedBlockCorners: false))
                        break;
                    if (UnitAt(next) is { } other && other != unit)
                    {
                        if (other.Status == UnitStatus.Active && !other.IsCivilian)
                        {
                            if (lastFree == previous)
                                struck = other;
                            break;
                        }
                    }
                    else
                    {
                        lastFree = next;
                    }
                    previous = next;
                }
                return (new PlannedAction(skill, struck, null, lastFree), null);
            }

            default:
                throw new InvalidOperationException($"Unknown self move {selfMove}.");
        }
    }

    /// <summary>
    /// Pas de <paramref name="from"/> à <paramref name="to"/> pour <paramref name="unit"/>, sans tenir compte de qui
    /// occupe <paramref name="to"/> (on peut s'y écraser) : le décor (coupe d'angle comprise) et, en diagonale, les
    /// ennemis de l'unité sur les coins (à terre compris, sauf si <paramref name="downedBlockCorners"/> est faux).
    /// </summary>
    private bool CanStepIgnoringDestination(Unit unit, Cell from, Cell to, bool downedBlockCorners)
    {
        if (!_grid.CanStep(from, to))
            return false;
        if (!from.IsDiagonalTo(to))
            return true;
        bool Blocks(Cell corner) =>
            UnitAt(corner) is { } other && AreOpponents(other, unit) && (downedBlockCorners || other.Status == UnitStatus.Active);
        return !Blocks(new Cell(from.X, to.Y)) && !Blocks(new Cell(to.X, from.Y));
    }

    /// <summary>
    /// Valide la cible d'une compétence utilisée depuis <paramref name="position"/> (case d'arrivée du lanceur).
    /// Compétence à zone : une case, à portée (0 = sur soi), libre et en vue pour un carré, alignée pour une ligne,
    /// et une zone non vide. Attaque ciblée : voir <see cref="CheckTarget"/>.
    /// </summary>
    internal (PlannedAction? Action, RejectionReason? Reason) PlanAction(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
        if (skill.SelfMove is { } selfMove)
            return PlanSelfMove(unit, position, skill, use, selfMove);

        if (skill.IsRescue)
            return PlanRescue(unit, position, skill, use);

        if (skill.Summon is not null)
        {
            // Une case libre (une fois le lanceur arrivé), à portée et en vue.
            if (use.Cell is not { } spot || use.Target is not null)
                return (null, RejectionReason.InvalidTarget);
            if (!IsWithinRange(position, spot, skill.Range))
                return (null, RejectionReason.OutOfRange);
            // Libre, ou occupée par le lanceur (il en part) ou par son ancien gadget du même type (il sera remplacé).
            if (!_grid.IsWalkable(spot)
                || (UnitAt(spot) is { } occupant && occupant != unit
                    && !(occupant.SummonedBy == unit.Id && occupant.Definition.Id == skill.Summon.Id)))
                return (null, RejectionReason.InvalidDestination);
            if (!HasLineOfSight(unit, position, spot))
                return (null, RejectionReason.NoLineOfSight);
            return (new PlannedAction(skill, null, null, spot), null);
        }

        if (skill.Zone is { } zone)
        {
            if (use.Cell is not { } cell || use.Target is not null)
                return (null, RejectionReason.InvalidTarget);
            if (position.DistanceTo(cell) > skill.Range)
                return (null, RejectionReason.OutOfRange);
            var cells = zone.Cells(_grid, position, cell);
            if (cells is null || cells.Count == 0)
                return (null, RejectionReason.InvalidTarget);
            if (zone.Shape == ZoneShape.Square && cell != position && !HasLineOfSight(unit, position, cell))
                return (null, RejectionReason.NoLineOfSight);
            return (new PlannedAction(skill, null, cells, Origin: zone.Shape == ZoneShape.Square ? cell : position), null);
        }

        if (skill.Target == SkillTargeting.Self)
        {
            // Pas de cible à donner (ou soi-même).
            if (use.Cell is not null || (use.Target is { } self && self != unit.Id))
                return (null, RejectionReason.InvalidTarget);
            return (new PlannedAction(skill, unit, null), null);
        }

        // Une unité, plus une case de destination pour une télékinésie (et seulement pour elle).
        var places = skill.ForcedMove?.Type == ForcedMoveType.Place;
        if (use.Target is not { } targetId || (use.Cell is not null) != places)
            return (null, RejectionReason.InvalidTarget);
        var target = FindUnit(targetId);
        if (target is null || !IsRightSide(unit, target, skill))
            return (null, RejectionReason.InvalidTarget);
        // Remplir une jauge déjà pleine, ou bloquée à 0 par une charge, ne servirait à rien.
        if (!skill.IsOffensive && !HasSupportEffect(skill, target))
            return (null, RejectionReason.NoEffect);
        if (IsImmune(skill, target))
            return (null, RejectionReason.NoEffect);
        // On ne domine pas un allié (une unité confuse pourrait le viser).
        if (target.Team == unit.Team && skill.Effects.Any(e => e.Status == StatusKind.Dominated))
            return (null, RejectionReason.InvalidTarget);
        if (CheckTarget(unit, position, target, skill) is { } reason)
            return (null, reason);
        if (places && !CanPlace(unit, position, target, use.Cell!.Value, skill.ForcedMove!.Distance))
            return (null, RejectionReason.InvalidDestination);
        return (new PlannedAction(skill, target, null, use.Cell), null);
    }

    /// <summary>
    /// Immunités : les robots aux pouvoirs mentaux, les boss à la domination. Une cible directe immunisée est refusée ;
    /// dans une zone, elle résiste. (Un civil ne peut pas être visé, et une domination ne se fait jamais en zone.)
    /// </summary>
    private static bool IsImmune(SkillDefinition skill, Unit target) =>
        (skill.IsMental && target.Definition.IsRobot)
        || (target.Definition.IsBoss && skill.Effects.Any(e => e.Status == StatusKind.Dominated));

    /// <summary>
    /// Une compétence de soutien aurait-elle un effet sur cette cible ? Un état ou un déplacement en a toujours ;
    /// un soin, si la cible est blessée ; un gain de jauge, si sa jauge n'est ni pleine ni bloquée par une charge.
    /// </summary>
    private static bool HasSupportEffect(SkillDefinition skill, Unit target) =>
        skill.Effects.Count > 0
        || skill.ForcedMove is not null
        || (skill.Heals && target.Hp < target.MaxHp)
        || (skill.GaugeGainPercent > 0 && !target.IsGaugeFull && target.Charge is null);

    /// <summary>
    /// Une attaque vise un ennemi ; une compétence de soutien, un allié autre que le lanceur ; une télékinésie,
    /// n'importe quelle unité sauf le lanceur. Personne ne vise un civil directement (le sauvetage a sa propre règle).
    /// </summary>
    private static bool IsRightSide(Unit caster, Unit target, SkillDefinition skill) => !target.IsCivilian && skill.Target switch
    {
        // Une unité confuse frappe n'importe qui (sauf elle-même).
        SkillTargeting.Enemy => target.Team != caster.Team || (caster.HasStatus(StatusKind.Confused) && target != caster),
        SkillTargeting.Ally => target != caster && target.Team == caster.Team,
        SkillTargeting.Any => target != caster,
        _ => throw new InvalidOperationException($"Unknown target kind {skill.Target}."),
    };

    /// <summary>
    /// Télékinésie : case autre que celle de la cible, à <paramref name="distance"/> au plus d'elle, vue depuis elle
    /// (ce qui exclut aussi les murs), et libre une fois le lanceur arrivé en <paramref name="casterPosition"/>
    /// (il a pu quitter sa case, ou arriver sur celle visée, dans la même commande).
    /// </summary>
    private bool CanPlace(Unit caster, Cell casterPosition, Unit target, Cell destination, int distance) =>
        destination != target.Position
        && destination != casterPosition
        && target.Position.DistanceTo(destination) <= distance
        && (UnitAt(destination) is null || UnitAt(destination) == caster)
        && LineOfSight.IsClear(_grid, target.Position, destination);

    /// <summary>
    /// Une attaque ciblée de <paramref name="unit"/>, depuis <paramref name="from"/>, peut-elle toucher
    /// <paramref name="target"/> : debout, à portée et en vue ? Vérifié au lancement et, pour une attaque chargée,
    /// de nouveau au déclenchement.
    /// </summary>
    private RejectionReason? CheckTarget(Unit unit, Cell from, Unit target, SkillDefinition skill)
    {
        if (target.Status != UnitStatus.Active)
            return RejectionReason.InvalidTarget;
        if (!IsWithinRange(from, target.Position, skill.Range))
            return RejectionReason.OutOfRange;
        if (!HasLineOfSight(unit, from, target.Position))
            return RejectionReason.NoLineOfSight;
        return null;
    }

    /// <summary>
    /// La compétence prend effet : sur sa cible, ou sur les unités debout de la zone (dans l'ordre des unités ;
    /// un coup ne touche que sa cible, donc la liste ne change pas en cours de route).
    /// <paramref name="damagePercent"/> : multiplicateur de dégâts du lanceur (voir <see cref="DamagePercentOf"/>).
    /// <paramref name="destination"/> : case où une télékinésie pose sa cible ; <paramref name="zoneOrigin"/> : d'où part
    /// une zone ordinaire (pour la couverture ; une attaque ciblée part du lanceur).
    /// </summary>
    private void Execute(Unit caster, SkillDefinition skill, Unit? target, IReadOnlyList<Cell>? zone, int damagePercent,
        Cell? destination = null, Cell? zoneOrigin = null)
    {
        // Une compétence exécutée est une « source » : un dôme bloque toute la source, pour tous ses protégés.
        if (skill.SelfMove is { } selfMove)
        {
            ExecuteSelfMove(caster, skill, selfMove, target, zone!, destination!.Value, damagePercent);
        }
        else if (skill.IsRescue)
        {
            Rescue(caster, skill, target!, destination!.Value);
        }
        else if (skill.Summon is not null)
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null));
            Summon(caster, skill.Summon, destination!.Value);
        }
        else if (zone is null)
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, target!.Id));
            var landed = ResolveHit(caster, skill, target, damagePercent, origin: caster.Position);
            if (landed && target.Status == UnitStatus.Active && skill.ForcedMove is { } move)
            {
                if (move.Type == ForcedMoveType.Push)
                    Push(caster, skill, target, move, damagePercent);
                else
                    Displace(target, destination!.Value);
            }
        }
        else
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, zone));
            if (skill.CreatesDome)
            {
                RaiseDome(caster, zone);
            }
            else
            {
                foreach (var unit in UnitsAffectedBy(caster, skill, zone))
                    ResolveHit(caster, skill, unit, damagePercent, origin: zoneOrigin);
            }
        }
        EndSource();
    }

    /// <summary>
    /// Mouvement spécial planifié par <see cref="PlanSelfMove"/> (sans attaque d'opportunité) :
    /// Percée = course puis coups sur tout ce qui a été traversé ; Saut = atterrissage puis zone ;
    /// Charge = course jusqu'à l'unité percutée, puis le coup (aucun si le décor l'a arrêtée).
    /// </summary>
    private void ExecuteSelfMove(Unit caster, SkillDefinition skill, SelfMoveType selfMove, Unit? struck,
        IReadOnlyList<Cell>? cells, Cell destination, int damagePercent)
    {
        switch (selfMove)
        {
            case SelfMoveType.Pierce:
            {
                var course = cells!;
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, course));
                var crossed = course.Take(course.Count - 1).ToHashSet();
                var hit = _units.Where(u => u != caster && u.Status == UnitStatus.Active && !u.IsCivilian
                                            && crossed.Contains(u.Position)).ToList();
                Displace(caster, destination);
                foreach (var unit in hit)
                    ResolveHit(caster, skill, unit, damagePercent);
                break;
            }
            case SelfMoveType.Leap:
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, cells));
                Displace(caster, destination);
                foreach (var unit in UnitsAffectedBy(caster, skill, cells!))
                    ResolveHit(caster, skill, unit, damagePercent, origin: destination); // l'onde part de l'atterrissage
                break;
            case SelfMoveType.Rush:
                // Même ordre que les autres : la compétence, la course, puis le coup (s'il a percuté quelqu'un).
                _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, struck?.Id));
                if (destination != caster.Position)
                    Displace(caster, destination);
                if (struck is not null)
                    ResolveHit(caster, skill, struck, damagePercent);
                break;
        }
    }

    /// <summary>
    /// Unités debout qu'une zone de <paramref name="caster"/> toucherait, dans l'ordre des unités : jamais le lanceur,
    /// puis tout le monde, les ennemis seulement ou les alliés seulement selon la zone ; une zone « ennemis » qui touche
    /// aussi les gadgets du lanceur (IEM) y ajoute ceux-ci, de son équipe. Les civils ne sont touchés que par les zones
    /// qui touchent tout le monde.
    /// </summary>
    internal List<Unit> UnitsAffectedBy(Unit caster, SkillDefinition skill, IReadOnlyList<Cell> zone)
    {
        var cells = zone.ToHashSet();
        var affects = skill.Zone!.Affects;
        return _units
            .Where(u => u != caster && u.Status == UnitStatus.Active && cells.Contains(u.Position))
            .Where(u => affects == ZoneTargets.All
                        || (!u.IsCivilian && (affects == ZoneTargets.Allies) == (u.Team == caster.Team))
                        || (skill.Zone.AlsoHitsOwnSummons && u.SummonedBy == caster.Id))
            .ToList();
    }

    /// <summary>Dôme posé par une unité : ses cases, et s'il a déjà bloqué quelque chose pendant la source en cours.</summary>
    private sealed class Dome(Unit owner, IReadOnlyList<Cell> cells)
    {
        public Unit Owner { get; } = owner;
        public HashSet<Cell> Cells { get; } = cells.ToHashSet();
        public bool Absorbing { get; set; }
    }

    /// <summary>Pose le dôme (l'ancien est déjà tombé : poser un dôme est une action du lanceur).</summary>
    private void RaiseDome(Unit owner, IReadOnlyList<Cell> cells)
    {
        _domes.Add(new Dome(owner, cells));
        _events.Add(new DomeRaised(CurrentTick, owner.Id, cells));
    }

    private void RemoveDome(Unit owner)
    {
        if (_domes.RemoveAll(d => d.Owner == owner) > 0)
            _events.Add(new DomeEnded(CurrentTick, owner.Id));
    }

    /// <summary>
    /// Un coup offensif (compétence visant un ennemi ou une zone) sur <paramref name="target"/> est-il bloqué par
    /// un dôme de son équipe qui la couvre ? Si oui, le dôme absorbe la source en cours (il tombera à sa fin).
    /// </summary>
    private bool IsBlockedByDome(SkillDefinition skill, Unit target)
    {
        if (!skill.IsOffensive)
            return false;
        // Un dôme protège son équipe et les civils sous lui.
        var dome = _domes.FirstOrDefault(d => (d.Owner.Team == target.Team || target.IsCivilian) && d.Cells.Contains(target.Position));
        if (dome is null)
            return false;
        dome.Absorbing = true;
        _events.Add(new HitBlocked(CurrentTick, target.Id, dome.Owner.Id));
        return true;
    }

    /// <summary>
    /// Fin d'une source (compétence ou attaque d'opportunité) : les dômes qui l'ont absorbée disparaissent, ainsi que
    /// ceux dont le lanceur est tombé pendant cette source (pas avant : l'attaque reste bloquée pour tous).
    /// Tous les dégâts arrivent pendant une source, donc tout lanceur tombé passe par ici.
    /// </summary>
    private void EndSource()
    {
        if (_domes.Count == 0)
            return;
        foreach (var dome in _domes.Where(d => d.Absorbing || d.Owner.Status != UnitStatus.Active).ToList())
            RemoveDome(dome.Owner);
    }

    /// <summary>
    /// Projection : pousse <paramref name="target"/> en ligne droite, depuis <paramref name="origin"/>, sur la distance
    /// prévue. Elle s'arrête avant une unité, ou avant un mur, le bord ou une diagonale bloquée (même règle de coupe
    /// d'angle qu'un déplacement de la cible) ; une collision fait des dégâts à la cible et à l'unité percutée si
    /// elle est debout.
    /// </summary>
    private void Push(Unit caster, SkillDefinition skill, Unit target, ForcedMoveDefinition move, int damagePercent)
    {
        var origin = caster.Position;
        var step = (X: Math.Sign(target.Position.X - origin.X), Y: Math.Sign(target.Position.Y - origin.Y));
        if (step == (0, 0))
            return;

        var position = target.Position;
        var collided = false;
        Unit? obstacle = null;
        for (var i = 0; i < move.Distance; i++)
        {
            var next = new Cell(position.X + step.X, position.Y + step.Y);
            // Le décor (ou un coin bouché) arrête d'abord : on ne percute pas une unité à travers un coin de mur.
            if (!CanStepIgnoringDestination(target, position, next, downedBlockCorners: true))
            {
                collided = true;
                break;
            }
            if (UnitAt(next) is { } other && other != target)
            {
                collided = true;
                obstacle = other;
                break;
            }
            position = next;
        }

        if (position != target.Position)
            Displace(target, position);
        if (!collided)
            return;

        _events.Add(new UnitCollided(CurrentTick, target.Id, obstacle?.Id));
        if (move.CollisionMaxDamage == 0)
            return;
        foreach (var hit in new[] { target, obstacle })
        {
            // La cible (projetée sous un dôme de son équipe) ou l'unité percutée peuvent être protégées :
            // la collision fait partie de la même source.
            // Un civil percuté n'est pas blessé (seules les zones les touchent).
            if (hit is { Status: UnitStatus.Active, IsCivilian: false } && !IsBlockedByDome(skill, hit))
            {
                var damage = _random.NextInt(move.CollisionMinDamage, move.CollisionMaxDamage);
                Damage(hit, Scaled(damage, damagePercent, hit), fallbackCell: null);
            }
        }
    }

    /// <summary>Déplacement forcé (sans chemin ni attaque d'opportunité) ; il interrompt une charge en cours.</summary>
    private void Displace(Unit unit, Cell to)
    {
        var from = unit.Position;
        unit.Position = to;
        _events.Add(new UnitDisplaced(CurrentTick, unit.Id, from, to));
        InterruptCharge(unit);
    }

    private int RestartPercentAfter(SkillDefinition skill) => RestartPercentAfter(skill.Weight);

    private int RestartPercentAfter(SkillWeight weight) =>
        weight == SkillWeight.Heavy ? Rules.HeavyActionRestartPercent : Rules.LightActionRestartPercent;

    /// <summary>
    /// Déclenche les attaques chargées arrivées à échéance, dans l'ordre des unités. Une attaque ciblée ne touche
    /// que si la cible est encore debout, à portée et en vue ; une zone touche ce qui s'y trouve maintenant.
    /// </summary>
    private void ReleaseCharges()
    {
        foreach (var unit in _units)
        {
            // Une charge peut être interrompue par le déclenchement d'une autre, plus tôt dans la boucle.
            if (unit.Charge is not { } charge || charge.ReleaseTick > CurrentTick)
                continue;

            unit.Charge = null;
            var skill = charge.Skill;
            if (charge.Zone is not null)
                Execute(unit, skill, null, charge.Zone, charge.DamagePercent, zoneOrigin: charge.ZoneOrigin);
            else if (charge.Target is { } targetId && FindUnit(targetId) is { } target
                     && CheckTarget(unit, unit.Position, target, skill) is null)
                Execute(unit, skill, target, null, charge.DamagePercent);
            else
                _events.Add(new ChargeMissed(CurrentTick, unit.Id, skill.Id, charge.Target!.Value));

            SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(skill)));
            DismissIfExhausted(unit);
        }
    }

    private void InterruptCharge(Unit unit)
    {
        if (unit.Charge is not { } charge)
            return;
        unit.Charge = null;
        _events.Add(new ChargeInterrupted(CurrentTick, unit.Id, charge.Skill.Id));
    }

    /// <summary>
    /// Déplace l'unité pas à pas ; chaque pas qui sort de la portée d'un ennemi peut déclencher son attaque
    /// d'opportunité (dans l'ordre des unités), sauf si l'unité a le passif qui les ignore.
    /// Renvoie faux si l'unité est tombée ou étourdie en chemin.
    /// </summary>
    private bool MoveAlong(Unit unit, IReadOnlyList<Cell> path)
    {
        var segmentStart = unit.Position;
        var segment = new List<Cell>();
        var lastFreeCell = unit.Position;
        var canBePunished = !unit.HasPassive(PassiveKind.IgnoresOpportunityAttacks);

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
            foreach (var enemy in canBePunished ? _units : Enumerable.Empty<Unit>())
            {
                if (!CanMakeOpportunityAttack(enemy, unit, next))
                    continue;

                FlushSegment();
                var attack = enemy.BasicAttack!;
                enemy.OpportunityAttacksLeft--;
                _events.Add(new OpportunityAttack(CurrentTick, enemy.Id, unit.Id, attack.Id));
                ResolveHit(enemy, attack, unit, DamagePercentOf(enemy), lastFreeCell);
                EndSource(); // chaque attaque d'opportunité est une source à part
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
    /// de sa portée, et c'est un ennemi debout, non étourdi, pas en train de charger, avec une attaque de base et
    /// une attaque d'opportunité restante.
    /// </summary>
    private bool CanMakeOpportunityAttack(Unit enemy, Unit mover, Cell next) =>
        enemy != mover
        && AreOpponents(enemy, mover)
        && enemy.Status == UnitStatus.Active
        && !enemy.HasStatus(StatusKind.Stunned)
        && enemy.Charge is null
        && enemy.BasicAttack is not null
        && enemy.OpportunityAttacksLeft > 0
        && ControllerOf(enemy) != Behaviors.FleeInstance // en fuite, elle ne fait que courir
        && IsInMeleeReach(enemy, mover.Position)
        && !IsInMeleeReach(enemy, next);

    /// <summary>
    /// Case que l'unité peut frapper au contact : adjacente, libre, sans coupe d'angle par un mur (règle de la vue :
    /// un obstacle bas sur un coin ne gêne pas).
    /// </summary>
    private bool IsInMeleeReach(Unit unit, Cell cell) => IsInMeleeReach(unit.Position, cell);

    private bool IsInMeleeReach(Cell from, Cell cell) =>
        from.IsAdjacentTo(cell) && LineOfSight.IsClear(_grid, from, cell);

    private bool IsOccupiedByOther(Unit unit, Cell cell) =>
        _units.Any(u => u != unit && u.Status != UnitStatus.Removed && u.Position == cell);

    /// <summary>
    /// Effets d'un coup de <paramref name="attacker"/> sur sa cible : dégâts (s'il y en a : exécution si la cible est
    /// assez basse et pas un boss, sinon tirage multiplié par <paramref name="damagePercent"/> et par la vulnérabilité
    /// de la cible), Point faible de l'attaquant, puis états si la cible est encore debout, puis gain de jauge.
    /// <paramref name="fallbackCell"/> : où tombe la cible si elle est à terre sur une case qu'elle partage
    /// (seulement possible en plein déplacement, en traversant un allié).
    /// <paramref name="origin"/> : d'où vient le coup (tireur, centre d'une zone), si la couverture peut jouer ; une
    /// exécution l'ignore.
    /// Renvoie faux si un dôme a bloqué le coup (il n'a alors aucun effet).
    /// </summary>
    private bool ResolveHit(Unit attacker, SkillDefinition skill, Unit target, int damagePercent, Cell? fallbackCell = null,
        Cell? origin = null)
    {
        if (IsBlockedByDome(skill, target))
            return false;
        // Immunisée (robot face à un pouvoir mental, prise dans une zone : une cible directe est refusée avant).
        if (IsImmune(skill, target))
        {
            _events.Add(new UnitResisted(CurrentTick, target.Id));
            return false;
        }

        // Contre un robot adverse, l'effet renforcé de la compétence (dégâts en plus, états plus longs) ; pas sur ses
        // propres gadgets pris dans son IEM, étourdis comme les ennemis ordinaires.
        var robotBonus = target.Definition.IsRobot && AreOpponents(target, attacker) ? skill.VsRobots : null;
        if (skill.DealsDamage || robotBonus is { DealsDamage: true })
        {
            int dealt;
            if (CanExecute(skill, target))
            {
                _events.Add(new UnitExecuted(CurrentTick, attacker.Id, target.Id));
                dealt = target.Hp;
            }
            else
            {
                var roll = skill.DealsDamage ? _random.NextInt(skill.MinDamage, skill.MaxDamage) : 0;
                if (robotBonus is { DealsDamage: true })
                    roll += _random.NextInt(robotBonus.MinDamage, robotBonus.MaxDamage);
                dealt = Scaled(roll, damagePercent, target);
                if (origin is { } from && CoverFor(from, target.Position) is { } cover)
                    dealt = AbsorbByCover(target, cover, dealt);
            }
            Damage(target, dealt, fallbackCell);

            // Point faible : un coup qui a vraiment fait des dégâts à un ennemi le rend vulnérable.
            if (dealt > 0 && target.Status == UnitStatus.Active && AreOpponents(target, attacker)
                && attacker.HasPassive(PassiveKind.WeakPoint))
                ApplyStatus(target, new StatusEffect { Status = StatusKind.Exposed, DurationMs = Rules.WeakPointExposedMs }, attacker);
        }
        if (target.Status != UnitStatus.Active)
            return true;
        if (skill.Heals)
            Heal(target, _random.NextInt(skill.MinHeal, skill.MaxHeal));
        foreach (var effect in skill.Effects)
            ApplyStatus(target, effect, attacker, robotBonus?.StatusDurationPercent ?? 100);
        if (skill.GaugeGainPercent > 0)
            GainGauge(target, Rules.GaugeAt(skill.GaugeGainPercent));
        return true;
    }

    /// <summary>
    /// Obstacle bas qui couvre <paramref name="target"/> contre un coup venu de <paramref name="origin"/> : le premier
    /// que traverse la ligne (centre à centre, coins compris) parmi ceux collés à la cible. Aucun pour un coup au
    /// contact (ou parti de la case même de la cible).
    /// </summary>
    private Cell? CoverFor(Cell origin, Cell target)
    {
        if (origin == target || origin.IsAdjacentTo(target))
            return null;
        foreach (var cell in LineOfSight.CrossedCells(origin, target))
        {
            if (_grid.TerrainAt(cell) == Terrain.LowObstacle && cell.IsAdjacentTo(target))
                return cell;
        }
        return null;
    }

    /// <summary>
    /// L'obstacle <paramref name="cover"/> absorbe sa part des dégâts (arrondie en dessous), dans la limite de ses PV, et
    /// l'encaisse ; à 0 PV, il devient du sol. Renvoie les dégâts qui passent.
    /// </summary>
    private int AbsorbByCover(Unit target, Cell cover, int damage)
    {
        var hp = _coverHp.GetValueOrDefault(cover, Rules.LowObstacleHp);
        var absorbed = Math.Min(hp, (int)((long)damage * Rules.CoverDamageReductionPercent / 100));
        if (absorbed == 0)
            return damage;
        hp -= absorbed;
        _coverHp[cover] = hp;
        _events.Add(new HitCovered(CurrentTick, target.Id, cover, absorbed, hp));
        if (hp == 0)
        {
            _grid.SetTerrain(cover, Terrain.Floor);
            _coverHp.Remove(cover);
            _events.Add(new CoverDestroyed(CurrentTick, cover));
        }
        return damage - absorbed;
    }

    /// <summary>Exécution : la cible est au plus au seuil de PV de la compétence, et ce n'est pas un boss.</summary>
    private static bool CanExecute(SkillDefinition skill, Unit target) =>
        skill.ExecuteAtOrBelowPercent > 0
        && !target.Definition.IsBoss
        && (long)target.Hp * 100 <= (long)target.MaxHp * skill.ExecuteAtOrBelowPercent;

    /// <summary>
    /// Dégâts finaux : tirage × bonus de l'attaquant (<paramref name="damagePercent"/>) × vulnérabilité de la cible,
    /// en une seule division (pas d'arrondi intermédiaire).
    /// </summary>
    private int Scaled(int damage, int damagePercent, Unit target)
    {
        var exposedPercent = target.HasStatus(StatusKind.Exposed) ? Rules.ExposedDamageTakenPercent : 100;
        return (int)((long)damage * damagePercent * exposedPercent / 10_000);
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

    /// <summary>Distance en cases (diagonale = 1), sans tenir compte des obstacles.</summary>
    private static bool IsWithinRange(Cell from, Cell to, int range)
    {
        var distance = from.DistanceTo(to);
        return distance > 0 && distance <= range;
    }

    /// <summary>
    /// Vue de <paramref name="from"/> (case d'arrivée de l'attaquant) vers <paramref name="to"/>.
    /// Au contact : pas de coupe d'angle par un mur, mais seul le décor compte (règle du corps à corps).
    /// Au-delà : ligne de vue, bloquée par les murs et les ennemis debout de l'attaquant.
    /// Les obstacles bas ne gênent jamais la vue.
    /// </summary>
    private bool HasLineOfSight(Unit attacker, Cell from, Cell to)
    {
        if (from.IsAdjacentTo(to))
            return LineOfSight.IsClear(_grid, from, to);
        return LineOfSight.IsClear(_grid, from, to, cell => BlocksSightOf(attacker, cell));
    }

    private void Damage(Unit target, int amount, Cell? fallbackCell)
    {
        target.Hp = Math.Max(0, target.Hp - amount);
        _events.Add(new UnitDamaged(CurrentTick, target.Id, amount, target.Hp));
        if (target.Hp > 0)
        {
            if (!target.IsFleeing && target.Definition.FleeAtHpPercent is { } percent
                && (long)target.Hp * 100 < (long)target.MaxHp * percent)
                StartFleeing(target);
            return;
        }

        // Son dôme éventuel tombe à la fin de la source en cours (EndSource).
        TakeOutOfAction(target, target.OnDefeat == DefeatOutcome.Downed ? UnitStatus.Downed : UnitStatus.Removed);

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

    /// <summary>
    /// Met l'unité hors de combat (à terre ou retirée) : elle perd ses états, sa charge et son intention, annoncés
    /// avant sa chute (rien ne lui arrive plus après). Sa jauge est gelée à sa valeur. Le statut change d'abord,
    /// pour que la fin d'un étourdissement n'annonce pas l'unité prête.
    /// </summary>
    private void TakeOutOfAction(Unit unit, UnitStatus status)
    {
        unit.Status = status;
        foreach (var effect in unit.StatusEndTicks.Keys.ToList())
            EndStatus(unit, effect);
        InterruptCharge(unit);
        unit.Intention = null;
    }

    /// <summary>
    /// L'unité passe en fuite, pour le reste du combat. Si c'est désormais la fuite qui la commande (ni confuse ni
    /// dominée), elle abandonne sa charge et son intention.
    /// </summary>
    private void StartFleeing(Unit unit)
    {
        unit.IsFleeing = true;
        _events.Add(new UnitFleeing(CurrentTick, unit.Id));
        if (ControllerOf(unit) != Behaviors.FleeInstance)
            return;
        InterruptCharge(unit);
        SetIntention(unit, null);
    }

    /// <summary>L'unité en fuite quitte le combat par le bord de carte : retirée, mais enfuie et non vaincue.</summary>
    private void Escape(Unit unit)
    {
        unit.Escaped = true;
        TakeOutOfAction(unit, UnitStatus.Removed);
        RemoveDome(unit);
        _events.Add(new UnitEscaped(CurrentTick, unit.Id));
    }

    /// <summary>Retire une unité sans qu'elle soit vaincue (gadget remplacé ou épuisé).</summary>
    private void Dismiss(Unit unit)
    {
        TakeOutOfAction(unit, UnitStatus.Removed);
        RemoveDome(unit);
        _events.Add(new UnitRemoved(CurrentTick, unit.Id));
    }

    /// <summary>
    /// Pose une unité invoquée sur <paramref name="cell"/>, de l'équipe du lanceur, jauge vide ; elle remplace celle
    /// du même type déjà posée par ce lanceur. Son identifiant suit le plus grand existant (déterministe).
    /// </summary>
    private void Summon(Unit owner, UnitDefinition definition, Cell cell)
    {
        foreach (var previous in _units.Where(u => u.SummonedBy == owner.Id && u.Definition.Id == definition.Id
                                                   && u.Status != UnitStatus.Removed).ToList())
            Dismiss(previous);

        var id = new UnitId(_units.Max(u => u.Id.Value) + 1);
        var unit = new Unit(definition.ToSetup(id, owner.Team, cell), Rules.GaugeCapacity) { SummonedBy = owner.Id };
        _units.Add(unit);
        _events.Add(new UnitSummoned(CurrentTick, id, definition.Id, owner.Id, owner.Team, cell));
    }

    /// <summary>Soigne une unité debout, sans dépasser son maximum.</summary>
    private void Heal(Unit unit, int amount)
    {
        var healed = Math.Min(amount, unit.MaxHp - unit.Hp);
        if (healed <= 0)
            return;
        unit.Hp += healed;
        _events.Add(new UnitHealed(CurrentTick, unit.Id, healed, unit.Hp));
    }

    /// <summary>
    /// Applique un état posé par <paramref name="source"/> ; s'il est déjà là, garde la plus longue des deux durées.
    /// Emprise : un état néfaste posé par une unité qui a ce passif dure plus longtemps.
    /// Étourdi : interrompt la charge. Confus ou dominé : l'unité change de décisions (et de camp, si dominée).
    /// </summary>
    private void ApplyStatus(Unit target, StatusEffect effect, Unit source, int durationPercent = 100)
    {
        // Tous les multiplicateurs de durée (robot, Emprise) en une seule division.
        long durationMs = (long)effect.DurationMs * durationPercent, scale = 100;
        if (effect.Status.IsHarmful() && source.HasPassive(PassiveKind.Grip))
        {
            durationMs *= Rules.GripDurationPercent;
            scale *= 100;
        }
        durationMs /= scale;
        var endTick = CurrentTick + Rules.MillisecondsToTicks((int)durationMs);
        if (target.StatusEndTicks.TryGetValue(effect.Status, out var currentEnd))
            endTick = Math.Max(endTick, currentEnd);

        target.SetStatus(effect.Status, endTick);
        _events.Add(new StatusApplied(CurrentTick, target.Id, effect.Status, endTick));

        switch (effect.Status)
        {
            case StatusKind.Stunned:
                InterruptCharge(target);
                break;
            case StatusKind.Confused:
                InterruptCharge(target);
                SetIntention(target, null);
                break;
            case StatusKind.Dominated:
                InterruptCharge(target);
                SetIntention(target, null);
                target.DominatedBy = source.Id;
                ChangeTeam(target, source.Team);
                break;
        }
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
        // On traverse un civil comme un allié, et un civil traverse tout le monde.
        return AreOpponents(other, mover) ? Occupant.Enemy : Occupant.Ally;
    }

    /// <summary>Adversaires : de camps différents, et aucun des deux n'est un civil (neutre).</summary>
    internal static bool AreOpponents(Unit a, Unit b) => a.Team != b.Team && !a.IsCivilian && !b.IsCivilian;

    /// <summary>Pour la ligne de vue : seuls les ennemis debout bloquent.</summary>
    private bool BlocksSightOf(Unit viewer, Cell cell) =>
        UnitAt(cell) is { Status: UnitStatus.Active } other && AreOpponents(other, viewer);

    private void Reject(CombatCommand command, RejectionReason reason) =>
        _events.Add(new CommandRejected(CurrentTick, command, reason));

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
    }
}
