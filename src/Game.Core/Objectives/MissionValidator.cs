using Game.Core.Combat;

namespace Game.Core.Objectives;

/// <summary>
/// Les objectifs d'un combat sont-ils réalisables avec sa carte et ses unités ? Un sauvetage ne demande pas plus de
/// civils qu'il n'y en a ; une poursuite (une seule) a ses sorties sur des cases praticables, et un seul vilain
/// poursuivi, ennemi et pas civil, qui ne part pas d'une sortie ; un vilain poursuivi n'existe qu'avec une poursuite.
/// </summary>
internal static class MissionValidator
{
    public static void Validate(CombatSetup setup)
    {
        ObjectiveDefinition.ValidateList(setup.Objectives);

        var civilians = setup.Units.Count(u => u.Definition.IsCivilian);
        if (setup.Objectives.FirstOrDefault(o => o.Minimum > civilians) is { } rescue)
            throw new ArgumentException($"A rescue objective asks for {rescue.Minimum} civilians, the combat only has {civilians}.");

        var pursuits = setup.Objectives.Where(o => o.Kind == ObjectiveKind.Pursuit).ToList();
        var pursued = setup.Units.Where(u => u.IsPursued).ToList();
        if (pursuits.Count > 1)
            throw new ArgumentException("A combat has at most one pursuit objective.");
        if (pursuits.Count != pursued.Count)
            throw new ArgumentException(pursuits.Count == 0
                ? "A unit is pursued, but no pursuit objective says where it runs."
                : $"A pursuit objective needs exactly one pursued unit (found {pursued.Count}).");
        if (pursued.FirstOrDefault(u => u.Team == CombatSimulation.PlayerTeam || u.Definition.IsCivilian) is { } wrong)
            throw new ArgumentException($"Unit {wrong.Id} cannot be pursued: only an enemy can, not a hero or a civilian.");
        foreach (var exit in pursuits.SelectMany(o => o.Exits!))
        {
            if (!setup.Grid.Contains(exit) || setup.Grid.IsBlocked(exit))
                throw new ArgumentException($"The pursuit exit {exit} is not a free cell of the map.");
            if (pursued.Any(u => u.Position == exit))
                throw new ArgumentException($"The pursued unit starts on the exit {exit}: it would escape at once.");
        }
    }
}
