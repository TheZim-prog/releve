using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class BuffTests
{
    private static readonly UnitId Hero = new(1);

    private static SkillDefinition SelfBuff(string id, StatusKind status, int durationMs) => new()
    {
        Id = id, Target = SkillTargeting.Self, Weight = SkillWeight.Heavy, Range = 0,
        MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Effects = new[] { new StatusEffect { Status = status, DurationMs = durationMs } },
    };

    /// <summary>Accéléré 100 ticks (5 s).</summary>
    private static readonly SkillDefinition Hyperspeed = SelfBuff("hyperspeed", StatusKind.Hasted, 5000);

    /// <summary>Surpuissant 100 ticks (5 s).</summary>
    private static readonly SkillDefinition EnergyForm = SelfBuff("energy-form", StatusKind.Empowered, 5000);

    private static ActivateCommand Use(string skill, UnitId? target = null) => new(Hero, null, new SkillUse(skill, target));

    [Fact]
    public void Hyperspeed_IsUsedOnSelf_AndTriplesTheGaugeSpeed()
    {
        var sim = Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, initiative: 10, gauge: Capacity, skills: new[] { Hyperspeed }));

        sim.Submit(Use("hyperspeed"));
        var events = sim.Tick();

        Assert.Equal(new SkillUsed(1, Hero, "hyperspeed", Hero), events[0]);
        Assert.Equal(new StatusApplied(1, Hero, StatusKind.Hasted, 101), events[1]);
        Assert.Equal(30, sim.Get(1).Gauge); // lourde : repart de 0, puis +10 × 300 %
        sim.Tick();
        Assert.Equal(60, sim.Get(1).Gauge);
    }

    [Fact]
    public void Haste_AndOffBalance_Combine()
    {
        var trip = Strike with { Id = "trip", MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 5000 } } };
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, initiative: 10, gauge: Capacity, skills: new[] { Hyperspeed }),
            Unit(2, team: 1, 1, 0, gauge: Capacity, skills: new[] { trip }));

        sim.Submit(Use("hyperspeed"));
        sim.Submit(new ActivateCommand(new UnitId(2), null, new SkillUse("trip", Hero)));
        sim.Tick();

        Assert.Equal(15, sim.Get(1).Gauge); // 10 × 300 % × 50 %
    }

    [Fact]
    public void EnergyForm_IncreasesAllDamageDealt_UntilItEnds()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity, skills: new[] { EnergyForm, Strike }),
            Unit(2, team: 1, 1, 0, hp: 1000));

        sim.Submit(Use("energy-form"));
        sim.Tick();
        sim.Submit(Use("strike", new UnitId(2)));
        Assert.Contains(new UnitDamaged(2, new UnitId(2), 15, 985), sim.Tick()); // 10 × 150 %

        sim.TickTimes(97); // ticks 3 à 99
        sim.Submit(Use("strike", new UnitId(2)));
        Assert.Contains(sim.Tick(), e => e is UnitDamaged { Tick: 100, Amount: 15 }); // dernier tick renforcé

        sim.Submit(Use("strike", new UnitId(2)));
        Assert.Contains(sim.Tick(), e => e is UnitDamaged { Tick: 101, Amount: 10 }); // l'état prend fin au tick 101
    }

    [Fact]
    public void EmpoweredChargedAttack_KeepsItsBonus_EvenIfTheStatusEndsBeforeRelease()
    {
        var shortForm = SelfBuff("short-form", StatusKind.Empowered, 100); // 2 ticks
        var aimed = Strike with { Id = "aimed", Weight = SkillWeight.Heavy, ChargeMs = 500 };
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity, skills: new[] { shortForm, aimed }),
            Unit(2, team: 1, 1, 0, hp: 1000));

        sim.Submit(Use("short-form"));
        sim.Tick();
        sim.Submit(Use("aimed", new UnitId(2))); // lancée au tick 2, renforcée ; l'état finit au tick 3
        var events = sim.TickTimes(11);

        Assert.Contains(new StatusEnded(3, Hero, StatusKind.Empowered), events);
        Assert.Contains(new UnitDamaged(12, new UnitId(2), 15, 985), events);
    }

    [Fact]
    public void Haste_EndingWhileOffBalance_KeepsTheFillExact()
    {
        // Initiative 7 : accéléré + déséquilibré = 10,5 par tick, puis déséquilibré seul = 3,5 par tick.
        var trip = Strike with { Id = "trip", MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 5000 } } };
        var haste = SelfBuff("haste", StatusKind.Hasted, 100); // 2 ticks
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, initiative: 7, gauge: Capacity, skills: new[] { haste }),
            Unit(2, team: 1, 1, 0, gauge: Capacity, skills: new[] { trip }));

        sim.Submit(Use("haste"));
        sim.Submit(new ActivateCommand(new UnitId(2), null, new SkillUse("trip", Hero)));
        sim.TickTimes(4); // ticks 1-2 : 10,5 × 2 = 21 ; ticks 3-4 : 3,5 × 2 = 7

        Assert.Equal(28, sim.Get(1).Gauge);
    }

    [Fact]
    public void SelfSkill_TakesNoTargetButItself()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Hyperspeed }),
            Unit(2, team: 0, 1, 0));

        var onAlly = Use("hyperspeed", new UnitId(2));
        var onCell = new ActivateCommand(Hero, null, new SkillUse("hyperspeed", null, new Cell(1, 0)));
        sim.Submit(onAlly);
        sim.Submit(onCell);
        sim.Submit(Use("hyperspeed", Hero));

        var events = sim.Tick();
        Assert.Contains(new CommandRejected(1, onAlly, RejectionReason.InvalidTarget), events);
        Assert.Contains(new CommandRejected(1, onCell, RejectionReason.InvalidTarget), events);
        Assert.Contains(new SkillUsed(1, Hero, "hyperspeed", Hero), events);
    }

    [Theory]
    [InlineData("""{ "id": "x", "target": "self", "weight": "heavy", "range": 0, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0 }""")]
    [InlineData("""{ "id": "x", "target": "self", "weight": "heavy", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "effects": [ { "status": "hasted", "durationMs": 100 } ] }""")]
    [InlineData("""{ "id": "x", "target": "self", "weight": "heavy", "range": 0, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "effects": [ { "status": "stunned", "durationMs": 100 } ] }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "effects": [ { "status": "empowered", "durationMs": 100 } ] }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "effects": [ { "status": "offBalance", "durationMs": 100 } ] }""")]
    public void BeneficialAndHarmfulStatuses_GoOnTheRightSide(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }

    [Fact]
    public void AllySkill_MayGiveABeneficialStatus()
    {
        var skill = SkillDefinition.ListFromJson("""
            [{ "id": "rally", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0,
               "effects": [ { "status": "hasted", "durationMs": 1000 } ] }]
            """).Single();

        Assert.Equal(StatusKind.Hasted, skill.Effects.Single().Status);
    }
}
