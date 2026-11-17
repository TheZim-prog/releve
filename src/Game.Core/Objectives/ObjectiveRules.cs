using Game.Core.Combat;

namespace Game.Core.Objectives;

/// <summary>Où en est chaque type d'objectif, d'après l'état de la simulation : la règle de chacun, à un seul endroit.</summary>
internal static class ObjectiveRules
{
    public static ObjectiveState Evaluate(Objective objective, CombatSimulation simulation) => objective.Definition.Kind switch
    {
        ObjectiveKind.Eliminate => NoEnemyLeft(simulation) ? ObjectiveState.Completed : ObjectiveState.InProgress,
        ObjectiveKind.Rescue => Rescue(objective.Definition.Minimum!.Value, RescueTally.Of(simulation), safe: NoEnemyLeft(simulation)),
        ObjectiveKind.Pursuit => Pursuit(simulation.PursuedUnit!),
        ObjectiveKind.Countdown => Countdown(objective, simulation.CurrentTick),
        ObjectiveKind.Protect => Protect(objective, simulation),
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective.Definition.Kind, "Unknown objective kind."),
    };

    /// <summary>
    /// À la victoire, ce qui est encore en cours se règle : un sauvetage compte les civils encore sur la carte, hors de
    /// danger ; une poursuite est ratée (le vilain court encore : il s'échappe), une course contre la montre aussi (les
    /// objets n'ont pas été neutralisés) ; une protection est réussie (les personnes protégées sont encore debout : sinon
    /// elle serait déjà ratée) ; une élimination est ratée (des ennemis sont encore debout).
    /// </summary>
    public static ObjectiveState SettleAtVictory(Objective objective, CombatSimulation simulation) => objective.Definition.Kind switch
    {
        ObjectiveKind.Rescue => Rescue(objective.Definition.Minimum!.Value, RescueTally.Of(simulation), safe: true),
        ObjectiveKind.Protect => ObjectiveState.Completed,
        _ => ObjectiveState.Failed,
    };

    /// <summary>
    /// Il ne reste debout que le camp des joueurs, et aucun ennemi ne doit encore arriver : les ennemis sont vaincus,
    /// enfuis ou en fuite (civils et gadgets à part).
    /// </summary>
    private static bool NoEnemyLeft(CombatSimulation simulation) =>
        !simulation.EnemiesStillToCome && simulation.TeamsStillFighting().All(team => team == CombatSimulation.PlayerTeam);

    /// <summary>
    /// Raté dès qu'une personne protégée tombe (à terre, ou hors de combat sans s'être enfuie) ; sinon réussi à la fin des
    /// vagues (plus d'ennemi en lice ni à venir) ou quand le temps est tenu.
    /// </summary>
    private static ObjectiveState Protect(Objective objective, CombatSimulation simulation) =>
        simulation.Units.Any(u => u is { IsProtected: true, Status: not UnitStatus.Active, Escaped: false }) ? ObjectiveState.Failed
        : (objective.Definition.Until == ProtectUntil.Time ? simulation.CurrentTick >= objective.EndTick : NoEnemyLeft(simulation))
            ? ObjectiveState.Completed
            : ObjectiveState.InProgress;

    /// <summary>Raté si le vilain poursuivi est sorti ; réussi s'il est hors de combat autrement (vaincu, à terre, rendu).</summary>
    private static ObjectiveState Pursuit(Unit pursued) =>
        pursued.Escaped ? ObjectiveState.Failed
        : pursued.Status != UnitStatus.Active ? ObjectiveState.Completed
        : ObjectiveState.InProgress;

    /// <summary>Réussi quand tous les objets sont neutralisés ; raté quand le compte à rebours arrive au bout avant.</summary>
    private static ObjectiveState Countdown(Objective objective, int tick) =>
        objective.Devices.All(d => d.IsNeutralized) ? ObjectiveState.Completed
        : tick >= objective.EndTick ? ObjectiveState.Failed
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
