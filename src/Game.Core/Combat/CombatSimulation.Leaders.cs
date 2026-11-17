namespace Game.Core.Combat;

// Chefs : quand le chef tombe, ses sbires fuient ou se rendent (docs/design.md 3.5).
public sealed partial class CombatSimulation
{
    /// <summary>
    /// L'unité quitte le combat (vaincue, à terre, enfuie) : si c'est un chef qui tombe pour la première fois, ses sbires
    /// réagiront juste après la source en cours, comme une réaction (un sbire qui se rend ne frappe plus ensuite).
    /// </summary>
    private void QueueMinionsReaction(Unit unit)
    {
        if (unit.MinionsOnFall is null || unit.FellAsLeader)
            return;
        unit.FellAsLeader = true;
        _pendingReactions.Enqueue(() => ReleaseMinions(unit));
    }

    /// <summary>
    /// Le chef est tombé : ses sbires, les unités de son camp d'origine encore sur la carte (ni civils, ni gadgets),
    /// prennent la fuite ou se rendent. Un sbire à terre se rend aussi, ou fuira s'il est relevé.
    /// </summary>
    private void ReleaseMinions(Unit leader)
    {
        var reaction = leader.MinionsOnFall!.Value;
        _events.Add(new LeaderFell(CurrentTick, leader.Id, reaction));
        foreach (var minion in MinionsOf(leader).ToList())
            ReactToFallenLeader(minion, reaction);
    }

    /// <summary>Un sbire dont le chef est tombé se rend (il quitte le combat), ou prend la fuite (à terre : une fois relevé).</summary>
    private void ReactToFallenLeader(Unit minion, MinionReaction reaction)
    {
        if (reaction == MinionReaction.Surrender)
        {
            minion.Surrendered = true;
            Withdraw(minion, new UnitSurrendered(CurrentTick, minion.Id));
        }
        else if (minion.Status == UnitStatus.Downed)
            minion.IsFleeing = true;
        else if (!minion.IsFleeing)
            StartFleeing(minion);
    }

    private IEnumerable<Unit> MinionsOf(Unit leader) =>
        _units.Where(u => u != leader && u.OriginalTeam == leader.OriginalTeam && u.Status != UnitStatus.Removed
                          && !u.IsCivilian && u.SummonedBy is null);
}
