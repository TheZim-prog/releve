using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Invocations au-delà de la tourelle et de la balise : leurres, drone mobile, Astre, Surcadençage, Double déploiement.</summary>
public class SummonVariantTests
{
    private static readonly UnitId Caster = new(1);

    private static UnitDefinition Gadget(string id, string behavior, int mobility = 0, int hp = 20, int initiative = 10,
        IReadOnlyList<SkillDefinition>? skills = null, LureDefinition? lure = null, DefeatBlastDefinition? blast = null,
        int? maxActivations = null) => new()
    {
        Id = id, Name = id, MaxHp = hp, Initiative = initiative, Mobility = mobility, OnDefeat = DefeatOutcome.Removed,
        BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = behavior, Skills = skills ?? Array.Empty<SkillDefinition>(),
        Lure = lure, DefeatBlast = blast, MaxActivations = maxActivations,
    };

    private static SkillDefinition Summoning(UnitDefinition gadget, int range = 3, bool leftBehind = false) => new()
    {
        Id = "summon", Weight = SkillWeight.Light, Range = range, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Summon = gadget, SummonLeftBehind = leftBehind,
    };

    private static readonly UnitDefinition Decoy =
        Gadget("decoy", Behaviors.Inert, hp: 1, lure: new LureDefinition { Range = 2, Actions = 1 });

    private static UnitSetup CasterWith(SkillDefinition skill, int x = 0, int y = 0, PassiveKind? passive = null) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill }, passive: passive);

    private static UnitSetup AiEnemy(int id, int x, int y) =>
        Unit(id, team: 1, x, y, skills: new[] { Strike }, behavior: Behaviors.Basic);

    private static UnitId Placed(IEnumerable<CombatEvent> events) => events.OfType<UnitSummoned>().Single().Unit;

    [Fact]
    public void ALure_TauntsTheAiEnemiesAroundIt_WhenItAppears()
    {
        var sim = Simulation(new Grid(9, 3), CasterWith(Summoning(Decoy)), AiEnemy(2, 4, 1), AiEnemy(3, 8, 1));

        sim.Submit(Use(Caster, "summon", cell: new Cell(3, 1)));
        var events = sim.Tick();

        var decoy = Placed(events);
        Assert.Equal(new[] { new UnitTaunted(1, new UnitId(2), decoy, 1) }, events.OfType<UnitTaunted>());
    }

    [Fact]
    public void ARiggedDecoy_BlowsUpWhenDestroyed_HurtingTheEnemiesAtItsContact()
    {
        var rigged = Decoy with { DefeatBlast = new DefeatBlastDefinition { MinDamage = 7, MaxDamage = 7 } };
        var sim = Simulation(new Grid(9, 3), CasterWith(Summoning(rigged)),
            Unit(2, team: 1, 3, 1, gauge: Capacity), Unit(3, team: 1, 3, 0), Unit(4, team: 0, 1, 1), Unit(5, team: 1, 6, 1));
        sim.Submit(Use(Caster, "summon", cell: new Cell(2, 1)));
        var decoy = Placed(sim.Tick());

        sim.Submit(Use(new UnitId(2), "strike", decoy));
        var events = sim.Tick();

        Assert.Contains(new UnitBlewUp(2, decoy, new Cell(2, 1)), events);
        Assert.Equal((93, 93), (sim.Get(2).Hp, sim.Get(3).Hp));
        Assert.Equal((100, 100), (sim.Get(4).Hp, sim.Get(5).Hp)); // pas l'allié, ni l'ennemi trop loin
    }

    [Fact]
    public void ASummonLeftBehind_AppearsWhereTheCasterWas_WhileHeWalksAway()
    {
        var sim = Simulation(new Grid(9, 3),
            CasterWith(Summoning(Decoy, range: 4, leftBehind: true), 1, 1), Unit(2, team: 1, 0, 1, basicAttack: "strike"));

        sim.Submit(Use(Caster, "summon", cell: new Cell(5, 1)));
        var events = sim.Tick();

        Assert.Equal(new Cell(5, 1), sim.Get(1).Position);
        Assert.Equal(new Cell(1, 1), events.OfType<UnitSummoned>().Single().Position);
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void ASummonLeftBehind_NeedsACellWithinItsSteps_AndAFreeCaster()
    {
        var skill = Summoning(Decoy, range: 4, leftBehind: true);
        var sim = Simulation(Grid.FromRows(".........", "..#######", "........."), CasterWith(skill, 1, 1));

        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Use(Caster, "summon", cell: new Cell(6, 1))));
        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Use(Caster, "summon", cell: new Cell(1, 1))));
        Assert.Null(sim.Check(Use(Caster, "summon", cell: new Cell(4, 0))));
        Assert.True(skill.MovesCaster);
    }

    [Fact]
    public void TheSpeedsterRunningAway_IsStoppedByATrapOnHisWay()
    {
        var trap = new SkillDefinition
        {
            Id = "trap", Weight = SkillWeight.Light, Range = 9, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false },
            Ground = new GroundZoneDefinition
            {
                IsTrap = true, Affects = ZoneTargets.Enemies, MinDamage = 1, MaxDamage = 1,
                Effects = new[] { new StatusEffect { Status = StatusKind.Immobilized, DurationMs = 1000 } },
            },
        };
        var sim = Simulation(new Grid(9, 1),
            CasterWith(Summoning(Decoy, range: 4, leftBehind: true), 1, 0), Unit(2, team: 1, 8, 0, gauge: Capacity, skills: new[] { trap }));
        sim.Submit(Use(new UnitId(2), "trap", cell: new Cell(3, 0)));
        sim.Tick();

        sim.Submit(Use(Caster, "summon", cell: new Cell(5, 0)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is TrapTriggered);
        Assert.Equal(new Cell(3, 0), sim.Get(1).Position);
        Assert.Equal(new Cell(1, 0), events.OfType<UnitSummoned>().Single().Position);
    }

    [Fact]
    public void TheBlast_ComesAfterTheAttackThatDestroyedTheDecoy()
    {
        var rigged = Decoy with { DefeatBlast = new DefeatBlastDefinition { MinDamage = 7, MaxDamage = 7 } };
        var sim = Simulation(new Grid(9, 3), CasterWith(Summoning(rigged)), Unit(2, team: 1, 3, 1, gauge: Capacity));
        sim.Submit(Use(Caster, "summon", cell: new Cell(2, 1)));
        var decoy = Placed(sim.Tick());

        sim.Submit(Use(new UnitId(2), "strike", decoy));
        var events = sim.Tick().ToList();

        Assert.True(events.FindIndex(e => e is UnitRemoved) < events.FindIndex(e => e is UnitBlewUp));
    }

    [Fact]
    public void ADrone_WalksToTheEnemy_AndAttacksOnItsOwn()
    {
        var shot = Strike with { Id = "drone-shot" };
        var drone = Gadget("drone", Behaviors.Basic, mobility: 3, initiative: 50, skills: new[] { shot });
        var sim = Simulation(new Grid(9, 1), CasterWith(Summoning(drone, range: 1)), Unit(2, team: 1, 7, 0));

        sim.Submit(Use(Caster, "summon", cell: new Cell(1, 0)));
        sim.TickTimes(30);

        Assert.True(sim.Get(2).Hp < 100);
        Assert.NotEqual(new Cell(1, 0), sim.Units.Single(u => u.SummonedBy == Caster).Position);
    }

    [Fact]
    public void AStar_ExplodesOnlyWithAnEnemyAround_OnEnemiesOnly_ThreeTimes()
    {
        var burst = Strike with
        {
            Id = "burst", Range = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        };
        var star = Gadget("star", Behaviors.Beacon, initiative: 100, skills: new[] { burst }, maxActivations: 3);
        var alone = Simulation(new Grid(9, 3), CasterWith(Summoning(star, range: 4)), Unit(2, team: 1, 8, 2));
        var crowded = Simulation(new Grid(9, 3), CasterWith(Summoning(star, range: 4)), Unit(2, team: 1, 4, 2), Unit(3, team: 0, 3, 2));

        alone.Submit(Use(Caster, "summon", cell: new Cell(3, 1)));
        alone.TickTimes(20);
        crowded.Submit(Use(Caster, "summon", cell: new Cell(3, 1)));
        var events = crowded.TickTimes(20);

        Assert.DoesNotContain(alone.EventLog, e => e is SkillUsed { SkillId: "burst" });
        Assert.Equal(70, crowded.Get(2).Hp); // trois explosions
        Assert.Equal(100, crowded.Get(3).Hp);
        Assert.Contains(events, e => e is UnitRemoved);
    }

    [Fact]
    public void Overclocking_MakesTheCastersGadgetsFillTheirGaugeFaster()
    {
        var turret = Gadget("turret", Behaviors.Basic, initiative: 10, skills: new[] { Strike });
        UnitId Placed(CombatSimulation sim) => sim.Units.Single(u => u.SummonedBy == Caster).Id;
        var normal = Simulation(new Grid(9, 1), CasterWith(Summoning(turret)));
        var overclocked = Simulation(new Grid(9, 1), CasterWith(Summoning(turret), passive: PassiveKind.Overclocking));

        foreach (var sim in new[] { normal, overclocked })
        {
            sim.Submit(Use(Caster, "summon", cell: new Cell(2, 0)));
            sim.TickTimes(4);
        }

        Assert.Equal(40, normal.FindUnit(Placed(normal))!.Gauge);
        Assert.Equal(80, overclocked.FindUnit(Placed(overclocked))!.Gauge);
    }

    [Fact]
    public void DoubleDeployment_KeepsTwoGadgetsOfAKind_TheThirdReplacesTheOldest()
    {
        var turret = Gadget("turret", Behaviors.Inert);
        var sim = Simulation(new Grid(9, 1), CasterWith(Summoning(turret, range: 5), passive: PassiveKind.DoubleDeployment));
        int[] Turrets() => sim.Units.Where(u => u.SummonedBy == Caster && u.Status == UnitStatus.Active).Select(u => u.Position.X).ToArray();

        foreach (var x in new[] { 1, 2, 3 })
        {
            sim.Submit(Use(Caster, "summon", cell: new Cell(x, 0)));
            sim.Tick();
            sim.TickTimes(10);
        }

        Assert.Equal(new[] { 2, 3 }, Turrets());
        // Le plus ancien, celui qui sera remplacé, peut céder sa case au nouveau.
        Assert.Null(sim.Check(Use(Caster, "summon", cell: new Cell(2, 0)), ignoreTiming: true));
        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Use(Caster, "summon", cell: new Cell(3, 0)), ignoreTiming: true));
    }

    [Theory]
    // Un leurre qui tombe à terre ; une explosion sans dégâts ; un leurre sans portée ; un « laissé derrière » sans invocation.
    [InlineData(""" "summon": { "id": "g", "name": "G", "maxHp": 1, "initiative": 1, "mobility": 0, "onDefeat": "downed", "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "inert", "defeatBlast": { "minDamage": 1, "maxDamage": 2 }, "skills": [] } """)]
    [InlineData(""" "summon": { "id": "g", "name": "G", "maxHp": 1, "initiative": 1, "mobility": 0, "onDefeat": "removed", "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "inert", "defeatBlast": { "minDamage": 0, "maxDamage": 0 }, "skills": [] } """)]
    [InlineData(""" "summon": { "id": "g", "name": "G", "maxHp": 1, "initiative": 1, "mobility": 0, "onDefeat": "removed", "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "inert", "lure": { "range": 0, "actions": 1 }, "skills": [] } """)]
    [InlineData(""" "effects": [ { "status": "stunned", "durationMs": 500 } ], "summonLeftBehind": true """)]
    public void InvalidSummons_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson(
            $$"""[ { "id": "x", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, {{fields}} } ]"""));
    }
}
