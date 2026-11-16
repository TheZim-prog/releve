using Game.Core.Combat;

namespace Game.Core.Objectives;

/// <summary>Où en est chaque type d'objectif, d'après l'état de la simulation : la règle de chacun, à un seul endroit.</summary>
internal static class ObjectiveRules
{
    public static ObjectiveState Evaluate(ObjectiveDefinition objective, CombatSimulation simulation) => objective.Kind switch
    {
        ObjectiveKind.Eliminate => NoEnemyLeft(simulation) ? ObjectiveState.Completed : ObjectiveState.InProgress,
        ObjectiveKind.Rescue => Rescue(objective.Minimum!.Value, RescueTally.Of(simulation), safe: NoEnemyLeft(simulation)),
        ObjectiveKind.Pursuit => Pursuit(simulation.PursuedUnit!),
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective.Kind, "Unknown objective kind."),
    };

    /// <summary>
    /// À la victoire, ce qui est encore en cours se règle : un sauvetage compte les civils encore sur la carte, hors de
    /// danger ; une poursuite est ratée (le vilain court encore : il s'échappe). L'élimination reste où elle en est.
    /// </summary>
    public static ObjectiveState SettleAtVictory(ObjectiveDefinition objective, CombatSimulation simulation) => objective.Kind switch
    {
        ObjectiveKind.Rescue => Rescue(objective.Minimum!.Value, RescueTally.Of(simulation), safe: true),
        ObjectiveKind.Pursuit => ObjectiveState.Failed,
        _ => ObjectiveState.InProgress,
    };

    /// <summary>
    /// Il ne reste debout que le camp des joueurs : les ennemis sont vaincus, enfuis ou en fuite (civils et gadgets à part).
    /// </summary>
    private static bool NoEnemyLeft(CombatSimulation simulation) =>
        simulation.TeamsStillFighting().All(team => team == CombatSimulation.PlayerTeam);

    /// <summary>Raté si le vilain poursuivi est sorti ; réussi s'il est hors de combat autrement (vaincu, à terre, rendu).</summary>
    private static ObjectiveState Pursuit(Unit pursued) =>
        pursued.Escaped ? ObjectiveState.Failed
        : pursued.Status != UnitStatus.Active ? ObjectiveState.Completed
        : ObjectiveState.InProgress;

    /// <summary>
    /// Réussi quand assez de civils sont évacués ; raté quand il n'en reste plus assez pour y arriver. Hors de danger
    /// (<paramref name="safe"/> : plus d'ennemi en lice, ou la victoire), ceux encore sur la carte comptent comme évacués.
    /// </summary>
    private static ObjectiveState Rescue(int minimum, RescueTally tally, bool safe)
    {
        var reachable = tally.Evacuated + tally.OnMap;
        return (safe ? reachable : tally.Evacuated) >= minimum ? ObjectiveState.Completed
            : reachable < minimum ? ObjectiveState.Failed
            : ObjectiveState.InProgress;
    }
}
