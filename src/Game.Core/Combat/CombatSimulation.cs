using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Simulation d'un combat, à ticks fixes et entièrement déterministe.
/// <para>
/// Déroulement d'un tick : les états arrivés à échéance prennent fin ; les attaques chargées arrivées à
/// échéance se déclenchent ; les commandes des joueurs soumises depuis le tick précédent sont appliquées
/// dans leur ordre d'arrivée (la première validée l'emporte en cas de conflit) ; les unités IA prêtes
/// agissent ; les jauges se remplissent ; les unités IA qui passent le seuil fixent leur intention.
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
        ReleaseCharges();

        while (_pending.TryDequeue(out var command))
        {
            _commandLog.Add(new LoggedCommand(CurrentTick, command));
            Apply(command);
        }

        foreach (var unit in _units)
        {
            if (unit.Behavior is not null && unit.IsReady)
                RunAi(unit, Behaviors.Find(unit.Behavior)!);
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
            throw new InvalidOperationException($"Behavior '{unit.Behavior}' produced an invalid command ({reason}): {command}.");
        ApplyActivation(plan);
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
            if (unit.Behavior is null || unit.Status != UnitStatus.Active || unit.Charge is not null)
                continue;

            var needsIntention = unit.Intention is null
                ? unit.Gauge >= threshold
                : FindUnit(unit.Intention.Target) is not { Status: UnitStatus.Active };
            if (needsIntention)
                SetIntention(unit, Behaviors.Find(unit.Behavior)!.ChooseIntention(this, unit));
        }
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
        // Commande d'un joueur : refusée pour une unité commandée par l'IA.
        if (FindUnit(command.Unit) is { Behavior: not null })
        {
            Reject(command, RejectionReason.NotControllable);
            return;
        }

        var (plan, reason) = ValidateActivation(command);
        if (plan is null)
            Reject(command, reason!.Value);
        else
            ApplyActivation(plan);
    }

    /// <summary>Activation validée, prête à être appliquée telle quelle.</summary>
    internal sealed record ActivationPlan(Unit Unit, IReadOnlyList<Cell>? Path, PlannedAction? Action);

    /// <summary>
    /// Validation complète d'une activation, sans rien changer : la commande est atomique, rien n'est appliqué
    /// si une partie échoue. Sert aux commandes des joueurs comme à l'IA.
    /// </summary>
    internal (ActivationPlan? Plan, RejectionReason? Reason) ValidateActivation(ActivateCommand command)
    {
        var unit = FindUnit(command.Unit);
        if (unit is null)
            return (null, RejectionReason.UnknownUnit);
        if (unit.Status != UnitStatus.Active)
            return (null, RejectionReason.Incapacitated);
        if (unit.HasStatus(StatusKind.Stunned))
            return (null, RejectionReason.Stunned);
        if (unit.Charge is not null)
            return (null, RejectionReason.Charging);
        if (!unit.IsReady)
            return (null, RejectionReason.NotReady);
        if (command.MoveTo is null && command.Skill is null)
            return (null, RejectionReason.Empty);

        var position = unit.Position;
        IReadOnlyList<Cell>? path = null;
        if (command.MoveTo is { } destination)
        {
            if (destination == unit.Position)
                return (null, RejectionReason.InvalidDestination);
            path = Pathfinder.FindPath(_grid, unit.Position, destination, unit.Mobility, cell => OccupantFor(unit, cell));
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
            if (CurrentTick < unit.CooldownEndTick(skill.Id))
                return (null, RejectionReason.OnCooldown);
            var (planned, rejection) = PlanAction(unit, position, skill, use);
            if (rejection is not null)
                return (null, rejection);
            action = planned;
        }

        return (new ActivationPlan(unit, path, action), null);
    }

    private void ApplyActivation(ActivationPlan plan)
    {
        var (unit, path, action) = plan;
        unit.Intention = null; // l'unité agit : son intention éventuelle est consommée
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

        if (action is null)
        {
            SetGauge(unit, Rules.GaugeAt(Rules.MoveOnlyRestartPercent));
            return;
        }

        var actionSkill = action.Skill;
        unit.StartCooldown(actionSkill.Id, CurrentTick + Rules.MillisecondsToTicks(actionSkill.CooldownMs));
        if (actionSkill.IsCharged)
        {
            // Immobilisée, jauge à 0 jusqu'au déclenchement (ReleaseCharges). Les dégâts sont fixés maintenant :
            // c'est l'attaque que le joueur a lancée et vue au sol.
            var charge = new PendingCharge(actionSkill, action.Target?.Id, action.Zone,
                CurrentTick + Rules.MillisecondsToTicks(actionSkill.ChargeMs), DamagePercentOf(unit));
            unit.Charge = charge;
            _events.Add(new ChargeStarted(CurrentTick, unit.Id, actionSkill.Id, charge.Target, charge.Zone, charge.ReleaseTick));
            return;
        }

        Execute(unit, actionSkill, action.Target, action.Zone, DamagePercentOf(unit), action.Destination);
        SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(actionSkill)));
    }

    /// <summary>Multiplicateur des dégâts infligés par l'unité, en % (surpuissant ou non).</summary>
    private int DamagePercentOf(Unit unit) => unit.HasStatus(StatusKind.Empowered) ? Rules.EmpoweredDamagePercent : 100;

    /// <summary>
    /// Action validée : sur une unité (<paramref name="Target"/>) ou sur des cases (<paramref name="Zone"/>) ;
    /// <paramref name="Destination"/> : case où la télékinésie pose sa cible.
    /// </summary>
    internal sealed record PlannedAction(SkillDefinition Skill, Unit? Target, IReadOnlyList<Cell>? Zone, Cell? Destination = null);

    /// <summary>
    /// Valide la cible d'une compétence utilisée depuis <paramref name="position"/> (case d'arrivée du lanceur).
    /// Compétence à zone : une case, à portée (0 = sur soi), libre et en vue pour un carré, alignée pour une ligne,
    /// et une zone non vide. Attaque ciblée : voir <see cref="CheckTarget"/>.
    /// </summary>
    internal (PlannedAction? Action, RejectionReason? Reason) PlanAction(Unit unit, Cell position, SkillDefinition skill, SkillUse use)
    {
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
            return (new PlannedAction(skill, null, cells), null);
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
        if (skill.GaugeGainPercent > 0 && skill.Effects.Count == 0 && (target.IsGaugeFull || target.Charge is not null))
            return (null, RejectionReason.NoEffect);
        if (CheckTarget(unit, position, target, skill) is { } reason)
            return (null, reason);
        if (places && !CanPlace(unit, position, target, use.Cell!.Value, skill.ForcedMove!.Distance))
            return (null, RejectionReason.InvalidDestination);
        return (new PlannedAction(skill, target, null, use.Cell), null);
    }

    /// <summary>
    /// Une attaque vise un ennemi ; une compétence de soutien, un allié autre que le lanceur ; une télékinésie,
    /// n'importe quelle unité sauf le lanceur.
    /// </summary>
    private static bool IsRightSide(Unit caster, Unit target, SkillDefinition skill) => skill.Target switch
    {
        SkillTargeting.Enemy => target.Team != caster.Team,
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
    /// <paramref name="destination"/> : case où une télékinésie pose sa cible.
    /// </summary>
    private void Execute(Unit caster, SkillDefinition skill, Unit? target, IReadOnlyList<Cell>? zone, int damagePercent,
        Cell? destination = null)
    {
        if (zone is null)
        {
            _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, target!.Id));
            ResolveHit(caster, skill, target, damagePercent);
            if (target.Status == UnitStatus.Active && skill.ForcedMove is { } move)
            {
                if (move.Type == ForcedMoveType.Push)
                    Push(caster.Position, target, move, damagePercent);
                else
                    Displace(target, destination!.Value);
            }
            return;
        }

        _events.Add(new SkillUsed(CurrentTick, caster.Id, skill.Id, null, zone));
        var cells = zone.ToHashSet();
        var affected = _units
            .Where(u => u != caster && u.Status == UnitStatus.Active && cells.Contains(u.Position))
            .Where(u => skill.Zone!.Affects == ZoneTargets.All || u.Team != caster.Team)
            .ToList();
        foreach (var unit in affected)
            ResolveHit(caster, skill, unit, damagePercent);
    }

    /// <summary>
    /// Projection : pousse <paramref name="target"/> en ligne droite, depuis <paramref name="origin"/>, sur la distance
    /// prévue. Elle s'arrête avant une unité, ou avant un mur, le bord ou une diagonale bloquée (même règle de coupe
    /// d'angle qu'un déplacement de la cible) ; une collision fait des dégâts à la cible et à l'unité percutée si
    /// elle est debout.
    /// </summary>
    private void Push(Cell origin, Unit target, ForcedMoveDefinition move, int damagePercent)
    {
        var step = (X: Math.Sign(target.Position.X - origin.X), Y: Math.Sign(target.Position.Y - origin.Y));
        if (step == (0, 0))
            return;

        var position = target.Position;
        var collided = false;
        Unit? obstacle = null;
        for (var i = 0; i < move.Distance; i++)
        {
            var next = new Cell(position.X + step.X, position.Y + step.Y);
            if (UnitAt(next) is { } other && other != target)
            {
                collided = true;
                obstacle = other;
                break;
            }
            if (!_grid.CanStep(position, next, cell => OccupantFor(target, cell)))
            {
                collided = true;
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
            if (hit is { Status: UnitStatus.Active })
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

    private int RestartPercentAfter(SkillDefinition skill) =>
        skill.Weight == SkillWeight.Heavy ? Rules.HeavyActionRestartPercent : Rules.LightActionRestartPercent;

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
                Execute(unit, skill, null, charge.Zone, charge.DamagePercent);
            else if (charge.Target is { } targetId && FindUnit(targetId) is { } target
                     && CheckTarget(unit, unit.Position, target, skill) is null)
                Execute(unit, skill, target, null, charge.DamagePercent);
            else
                _events.Add(new ChargeMissed(CurrentTick, unit.Id, skill.Id, charge.Target!.Value));

            SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(skill)));
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
        && enemy.Team != mover.Team
        && enemy.Status == UnitStatus.Active
        && !enemy.HasStatus(StatusKind.Stunned)
        && enemy.Charge is null
        && enemy.BasicAttack is not null
        && enemy.OpportunityAttacksLeft > 0
        && IsInMeleeReach(enemy, mover.Position)
        && !IsInMeleeReach(enemy, next);

    /// <summary>Case que l'unité peut frapper au contact : adjacente, sans coupe d'angle par un mur.</summary>
    private bool IsInMeleeReach(Unit unit, Cell cell) => _grid.CanStep(unit.Position, cell);

    private bool IsOccupiedByOther(Unit unit, Cell cell) =>
        _units.Any(u => u != unit && u.Status != UnitStatus.Removed && u.Position == cell);

    /// <summary>
    /// Effets d'un coup de <paramref name="attacker"/> sur sa cible : dégâts (s'il y en a : exécution si la cible est
    /// assez basse et pas un boss, sinon tirage multiplié par <paramref name="damagePercent"/> et par la vulnérabilité
    /// de la cible), Point faible de l'attaquant, puis états si la cible est encore debout, puis gain de jauge.
    /// <paramref name="fallbackCell"/> : où tombe la cible si elle est à terre sur une case qu'elle partage
    /// (seulement possible en plein déplacement, en traversant un allié).
    /// </summary>
    private void ResolveHit(Unit attacker, SkillDefinition skill, Unit target, int damagePercent, Cell? fallbackCell = null)
    {
        if (skill.DealsDamage)
        {
            int dealt;
            if (CanExecute(skill, target))
            {
                _events.Add(new UnitExecuted(CurrentTick, attacker.Id, target.Id));
                dealt = target.Hp;
            }
            else
            {
                dealt = Scaled(_random.NextInt(skill.MinDamage, skill.MaxDamage), damagePercent, target);
            }
            Damage(target, dealt, fallbackCell);

            // Point faible : un coup qui a vraiment fait des dégâts à un ennemi le rend vulnérable.
            if (dealt > 0 && target.Status == UnitStatus.Active && target.Team != attacker.Team
                && attacker.HasPassive(PassiveKind.WeakPoint))
                ApplyStatus(target, new StatusEffect { Status = StatusKind.Exposed, DurationMs = Rules.WeakPointExposedMs });
        }
        if (target.Status != UnitStatus.Active)
            return;
        foreach (var effect in skill.Effects)
            ApplyStatus(target, effect);
        if (skill.GaugeGainPercent > 0)
            GainGauge(target, Rules.GaugeAt(skill.GaugeGainPercent));
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

        // Une unité vaincue perd ses états et sa charge, annoncés avant sa chute (rien n'arrive plus après),
        // ainsi que son intention.
        foreach (var status in target.StatusEndTicks.Keys.ToList())
            EndStatus(target, status);
        InterruptCharge(target);
        target.Intention = null;

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
        if (effect.Status == StatusKind.Stunned)
            InterruptCharge(target);
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
