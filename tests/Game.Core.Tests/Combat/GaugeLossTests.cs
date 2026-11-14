using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Perte de jauge (Brouillage, Contretemps) : la cible perd une part de sa jauge, en % de la capacité (100 ici).
/// Dans ces combats la cible a une initiative de 0, pour que sa jauge ne bouge que par les compétences.
/// </summary>
public class GaugeLossTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Target = new(2);

    /// <summary>Retire 30 % de jauge, sans dégâts, jusqu'à 5 cases.</summary>
    private static readonly SkillDefinition Jam = Strike with { Id = "jam", Range = 5, MinDamage = 0, MaxDamage = 0, GaugeLossPercent = 30 };

    private static CombatSimulation Combat(int targetGauge, SkillDefinition? skill = null, bool boss = false, bool robot = false,
        IReadOnlyList<SkillDefinition>? targetSkills = null) => Simulation(new Grid(6, 1),
        Unit(1, team: 0, 0, 0, hp: 1000, gauge: Capacity, skills: new[] { skill ?? Jam }),
        Unit(2, team: 1, 1, 0, gauge: targetGauge, initiative: 0, isBoss: boss, isRobot: robot, skills: targetSkills));

    private static IReadOnlyList<CombatEvent> UseOnTarget(CombatSimulation sim, string skill = "jam")
    {
        sim.Submit(Use(Hero, skill, Target));
        return sim.Tick();
    }

    [Fact]
    public void AUnitThatLosesGauge_IsNoLongerReady()
    {
        var sim = Combat(targetGauge: Capacity);
        Assert.True(sim.Get(2).IsReady);

        var events = UseOnTarget(sim);

        Assert.Contains(new GaugeLost(1, Target, 30, 70), events);
        Assert.Equal((70, false), (sim.Get(2).Gauge, sim.Get(2).IsReady));
    }

    [Fact]
    public void AUnit_CannotLoseMoreGaugeThanItHas()
    {
        Assert.Contains(new GaugeLost(1, Target, 20, 0), UseOnTarget(Combat(targetGauge: 20)));
        Assert.DoesNotContain(UseOnTarget(Combat(targetGauge: 0)), e => e is GaugeLost);
    }

    [Fact]
    public void ABoss_LosesHalfAsMuch()
    {
        Assert.Contains(new GaugeLost(1, Target, 15, 85), UseOnTarget(Combat(targetGauge: Capacity, boss: true)));
    }

    [Fact]
    public void ARobot_CanLoseMore()
    {
        var jam = Jam with { VsRobots = new RobotBonusDefinition { StatusDurationPercent = 100, GaugeLossPercent = 30 } };

        Assert.Contains(new GaugeLost(1, Target, 60, 40), UseOnTarget(Combat(targetGauge: Capacity, skill: jam, robot: true)));
        Assert.Contains(new GaugeLost(1, Target, 30, 70), UseOnTarget(Combat(targetGauge: Capacity, skill: jam)));
    }

    [Fact]
    public void AStrikeThatInterruptsCharges_StopsTheAttackInProgress()
    {
        var charged = Smash with { Id = "charged", ChargeMs = 1000, CooldownMs = 0 };
        var offbeat = Jam with { InterruptsCharge = true };
        var sim = Combat(targetGauge: Capacity, skill: offbeat, targetSkills: new[] { charged });
        sim.Submit(Use(Target, "charged", Hero));
        sim.Tick();

        sim.Submit(Use(Hero, "jam", Target));
        var events = sim.Tick();

        Assert.Contains(new ChargeInterrupted(2, Target, "charged"), events);
        Assert.DoesNotContain(events, e => e is GaugeLost); // sa jauge était déjà à 0 pendant la charge
        Assert.DoesNotContain(sim.TickTimes(25), e => e is UnitDamaged);
    }

    [Fact]
    public void WithoutThatFlag_AChargingUnitIsLeftAlone()
    {
        var charged = Smash with { Id = "charged", ChargeMs = 1000, CooldownMs = 0 };
        var sim = Combat(targetGauge: Capacity, targetSkills: new[] { charged });
        sim.Submit(Use(Target, "charged", Hero));
        sim.Tick();

        sim.Submit(Use(Hero, "jam", Target));

        Assert.DoesNotContain(sim.Tick(), e => e is GaugeLost or ChargeInterrupted);
    }

    [Fact]
    public void InAZoneThatHitsEveryone_OnlyEnemiesLoseGauge()
    {
        var wave = Jam with { Range = 0, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false } };
        var sim = Simulation(new Grid(3, 2),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { wave }),
            Unit(2, team: 1, 1, 0, gauge: 50, initiative: 0),
            Unit(3, team: 0, 0, 1, gauge: 50, initiative: 0));

        sim.Submit(Use(Hero, "jam", cell: new Cell(0, 0)));

        Assert.Equal(new[] { Target }, sim.Tick().OfType<GaugeLost>().Select(e => e.Unit));
    }

    [Fact]
    public void TheData_AreChecked()
    {
        Jam.Validate(); // une compétence qui ne fait que retirer de la jauge a bien un effet
        (Jam with { GaugeLossPercent = 0, InterruptsCharge = true }).Validate();
        Assert.Throws<ArgumentException>(() => (Jam with { GaugeLossPercent = 101 }).Validate());
        Assert.Throws<ArgumentException>(() => (Jam with { Target = SkillTargeting.Ally }).Validate());
        // Contre les robots seulement : c'est un effet aussi.
        (Jam with { GaugeLossPercent = 0, VsRobots = new RobotBonusDefinition { StatusDurationPercent = 100, GaugeLossPercent = 20 } }).Validate();
    }
}
