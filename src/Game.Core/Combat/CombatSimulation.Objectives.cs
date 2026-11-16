using Game.Core.Objectives;

namespace Game.Core.Combat;

// Objectifs de mission et issue du combat.
public sealed partial class CombatSimulation
{
    /// <summary>Camp des joueurs : sa chute est une défaite, et c'est lui que servent les objectifs.</summary>
    public const int PlayerTeam = 0;

    private readonly List<Objective> _objectives = new();

    /// <summary>Objectifs du combat, dans l'ordre du scénario.</summary>
    public IReadOnlyList<Objective> Objectives { get; private set; } = Array.Empty<Objective>();

    /// <summary>
    /// Victoire, défaite, ou <c>null</c> tant que le combat dure. Une fois fixée, elle ne change plus (la simulation
    /// peut encore tourner, mais les objectifs ne bougent plus).
    /// </summary>
    public bool? Outcome { get; private set; }

    private void SetUpObjectives(IReadOnlyList<ObjectiveDefinition> objectives)
    {
        ObjectiveDefinition.ValidateList(objectives);
        _objectives.AddRange(objectives.Select(o => new Objective(o)));
        Objectives = _objectives.AsReadOnly();
    }

    /// <summary>
    /// Fin de tick : défaite si tous les héros sont hors de combat (rien ne se réussit alors, même au même tick) ; sinon
    /// chaque objectif en cours peut être réussi ou raté, puis l'issue tombe : défaite si un objectif principal est raté,
    /// victoire quand tous les principaux sont réussis. Un combat sans objectif (tests, outils) ne se termine jamais.
    /// </summary>
    private void UpdateObjectives()
    {
        if (Outcome is not null || _objectives.Count == 0)
            return;
        if (!TeamsStillFighting().Contains(PlayerTeam))
        {
            EndCombat(victory: false);
            return;
        }

        for (var i = 0; i < _objectives.Count; i++)
        {
            var objective = _objectives[i];
            if (objective.State != ObjectiveState.InProgress)
                continue;
            objective.State = ObjectiveRules.Evaluate(objective.Definition, this);
            if (objective.State == ObjectiveState.Completed)
                _events.Add(new ObjectiveCompleted(CurrentTick, i));
            else if (objective.State == ObjectiveState.Failed)
                _events.Add(new ObjectiveFailed(CurrentTick, i));
        }

        var main = _objectives.Where(o => !o.Definition.Secondary);
        if (main.Any(o => o.State == ObjectiveState.Failed))
            EndCombat(victory: false);
        else if (main.All(o => o.State == ObjectiveState.Completed))
            EndCombat(victory: true);
    }

    private void EndCombat(bool victory)
    {
        Outcome = victory;
        _events.Add(new CombatEnded(CurrentTick, victory));
    }
}
