using Game.Core.Combat;
using Game.Core.Grids;

namespace Game.Core.Tests.Combat;

/// <summary>Raccourcis pour monter des combats de test, avec des valeurs rondes faciles à suivre.</summary>
internal static class CombatTestKit
{
    public const int Capacity = 100;

    /// <summary>
    /// Jauge pleine à 100 ; reprise à 50 % (déplacement seul), 40 % (léger), 0 % (lourd) ;
    /// déséquilibré = remplissage à 50 %. 20 ticks/s : 1 tick = 50 ms.
    /// </summary>
    public static readonly CombatRules Rules = new()
    {
        TicksPerSecond = 20,
        GaugeCapacity = Capacity,
        MoveOnlyRestartPercent = 50,
        LightActionRestartPercent = 40,
        HeavyActionRestartPercent = 0,
        OffBalanceFillPercent = 50,
        HastedFillPercent = 300,
        EmpoweredDamagePercent = 150,
        ExposedDamageTakenPercent = 125,
        WeakPointExposedMs = 1000, // 20 ticks
        GripDurationPercent = 150,
        IntentionThresholdPercent = 50,
        ReviveHpPercent = 25,
        ReviveWeight = SkillWeight.Light,
    };

    /// <summary>Attaque légère, 10 dégâts fixes, sans recharge.</summary>
    public static readonly SkillDefinition Strike = new()
    {
        Id = "strike", Weight = SkillWeight.Light, Range = 1, MinDamage = 10, MaxDamage = 10, CooldownMs = 0,
    };

    /// <summary>Attaque lourde, 30 dégâts fixes, 5 s de recharge (100 ticks).</summary>
    public static readonly SkillDefinition Smash = new()
    {
        Id = "smash", Weight = SkillWeight.Heavy, Range = 1, MinDamage = 30, MaxDamage = 30, CooldownMs = 5000,
    };

    /// <summary>
    /// Unité de test. Sans <paramref name="basicAttack"/>, elle ne fait pas d'attaque d'opportunité :
    /// les tests qui ne portent pas dessus n'en subissent pas par surprise.
    /// </summary>
    public static UnitSetup Unit(
        int id, int team, int x, int y,
        int initiative = 10, int mobility = 3, int gauge = 0, int hp = 100,
        DefeatOutcome onDefeat = DefeatOutcome.Removed, IReadOnlyList<SkillDefinition>? skills = null,
        string? basicAttack = null, int opportunityAttacks = 1, string? behavior = null,
        PassiveKind? passive = null, bool isBoss = false, bool isRobot = false)
    {
        var definition = new UnitDefinition
        {
            Id = $"unit-{id}",
            Name = $"Unit {id}",
            MaxHp = hp,
            Initiative = initiative,
            Mobility = mobility,
            OnDefeat = onDefeat,
            BasicAttack = basicAttack,
            OpportunityAttacksPerCycle = opportunityAttacks,
            Behavior = behavior,
            Skills = skills ?? new[] { Strike, Smash },
            Passive = passive,
            IsBoss = isBoss,
            IsRobot = isRobot,
        };
        return definition.ToSetup(new UnitId(id), team, new Cell(x, y), gauge);
    }

    public static CombatSetup Setup(Grid grid, params UnitSetup[] units) => new(Rules, grid, units, Seed: 1);

    public static CombatSimulation Simulation(Grid grid, params UnitSetup[] units) => new(Setup(grid, units));

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
