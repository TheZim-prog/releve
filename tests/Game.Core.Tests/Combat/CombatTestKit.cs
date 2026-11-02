using Game.Core.Combat;
using Game.Core.Grids;

namespace Game.Core.Tests.Combat;

/// <summary>Raccourcis pour monter des combats de test, avec des valeurs rondes faciles à suivre.</summary>
internal static class CombatTestKit
{
    public const int Capacity = 100;

    /// <summary>Jauge pleine à 100, reprise à 50 % après un déplacement seul.</summary>
    public static readonly CombatRules Rules = new()
    {
        TicksPerSecond = 20,
        GaugeCapacity = Capacity,
        MoveOnlyRestartPercent = 50,
    };

    public static UnitSetup Unit(int id, int team, int x, int y, int initiative = 10, int mobility = 3, int gauge = 0) =>
        new(new UnitId(id), team, new Cell(x, y), initiative, mobility, gauge);

    public static CombatSimulation Simulation(Grid grid, params UnitSetup[] units) =>
        new(Rules, grid, units);

    public static IReadOnlyList<CombatEvent> TickTimes(this CombatSimulation simulation, int count)
    {
        var events = new List<CombatEvent>();
        for (var i = 0; i < count; i++)
            events.AddRange(simulation.Tick());
        return events;
    }

    public static Unit Get(this CombatSimulation simulation, int id) =>
        simulation.FindUnit(new UnitId(id)) ?? throw new InvalidOperationException($"No unit {id}.");
}
