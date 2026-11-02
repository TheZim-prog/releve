using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class EmpTests
{
    private static readonly UnitId Tech = new(1);

    /// <summary>IEM de test : carré de rayon 1, ennemis et gadgets du lanceur étourdis 1 s (20 ticks) ; robots x2 et 15 dégâts.</summary>
    private static readonly SkillDefinition Emp = new()
    {
        Id = "emp", Weight = SkillWeight.Heavy, Range = 4, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition
        {
            Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Enemies, AlsoHitsOwnSummons = true,
        },
        Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 1000 } },
        VsRobots = new RobotBonusDefinition { StatusDurationPercent = 200, MinDamage = 15, MaxDamage = 15 },
    };

    private static UnitSetup TechAt(int x, int y) => Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { Emp, Strike });

    private static ActivateCommand Pulse(Cell center) => new(Tech, null, new SkillUse("emp", null, center));

    [Fact]
    public void Emp_StunsEnemies_AndTheCastersOwnGadgets_ButNotOtherAllies()
    {
        // Zone autour de (4,1) : un ennemi, un gadget du tech, un gadget d'un autre allié, un héros allié.
        var sim = Simulation(new Grid(8, 3), TechAt(0, 1),
            Unit(2, team: 1, 4, 1), Unit(3, team: 0, 3, 0), Unit(4, team: 0, 5, 0), Unit(5, team: 0, 4, 2), Unit(6, team: 0, 0, 0));
        sim.Get(3).SummonedBy = Tech;
        sim.Get(4).SummonedBy = new UnitId(6);

        sim.Submit(Pulse(new Cell(4, 1)));
        var stunned = sim.Tick().OfType<StatusApplied>().Select(e => e.Unit.Value).ToList();

        Assert.Equal(new[] { 2, 3 }, stunned);
    }

    [Fact]
    public void Emp_IsDevastatingAgainstRobots()
    {
        var sim = Simulation(new Grid(8, 3), TechAt(0, 1), Unit(2, team: 1, 4, 1, isRobot: true), Unit(3, team: 1, 4, 0));

        sim.Submit(Pulse(new Cell(4, 1)));
        var events = sim.Tick();

        Assert.Contains(new UnitDamaged(1, new UnitId(2), 15, 85), events);
        Assert.Contains(new StatusApplied(1, new UnitId(2), StatusKind.Stunned, 1 + 40), events); // 2 x 20 ticks
        Assert.Contains(new StatusApplied(1, new UnitId(3), StatusKind.Stunned, 1 + 20), events);
        Assert.DoesNotContain(events, e => e is UnitDamaged { Unit.Value: 3 });
    }

    [Fact]
    public void OwnRobotGadgets_AreStunnedLikeEnemies_WithoutTheRobotBonus()
    {
        var sim = Simulation(new Grid(8, 3), TechAt(0, 1), Unit(2, team: 1, 4, 1), Unit(3, team: 0, 4, 0, isRobot: true));
        sim.Get(3).SummonedBy = Tech;

        sim.Submit(Pulse(new Cell(4, 1)));
        var events = sim.Tick();

        Assert.Contains(new StatusApplied(1, new UnitId(3), StatusKind.Stunned, 1 + 20), events);
        Assert.DoesNotContain(events, e => e is UnitDamaged);
    }

    [Fact]
    public void ASkillCanDamageOnlyRobots()
    {
        var antiRobot = SkillDefinition.ListFromJson("""
            [{ "id": "disruptor", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0,
               "vsRobots": { "statusDurationPercent": 100, "minDamage": 12, "maxDamage": 12 } }]
            """).Single();
        var sim = Simulation(new Grid(4, 1), Unit(1, team: 0, 1, 0, gauge: Capacity, skills: new[] { antiRobot }),
            Unit(2, team: 1, 0, 0, isRobot: true), Unit(3, team: 1, 2, 0));

        sim.Submit(new ActivateCommand(Tech, null, new SkillUse("disruptor", new UnitId(2))));
        Assert.Contains(new UnitDamaged(1, new UnitId(2), 12, 88), sim.Tick());
    }

    [Fact]
    public void RobotBonusDamage_AddsToTheSkillsOwnDamage()
    {
        var zap = Strike with { VsRobots = new RobotBonusDefinition { StatusDurationPercent = 100, MinDamage = 5, MaxDamage = 5 } };
        var sim = Simulation(new Grid(4, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { zap }), Unit(2, team: 1, 1, 0, isRobot: true));

        sim.Submit(new ActivateCommand(Tech, null, new SkillUse("strike", new UnitId(2))));

        Assert.Contains(new UnitDamaged(1, new UnitId(2), 15, 85), sim.Tick());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)] // l'ennemi est collé au gadget du lanceur : pas d'IEM
    public void BasicAi_DoesNotPulseItsOwnGadgets(bool gadgetNearTheEnemy, bool pulses)
    {
        var ai = Unit(1, team: 0, 0, 1, gauge: Capacity, mobility: 0, skills: new[] { Emp }, behavior: Behaviors.Basic);
        var sim = Simulation(new Grid(8, 3), ai, Unit(2, team: 1, 4, 1), Unit(3, team: 0, gadgetNearTheEnemy ? 4 : 1, 0));
        sim.Get(3).SummonedBy = Tech;

        var used = sim.Tick().Any(e => e is SkillUsed { SkillId: "emp" });

        Assert.Equal(pulses, used);
    }

    [Theory]
    [InlineData("""{ "id": "x", "weight": "heavy", "range": 4, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false, "alsoHitsOwnSummons": true }, "effects": [ { "status": "stunned", "durationMs": 1000 } ] }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "vsRobots": { "statusDurationPercent": 90 } }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "vsRobots": { "statusDurationPercent": 100, "minDamage": 9, "maxDamage": 3 } }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "vsRobots": { "statusDurationPercent": 200 } }""")] // aucun état à allonger
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "effects": [ { "status": "stunned", "durationMs": 1000 } ], "vsRobots": { "statusDurationPercent": 100 } }""")] // ne change rien
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "isMental": true, "effects": [ { "status": "stunned", "durationMs": 1000 } ], "vsRobots": { "statusDurationPercent": 200 } }""")]
    public void InvalidEmpData_IsRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }
}
