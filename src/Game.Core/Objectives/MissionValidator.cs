using Game.Core.Combat;
using Game.Core.Grids;

namespace Game.Core.Objectives;

/// <summary>
/// Les objectifs d'un combat sont-ils réalisables avec sa carte et ses unités ? Un sauvetage ne demande pas plus de
/// civils qu'il n'y en a ; une poursuite (une seule) a ses sorties sur des cases praticables, et un seul vilain
/// poursuivi, ennemi et pas civil, qui ne part pas d'une sortie ; un vilain poursuivi n'existe qu'avec une poursuite ;
/// les objets d'une course contre la montre sont sur des cases libres.
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

        // Les objets se posent sur des cases libres : ni décor, ni unité, ni sortie, ni un autre objet.
        var exits = pursuits.SelectMany(o => o.Exits!).ToHashSet();
        var taken = new HashSet<Cell>();
        foreach (var device in setup.Objectives.SelectMany(o => o.Devices ?? Array.Empty<DeviceDefinition>()))
        {
            var cell = device.Cell;
            if (!setup.Grid.Contains(cell) || setup.Grid.IsBlocked(cell) || setup.Units.Any(u => u.Position == cell)
                || exits.Contains(cell) || !taken.Add(cell))
                throw new ArgumentException($"The device on {cell} is not on a free cell (scenery, unit, exit or another device).");
            // Une case voisine d'où l'atteindre au contact (la carte du scénario n'a pas encore les objets : la case est du sol).
            if (!setup.Grid.Neighbors(cell).Any())
                throw new ArgumentException($"The device on {cell} cannot be reached: no free cell next to it.");
        }
    }
}
