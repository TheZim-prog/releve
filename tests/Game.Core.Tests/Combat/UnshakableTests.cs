using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Inébranlable : un adversaire ne peut ni le déplacer de force, ni le déséquilibrer.</summary>
public class UnshakableTests
{
    private static readonly UnitId Colossus = new(1);
    private static readonly UnitId Foe = new(9);

    private static readonly SkillDefinition Shove = Strike with
    {
        Id = "shove", ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2 },
    };

    private static readonly SkillDefinition Trip = Strike with
    {
        Id = "trip", Effects = new[] { new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 1000 } },
    };

    private static readonly SkillDefinition Telekinesis = new()
    {
        Id = "telekinesis", Target = SkillTargeting.Any, Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Place, Distance = 3 },
    };

    private static UnitSetup Unshakable(int x) => Unit(1, team: 0, x, 0, passive: PassiveKind.Unshakable);

    private static UnitSetup Foe9(int x, params SkillDefinition[] skills) => Unit(9, team: 1, x, 0, gauge: Capacity, skills: skills);

    [Fact]
    public void APush_FromAnEnemy_HitsButDoesNotMoveHim()
    {
        var sim = Simulation(new Grid(8, 1), Unshakable(3), Foe9(4, Shove));

        sim.Submit(Use(Foe, "shove", Colossus));
        var events = sim.Tick();

        Assert.Equal(90, sim.Get(1).Hp);
        Assert.Contains(new UnitResisted(1, Colossus), events);
        Assert.Equal(new Cell(3, 0), sim.Get(1).Position);
    }

    [Fact]
    public void HeCannotBeKnockedOffBalance()
    {
        var sim = Simulation(new Grid(8, 1), Unshakable(3), Foe9(4, Trip));

        sim.Submit(Use(Foe, "trip", Colossus));

        Assert.Contains(new UnitResisted(1, Colossus), sim.Tick());
        Assert.Equal(90, sim.Get(1).Hp);
        Assert.False(sim.Get(1).HasStatus(StatusKind.OffBalance));
    }

    [Fact]
    public void AnEnemyTelekinesis_IsRefused_ButAnAllyCanStillMoveHim()
    {
        var sim = Simulation(new Grid(8, 1), Unshakable(3), Foe9(6, Telekinesis), Unit(2, team: 0, 1, 0, gauge: Capacity, skills: new[] { Telekinesis }));

        Assert.Equal(RejectionReason.NoEffect, sim.Check(Use(Foe, "telekinesis", Colossus, new Cell(4, 0))));
        sim.Submit(Use(new UnitId(2), "telekinesis", Colossus, new Cell(4, 0)));
        sim.Tick();

        Assert.Equal(new Cell(4, 0), sim.Get(1).Position);
    }
}
