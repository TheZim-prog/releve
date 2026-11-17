using Game.Core.Combat;
using Game.Core.Grids;

namespace Game.Core.Objectives;

/// <summary>
/// Les objectifs d'un combat sont-ils réalisables avec sa carte, ses unités et ses renforts ? Une règle par type
/// d'objectif ; les marques des unités (poursuivie, protégée) n'existent qu'avec l'objectif qui les utilise.
/// </summary>
internal static class MissionValidator
{
    public static void Validate(CombatSetup setup)
    {
        ObjectiveDefinition.ValidateList(setup.Objectives);
        ValidateRescue(setup);
        var exits = ValidatePursuit(setup);
        ValidateDevices(setup, exits);
        ValidateProtection(setup);
        ValidateWaves(setup);
    }

    /// <summary>
    /// Les renforts arrivent au moins 1 ms après le début, sur une case praticable qui n'est pas celle d'un objet, jauge
    /// dans la capacité ; ils ne sont ni chefs, ni poursuivis, ni protégés (ces rôles sont fixés au début du combat).
    /// </summary>
    private static void ValidateWaves(CombatSetup setup)
    {
        var devices = setup.Objectives.SelectMany(o => o.Devices ?? Array.Empty<DeviceDefinition>()).Select(d => d.Cell).ToHashSet();
        foreach (var wave in setup.Waves)
        {
            if (wave.AtMs < 1)
                throw new ArgumentException("A wave arrives at least 1 ms after the start.");
            foreach (var unit in wave.Units)
            {
                if (!setup.Grid.IsWalkable(unit.Position) || devices.Contains(unit.Position))
                    throw new ArgumentException($"A reinforcement arrives on a blocked cell {unit.Position}.");
                if (unit.MinionsOnFall is not null || unit.IsPursued || unit.IsProtected)
                    throw new ArgumentException($"A reinforcement ('{unit.Definition.Id}') cannot be a leader, pursued or protected.");
                if (unit.InitialGauge < 0 || unit.InitialGauge > setup.Rules.GaugeCapacity)
                    throw new ArgumentException($"A reinforcement ('{unit.Definition.Id}') has a gauge outside [0, {setup.Rules.GaugeCapacity}].");
            }
        }
    }

    /// <summary>Un sauvetage ne demande pas plus de civils qu'il n'y en a.</summary>
    private static void ValidateRescue(CombatSetup setup)
    {
        var civilians = setup.Units.Count(u => u.Definition.IsCivilian);
        if (setup.Objectives.FirstOrDefault(o => o.Minimum > civilians) is { } rescue)
            throw new ArgumentException($"A rescue objective asks for {rescue.Minimum} civilians, the combat only has {civilians}.");
    }

    /// <summary>
    /// Une poursuite au plus, avec un seul vilain poursuivi, ennemi et pas civil, qui ne part pas d'une sortie ; ses sorties
    /// sont des cases praticables. Renvoie les sorties.
    /// </summary>
    private static IReadOnlySet<Cell> ValidatePursuit(CombatSetup setup)
    {
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
        var exits = pursuits.SelectMany(o => o.Exits!).ToHashSet();
        foreach (var exit in exits)
        {
            if (!setup.Grid.Contains(exit) || setup.Grid.IsBlocked(exit))
                throw new ArgumentException($"The pursuit exit {exit} is not a free cell of the map.");
            if (pursued.Any(u => u.Position == exit))
                throw new ArgumentException($"The pursued unit starts on the exit {exit}: it would escape at once.");
        }
        return exits;
    }

    /// <summary>
    /// Les objets d'une course contre la montre se posent sur des cases libres (ni décor, ni unité, ni sortie, ni un autre
    /// objet), avec une case voisine d'où les atteindre.
    /// </summary>
    private static void ValidateDevices(CombatSetup setup, IReadOnlySet<Cell> exits)
    {
        var taken = new HashSet<Cell>();
        foreach (var device in setup.Objectives.SelectMany(o => o.Devices ?? Array.Empty<DeviceDefinition>()))
        {
            var cell = device.Cell;
            if (!setup.Grid.Contains(cell) || setup.Grid.IsBlocked(cell) || setup.Units.Any(u => u.Position == cell)
                || exits.Contains(cell) || !taken.Add(cell))
                throw new ArgumentException($"The device on {cell} is not on a free cell (scenery, unit, exit or another device).");
            // La carte du scénario n'a pas encore les objets : la case est du sol, ses voisines praticables s'y rendent.
            if (!setup.Grid.Neighbors(cell).Any())
                throw new ArgumentException($"The device on {cell} cannot be reached: no free cell next to it.");
        }
    }

    /// <summary>
    /// Une protection au plus, avec au moins une personne protégée : un allié du camp des joueurs commandé par le jeu (pas
    /// un héros ni un civil). Jusqu'à la fin des vagues : au moins une vague amène des ennemis.
    /// </summary>
    private static void ValidateProtection(CombatSetup setup)
    {
        var protections = setup.Objectives.Where(o => o.Kind == ObjectiveKind.Protect).ToList();
        var guarded = setup.Units.Where(u => u.IsProtected).ToList();
        if (protections.Count > 1)
            throw new ArgumentException("A combat has at most one protection objective.");
        if ((protections.Count == 0) != (guarded.Count == 0))
            throw new ArgumentException(protections.Count == 0
                ? "A unit is protected, but no protection objective asks for it."
                : "A protection objective needs at least one protected unit.");
        if (guarded.FirstOrDefault(u => u.Team != CombatSimulation.PlayerTeam || u.Definition.Behavior is null || u.Definition.IsCivilian)
            is { } wrong)
            throw new ArgumentException($"Unit {wrong.Id} cannot be protected: only an ally led by the game can (not a hero, an enemy or a civilian).");
        if (protections.Any(o => o.Until == ProtectUntil.Waves) && !setup.Waves.Any(w => w.BringsEnemies))
            throw new ArgumentException("A protection until the end of the waves needs waves that bring enemies.");
    }
}
