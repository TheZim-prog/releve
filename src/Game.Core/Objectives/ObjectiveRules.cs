using Game.Core.Combat;

namespace Game.Core.Objectives;

/// <summary>Où en est chaque type d'objectif, d'après l'état de la simulation : la règle de chacun, à un seul endroit.</summary>
internal static class ObjectiveRules
{
    public static ObjectiveState Evaluate(ObjectiveDefinition objective, CombatSimulation simulation) => objective.Kind switch
    {
        ObjectiveKind.Eliminate => Eliminate(simulation),
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective.Kind, "Unknown objective kind."),
    };

    /// <summary>Réussi quand il ne reste debout que le camp des joueurs (ennemis vaincus ou enfuis, civils et gadgets à part).</summary>
    private static ObjectiveState Eliminate(CombatSimulation simulation) =>
        simulation.TeamsStillFighting().All(team => team == CombatSimulation.PlayerTeam)
            ? ObjectiveState.Completed
            : ObjectiveState.InProgress;
}
