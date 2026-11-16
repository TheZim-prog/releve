using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Démesure : des coups plus forts, et un décor qui s'use plus vite.</summary>
public class ExcessTests
{
    private static readonly UnitId Shooter = new(1);
    private static readonly UnitId Target = new(2);

    /// <summary>Tir de 20 dégâts fixes, portée 6.</summary>
    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 6, MinDamage = 20, MaxDamage = 20 };

    private static CombatSimulation Firing(string row, PassiveKind? passive) =>
        Simulation(Grid.FromRows(row), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Shot }, passive: passive),
            Unit(2, team: 1, 4, 0));

    [Fact]
    public void Excess_HitsHarder()
    {
        var sim = Firing(".......", PassiveKind.Excess);

        sim.Submit(Use(Shooter, "shot", Target));

        Assert.Contains(new UnitDamaged(1, Target, 30, 70), sim.Tick());
    }

    [Fact]
    public void Excess_WearsTheCoverTwiceAsFast_WhichStillAbsorbsItsShare()
    {
        var sim = Firing("...o...", PassiveKind.Excess);

        sim.Submit(Use(Shooter, "shot", Target));
        var events = sim.Tick();

        // 30 dégâts : l'obstacle en absorbe 15, et en perd 30 sur ses 40 PV.
        Assert.Contains(new HitCovered(1, Target, new Cell(3, 0), 15, 10), events);
        Assert.Contains(new UnitDamaged(1, Target, 15, 85), events);
    }

    [Fact]
    public void Excess_CannotWearTheCoverBelowZero()
    {
        var sim = Firing("...o...", PassiveKind.Excess);
        sim.Submit(Use(Shooter, "shot", Target));
        sim.Tick();

        sim.TickTimes(5); // de nouveau prêt

        sim.Submit(Use(Shooter, "shot", Target));
        var events = sim.Tick();

        Assert.Contains(events, e => e is HitCovered { Absorbed: 10, CoverHp: 0 });
        Assert.Contains(events, e => e is CoverDestroyed);
    }
}
