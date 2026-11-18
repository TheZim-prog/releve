using Game.Core.Grids;

namespace Game.Core.Combat;

// Commandes des joueurs et ordres à l'avance : validation d'une activation (atomique), puis application.
public sealed partial class CombatSimulation
{
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
            case ContinueCommand resume:
                Continue(resume);
                break;
            default:
                throw new ArgumentException($"Unknown command type {command.GetType().Name}.", nameof(command));
        }
    }

    private void ApplyActivate(ActivateCommand command)
    {
        var (plan, reason) = ValidatePlayerCommand(command);
        if (plan is null)
        {
            Reject(command, reason!.Value);
            return;
        }
        ApplyActivation(plan);
        // Pendant des actions imposées, seule une action imposée passe la validation.
        if (ImposedOn(plan.Unit.Id) is { } imposition)
            CompleteImposition(imposition);
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

    /// <summary>
    /// Ordres à l'avance tels que le joueur les a demandés : ceux enregistrés, plus les ordres et annulations soumis
    /// mais pas encore appliqués (pendant la pause, par exemple). Pour l'affichage ; rien n'est changé.
    /// </summary>
    public IReadOnlyList<ActivateCommand> PlannedOrders() => PlannedOrders(Array.Empty<CombatCommand>());

    /// <summary>
    /// Comme <see cref="PlannedOrders()"/>, en comptant aussi des commandes qui ne sont pas encore arrivées ici (celles
    /// qu'un client a envoyées au serveur), après les commandes en attente.
    /// </summary>
    public IReadOnlyList<ActivateCommand> PlannedOrders(IEnumerable<CombatCommand> notYetSubmitted)
    {
        ArgumentNullException.ThrowIfNull(notYetSubmitted);
        var orders = new List<ActivateCommand>(_orders);
        foreach (var command in _pending.Concat(notYetSubmitted))
        {
            if (command is not (OrderCommand or CancelOrderCommand))
                continue;
            orders.RemoveAll(o => o.Unit == command.Unit);
            if (command is OrderCommand order)
                orders.Add(order.Activation);
        }
        return orders;
    }

    /// <summary>
    /// Commande d'un joueur : refusée pour une unité commandée par l'IA, sinon validée comme toute activation. Tant que le
    /// script fige le combat, une activation valide par ailleurs est refusée (un ordre à l'avance attend, lui), sauf
    /// l'action imposée à un héros : celle-là seule lui est permise, même pas un ordre à l'avance.
    /// </summary>
    private (ActivationPlan? Plan, RejectionReason? Reason) ValidatePlayerCommand(ActivateCommand command, bool ignoreTiming = false)
    {
        if (FindUnit(command.Unit) is { } commanded && ControllerOf(commanded) is not null)
            return (null, RejectionReason.NotControllable);
        if (ImposedOn(command.Unit) is { } imposed)
        {
            if (ignoreTiming)
                return (null, RejectionReason.ImposedActionIsNow);
            var result = ValidateActivation(command);
            return result.Plan is not null && !imposed.Allows(command) ? (null, RejectionReason.NotTheImposedAction) : result;
        }
        if (!ignoreTiming && (IsTimeFrozen || _inFrozenStep))
        {
            // La vraie raison d'abord (case inaccessible, hors de portée…) ; « figé » seulement s'il suffit d'attendre.
            var (_, reason) = ValidateActivation(command, ignoreTiming: true);
            return (null, reason ?? RejectionReason.TimeFrozen);
        }
        return ValidateActivation(command, ignoreTiming);
    }

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
            if (!ignoreTiming && !unit.CanMove)
                return (null, RejectionReason.Immobilized);
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
            if (!ignoreTiming && unit.UnavailableAt(skill, CurrentTick) is { } wait)
                return (null, wait);
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

    private void ApplyActivation(ActivationPlan plan)
    {
        ApplyActivationCore(plan);
        FlushGauge(plan.Unit);
        DismissIfExhausted(plan.Unit);
    }

    private void ApplyActivationCore(ActivationPlan plan)
    {
        var (unit, path, action, interaction) = plan;
        unit.Intention = null; // l'unité agit : son intention éventuelle est consommée
        SpendTaunt(unit); // et, provoquée, elle vient d'obéir une fois
        RemoveDome(unit); // et son dôme éventuel tombe (un nouveau peut être posé par cette action)
        unit.Gauge = 0; // l'unité a agi : sa jauge n'est plus pleine, même si elle repart à 100 %
        unit.AttackedSinceActivation = false;
        unit.OpportunityAttacksLeft = unit.OpportunityAttacksPerCycle; // rechargées quand l'unité rejoue
        ResetReactions(unit); // ses réactions aussi ; sa garde et sa surveillance tombent

        // Tombée, étourdie ou immobilisée en chemin par une attaque d'opportunité : le reste du trajet et l'action
        // sont annulés. Encore debout, elle a quand même bougé : sa jauge repart comme après un déplacement seul.
        if (path is not null && !MoveAlong(unit, path))
        {
            if (unit.Status == UnitStatus.Active)
                SetGauge(unit, Rules.GaugeAt(Rules.MoveOnlyRestartPercent));
            return;
        }

        // Elle agit dans une zone surveillée : le tir part avant son action ; tombée ou étourdie, elle ne la fait pas.
        if ((action is not null || interaction is not null) && HasZoneWatchOn(unit, unit.Position, unit.Position))
        {
            TriggerZoneWatches(unit, unit.Position, unit.Position);
            if (!CanStillAct(unit))
            {
                if (unit.Status == UnitStatus.Active)
                    SetGauge(unit, Rules.GaugeAt(Rules.MoveOnlyRestartPercent));
                return;
            }
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
            SkillWeight weight;
            switch (interaction.Kind)
            {
                case InteractionKind.Revive:
                    Revive(unit, interaction.Target!);
                    weight = Rules.ReviveWeight;
                    break;
                case InteractionKind.Operate:
                    Operate(unit, interaction.Device!);
                    weight = Rules.OperateWeight;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown interaction {interaction.Kind}.");
            }
            SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(weight)));
            AfterAction(unit);
            return;
        }

        // Passée en fuite en chemin (attaque d'opportunité) : elle ne fait plus que courir, l'action est annulée. De même
        // si un état reçu en chemin lui interdit la compétence prévue (désarmée : seule son attaque de base passerait),
        // ou si elle n'a plus de PV à donner (Transfusion, après une attaque d'opportunité).
        if (action is null || ControllerOf(unit) == Behaviors.FleeInstance || unit.StatusBlocking(action.Skill) is not null
            || (action.Skill.GivesOwnHp && unit.Hp <= 1))
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
                CurrentTick + Rules.MillisecondsToTicks(actionSkill.ChargeMs), DamageFactorOf(unit), action.Origin);
            unit.Charge = charge;
            _events.Add(new ChargeStarted(CurrentTick, unit.Id, actionSkill.Id, charge.Target, charge.Zone, charge.ReleaseTick));
            // Une forteresse se lève tout de suite et tient pendant la charge.
            if (actionSkill.HoldsDome)
                RaiseDome(unit, charge.Zone, held: true);
            return;
        }

        Execute(unit, actionSkill, action.Target, action.Zone, DamageFactorOf(unit), action.Destination, action.Origin, action.Chain);
        if (unit.Status == UnitStatus.Active) // tombée par une réaction à son action (riposte, explosion) : jauge gelée
            SetGauge(unit, Rules.GaugeAt(RestartPercentAfter(actionSkill)));
        AfterAction(unit);
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

    private int RestartPercentAfter(SkillDefinition skill) => RestartPercentAfter(skill.Weight);

    private int RestartPercentAfter(SkillWeight weight) =>
        weight == SkillWeight.Heavy ? Rules.HeavyActionRestartPercent : Rules.LightActionRestartPercent;

    private void Reject(CombatCommand command, RejectionReason reason) =>
        _events.Add(new CommandRejected(CurrentTick, command, reason));
}
