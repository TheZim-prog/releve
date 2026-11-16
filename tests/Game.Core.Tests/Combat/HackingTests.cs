using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Piratage et Virus : un robot est commandé ou rendu confus, un autre ennemi seulement désarmé ou ralenti.</summary>
public class HackingTests
{
    private static readonly UnitId Tech = new(1);
    private static readonly UnitId Target = new(2);

    private static SkillDefinition Hack(StatusKind robotStatus, StatusKind otherStatus) => new()
    {
        Id = "hack", Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Effects = new[] { new StatusEffect { Status = otherStatus, DurationMs = 1000 } },
        VsRobots = new RobotBonusDefinition
        {
            StatusDurationPercent = 100, ReplacesEffects = true,
            Effects = new[] { new StatusEffect { Status = robotStatus, DurationMs = 2000 } },
        },
    };

    private static CombatSimulation Against(SkillDefinition hack, bool robot, bool boss = false) =>
        Simulation(new Grid(6, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { hack }),
            Unit(2, team: 1, 3, 0, isRobot: robot, isBoss: boss));

    [Fact]
    public void Hacking_ARobot_PutsItUnderTheTechsControl_WithoutDisarmingIt()
    {
        var sim = Against(Hack(StatusKind.Dominated, StatusKind.Disarmed), robot: true);

        sim.Submit(Use(Tech, "hack", Target));
        sim.Tick();

        Assert.Equal(0, sim.Get(2).Team);
        Assert.Equal(Tech, sim.Get(2).DominatedBy);
        Assert.False(sim.Get(2).HasStatus(StatusKind.Disarmed));
    }

    [Fact]
    public void Hacking_AnotherEnemy_OnlyDisarmsIt()
    {
        var sim = Against(Hack(StatusKind.Dominated, StatusKind.Disarmed), robot: false);

        sim.Submit(Use(Tech, "hack", Target));
        sim.Tick();

        Assert.Equal(1, sim.Get(2).Team);
        Assert.True(sim.Get(2).HasStatus(StatusKind.Disarmed));
    }

    [Fact]
    public void Virus_ConfusesARobot_AndSlowsAnotherEnemy()
    {
        var onARobot = Against(Hack(StatusKind.Confused, StatusKind.OffBalance), robot: true);
        var onAThug = Against(Hack(StatusKind.Confused, StatusKind.OffBalance), robot: false);
        onARobot.Submit(Use(Tech, "hack", Target));
        onAThug.Submit(Use(Tech, "hack", Target));
        onARobot.Tick();
        onAThug.Tick();

        Assert.True(onARobot.Get(2).HasStatus(StatusKind.Confused));
        Assert.False(onARobot.Get(2).HasStatus(StatusKind.OffBalance));
        Assert.True(onAThug.Get(2).HasStatus(StatusKind.OffBalance));
    }

    [Fact]
    public void ARobotHackedByADominatedTech_ObeysTheTechsMaster()
    {
        var dominate = new SkillDefinition
        {
            Id = "dominate", Weight = SkillWeight.Light, Range = 6, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, IsMental = true,
            Effects = new[] { new StatusEffect { Status = StatusKind.Dominated, DurationMs = 5000 } },
        };
        var sim = Simulation(new Grid(6, 2),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Hack(StatusKind.Dominated, StatusKind.Disarmed) }),
            Unit(2, team: 0, 3, 0, isRobot: true),
            Unit(9, team: 1, 0, 1, gauge: Capacity, skills: new[] { dominate }));
        sim.Submit(Use(new UnitId(9), "dominate", Tech));
        sim.Tick();

        sim.Submit(Use(Tech, "hack", Target)); // le robot des héros, adversaire du tech dominé
        sim.Tick();

        Assert.Equal((1, new UnitId(9)), (sim.Get(2).Team, sim.Get(2).DominatedBy));
    }

    [Fact]
    public void RobotEffectsThatReplaceTheSkills_NeedStatusesToReplace_AndAreNotLengthened()
    {
        var hack = Hack(StatusKind.Dominated, StatusKind.Disarmed);
        hack.Validate();
        Assert.Throws<ArgumentException>(() => (hack with { Effects = Array.Empty<StatusEffect>() }).Validate());
        Assert.Throws<ArgumentException>(() => (hack with { VsRobots = hack.VsRobots! with { StatusDurationPercent = 200 } }).Validate());
    }

    [Fact]
    public void ARobotBoss_CannotBeHacked()
    {
        var sim = Against(Hack(StatusKind.Dominated, StatusKind.Disarmed), robot: true, boss: true);

        Assert.Equal(RejectionReason.NoEffect, sim.Check(Use(Tech, "hack", Target)));
    }
}
