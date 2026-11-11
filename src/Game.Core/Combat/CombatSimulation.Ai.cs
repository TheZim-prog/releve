namespace Game.Core.Combat;

// Unités commandées par l'IA : qui décide pour une unité, intentions, exécution, fuite.
public sealed partial class CombatSimulation
{
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

    /// <summary>L'unité en fuite quitte le combat par le bord de carte : retirée, mais enfuie et non vaincue.</summary>
    private void Escape(Unit unit)
    {
        unit.Escaped = true;
        TakeOutOfAction(unit, UnitStatus.Removed);
        RemoveDome(unit);
        _events.Add(new UnitEscaped(CurrentTick, unit.Id));
    }
}
