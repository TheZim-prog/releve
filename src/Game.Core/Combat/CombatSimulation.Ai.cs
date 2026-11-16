namespace Game.Core.Combat;

// Unités commandées par l'IA : qui décide pour une unité, intentions, exécution, fuite.
public sealed partial class CombatSimulation
{
    /// <summary>
    /// Provoque <paramref name="target"/> : son IA ne visera que <paramref name="taunter"/> pendant ses prochaines
    /// activations. Elle abandonne son intention ; une attaque déjà en charge part comme prévu. Sans effet sur une
    /// unité qui n'obéit pas aux provocations (en fuite, confuse, balise, commandée par un joueur). Une nouvelle
    /// provocation du même lanceur ne raccourcit pas celle en cours ; celle d'un autre la remplace.
    /// </summary>
    private void Taunt(Unit target, Unit taunter, int actions)
    {
        if (ControllerOf(target)?.ObeysTaunts != true)
            return;
        if (target.TauntedBy == taunter.Id)
            actions = Math.Max(actions, target.TauntActionsLeft);
        target.TauntedBy = taunter.Id;
        target.TauntActionsLeft = actions;
        _events.Add(new UnitTaunted(CurrentTick, target.Id, taunter.Id, actions));
        SetIntention(target, null);
    }

    /// <summary>Celui que l'unité provoquée doit viser, s'il est encore debout et toujours son adversaire.</summary>
    internal Unit? TaunterOf(Unit unit) =>
        unit.TauntActionsLeft > 0 && unit.TauntedBy is { } id && FindUnit(id) is { Status: UnitStatus.Active } taunter
        && AreOpponents(taunter, unit)
            ? taunter
            : null;

    /// <summary>
    /// L'unité vient de s'activer : si elle obéissait à une provocation (attaque ou approche), une activation de moins
    /// à subir. Une activation où elle n'obéit pas (confuse, dominée par un joueur…) ne compte pas.
    /// </summary>
    private void SpendTaunt(Unit unit)
    {
        if (TaunterOf(unit) is null || ControllerOf(unit)?.ObeysTaunts != true)
            return;
        if (unit.TauntActionsLeft == 1)
            EndTaunt(unit);
        else
            unit.TauntActionsLeft--;
    }

    /// <summary>La provocation de l'unité prend fin, s'il y en a une.</summary>
    private void EndTaunt(Unit unit)
    {
        if (unit.TauntActionsLeft == 0)
            return;
        unit.TauntedBy = null;
        unit.TauntActionsLeft = 0;
        _events.Add(new TauntEnded(CurrentTick, unit.Id));
    }

    /// <summary>L'unité tombe ou quitte le combat : sa provocation prend fin, et celles qu'elle avait lancées aussi.</summary>
    private void EndTauntsOf(Unit unit)
    {
        EndTaunt(unit);
        foreach (var taunted in _units.Where(u => u.TauntedBy == unit.Id))
            EndTaunt(taunted);
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

    /// <summary>L'unité obéit-elle aux joueurs (ni IA, ni confuse, ni dominée par l'IA, ni civil) ?</summary>
    public bool IsPlayerControlled(UnitId unit) => FindUnit(unit) is { } found && ControllerOf(found) is null;

    /// <summary>
    /// Une unité IA prête exécute son intention. Si elle est absente ou devenue impossible (cible vaincue,
    /// compétence en recharge, cible hors d'atteinte), l'unité en choisit une nouvelle ; si celle-ci non plus
    /// ne peut pas être exécutée maintenant, l'unité se rapproche de sa cible, ou attend jauge pleine.
    /// </summary>
    private void RunAi(Unit unit, IUnitBehavior behavior)
    {
        // Une unité en fuite déjà au bord quitte le combat au lieu d'agir (sauf immobilisée : elle est retenue).
        if (behavior == Behaviors.FleeInstance && unit.CanMove && _grid.IsEdge(unit.Position))
        {
            Escape(unit);
            return;
        }

        var command = (unit.Intention is { } intention ? behavior.Execute(this, unit, intention) : null) ?? Rethink(unit, behavior);
        // Provoquée, mais elle ne peut ni frapper ni approcher celui qui l'a provoquée : la provocation tombe (sinon elle
        // resterait figée), et elle choisit librement.
        if (command is null && behavior.ObeysTaunts && TaunterOf(unit) is not null)
        {
            EndTaunt(unit);
            command = Rethink(unit, behavior);
        }
        if (command is null)
            return;

        // Même validation que pour un joueur : le comportement ne contourne aucune règle.
        var (plan, reason) = ValidateActivation(command);
        // Un état ou une recharge que le comportement n'a pas vu : l'unité attend, comme un ordre de joueur.
        if (plan is null && reason!.Value.OnlyNeedsTime())
            return;
        if (plan is null)
            throw new InvalidOperationException($"{behavior.GetType().Name} produced an invalid command ({reason}) for unit {unit.Id}: {command}.");
        ApplyActivation(plan);

        // En fuite (déjà, ou depuis une attaque d'opportunité en chemin) et arrivée au bord : elle s'enfuit aussitôt.
        if (unit.Status == UnitStatus.Active && unit.CanMove && ControllerOf(unit) == Behaviors.FleeInstance && _grid.IsEdge(unit.Position))
            Escape(unit);
    }

    /// <summary>Nouvelle intention, et l'activation qui l'exécute ou, à défaut, qui rapproche de sa cible (ou <c>null</c>).</summary>
    private ActivateCommand? Rethink(Unit unit, IUnitBehavior behavior)
    {
        SetIntention(unit, behavior.ChooseIntention(this, unit));
        return unit.Intention is { } fresh ? behavior.Execute(this, unit, fresh) ?? behavior.Approach(this, unit, fresh) : null;
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

    private void SetIntention(Unit unit, Intention? intention)
    {
        if (intention == unit.Intention)
            return;
        unit.Intention = intention;
        _events.Add(new IntentionChosen(CurrentTick, unit.Id, intention));
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

    /// <summary>
    /// L'unité en fuite quitte le combat par le bord de carte : retirée, mais enfuie et non vaincue. Hors de toute source,
    /// ce que sa sortie provoque (les sbires d'un chef enfui) part aussitôt.
    /// </summary>
    private void Escape(Unit unit)
    {
        unit.Escaped = true;
        Withdraw(unit, new UnitEscaped(CurrentTick, unit.Id));
        FlushReactions();
    }
}
