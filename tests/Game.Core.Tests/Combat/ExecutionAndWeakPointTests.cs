using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ExecutionAndWeakPointTests
{
    private static readonly UnitId Fighter = new(1);
    private static readonly UnitId Target = new(2);

    /// <summary>Lourde, 40 dégâts, achève sous 30 % de PV.</summary>
    private static readonly SkillDefinition Execution = Strike with
    {
        Id = "execution", Weight = SkillWeight.Heavy, MinDamage = 40, MaxDamage = 40, ExecuteAtOrBelowPercent = 30,
    };

    private static ActivateCommand Use(UnitId attacker, UnitId target, string skill) => new(attacker, null, new SkillUse(skill, target));

    [Fact]
    public void Execution_FinishesATargetAtOrBelowTheThreshold()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Execution }),
            Unit(2, team: 1, 1, 0, hp: 200));
        sim.Get(2).Hp = 60; // 30 % de 200

        sim.Submit(Use(Fighter, Target, "execution"));
        var events = sim.Tick();

        Assert.Equal(new UnitExecuted(1, Fighter, Target), events[1]);
        Assert.Equal(new UnitDamaged(1, Target, 60, 0), events[2]);
        Assert.Equal(new UnitRemoved(1, Target), events[3]);
    }

    [Fact]
    public void Execution_AboveTheThreshold_DealsItsNormalDamage()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Execution }),
            Unit(2, team: 1, 1, 0, hp: 200));
        sim.Get(2).Hp = 61;

        sim.Submit(Use(Fighter, Target, "execution"));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitExecuted);
        Assert.Contains(new UnitDamaged(1, Target, 40, 21), events);
    }

    [Fact]
    public void Execution_DoesNotFinishABoss()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Execution }),
            Unit(2, team: 1, 1, 0, hp: 200, isBoss: true));
        sim.Get(2).Hp = 50;

        sim.Submit(Use(Fighter, Target, "execution"));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitExecuted);
        Assert.Contains(new UnitDamaged(1, Target, 40, 10), events);
    }

    [Fact]
    public void WeakPoint_ExposesTheTarget_EveryoneDealsMoreDamage()
    {
        var sim = Simulation(new Grid(4, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, initiative: Capacity, passive: PassiveKind.WeakPoint),
            Unit(2, team: 1, 1, 0, hp: 1000),
            Unit(3, team: 0, 2, 0, gauge: Capacity));

        sim.Submit(Use(Fighter, Target, "strike"));
        var events = sim.Tick();
        Assert.Contains(new UnitDamaged(1, Target, 10, 990), events); // le premier coup n'est pas encore augmenté
        Assert.Contains(new StatusApplied(1, Target, StatusKind.Exposed, 21), events);

        sim.Submit(Use(new UnitId(3), Target, "strike")); // un allié
        Assert.Contains(new UnitDamaged(2, Target, 12, 978), sim.Tick());

        sim.Submit(Use(Fighter, Target, "strike")); // le combattant lui-même
        Assert.Contains(new UnitDamaged(3, Target, 12, 966), sim.Tick());
    }

    [Fact]
    public void WeakPoint_AlsoMarksOnOpportunityAttacks()
    {
        var sim = Simulation(new Grid(5, 1),
            Unit(1, team: 0, 0, 0, basicAttack: "strike", passive: PassiveKind.WeakPoint),
            Unit(2, team: 1, 1, 0, gauge: Capacity));

        sim.Submit(new ActivateCommand(Target, new Cell(3, 0)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is OpportunityAttack { Attacker.Value: 1 });
        Assert.Contains(events, e => e is StatusApplied { Unit.Value: 2, Status: StatusKind.Exposed });
    }

    [Fact]
    public void Exposed_AlsoIncreasesCollisionDamage()
    {
        var projection = Strike with
        {
            Id = "projection", MinDamage = 0, MaxDamage = 0,
            ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2, CollisionMinDamage = 8, CollisionMaxDamage = 8 },
        };
        var sim = Simulation(Grid.FromRows("..#"),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { projection }),
            Unit(2, team: 1, 1, 0));
        sim.Get(2).SetStatus(StatusKind.Exposed, endTick: 100);

        sim.Submit(Use(Fighter, Target, "projection"));

        Assert.Contains(new UnitDamaged(1, Target, 10, 90), sim.Tick()); // 8 × 125 %
    }

    [Fact]
    public void EmpoweredOnExposed_IsComputedWithoutIntermediateRounding()
    {
        var thirteen = Strike with { MinDamage = 13, MaxDamage = 13 };
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { thirteen }),
            Unit(2, team: 1, 1, 0));
        sim.Get(1).SetStatus(StatusKind.Empowered, endTick: 100);
        sim.Get(2).SetStatus(StatusKind.Exposed, endTick: 100);

        sim.Submit(Use(Fighter, Target, "strike"));

        Assert.Contains(new UnitDamaged(1, Target, 24, 76), sim.Tick()); // 13 × 150 % × 125 % = 24,375
    }

    [Fact]
    public void WeakPoint_DoesNotMarkAlliesInAZone_NorOnAZeroDamageRoll()
    {
        var blast = new SkillDefinition
        {
            Id = "blast", Weight = SkillWeight.Light, Range = 3, MinDamage = 5, MaxDamage = 5, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var graze = Strike with { Id = "graze", MinDamage = 0, MaxDamage = 1 }; // tirage 0 ou 1
        var sim = Simulation(new Grid(6, 3),
            Unit(1, team: 0, 2, 1, gauge: Capacity, initiative: Capacity, skills: new[] { blast, graze }, passive: PassiveKind.WeakPoint),
            Unit(2, team: 1, 3, 1, hp: 1000),
            Unit(3, team: 0, 3, 2));

        sim.Submit(new ActivateCommand(Fighter, null, new SkillUse("blast", null, new Cell(3, 1))));
        var events = sim.Tick();
        Assert.Contains(events, e => e is StatusApplied { Unit.Value: 2, Status: StatusKind.Exposed });
        Assert.DoesNotContain(events, e => e is StatusApplied { Unit.Value: 3 });

        // Les coups à 0 de dégâts ne marquent pas : on vérifie sur une série de tirages (prêt à chaque tick).
        var zeroHits = 0;
        for (var i = 0; i < 20; i++)
        {
            sim.Get(2).ClearStatus(StatusKind.Exposed);
            sim.Submit(new ActivateCommand(Fighter, null, new SkillUse("graze", Target)));
            var hit = sim.Tick();
            if (hit.OfType<UnitDamaged>().SingleOrDefault() is { Amount: 0 })
            {
                zeroHits++;
                Assert.DoesNotContain(hit, e => e is StatusApplied);
            }
        }
        Assert.True(zeroHits > 0);
    }

    [Theory]
    [InlineData("""{ "id": "x", "weight": "heavy", "range": 1, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "executeAtOrBelowPercent": 100 }""")]
    [InlineData("""{ "id": "x", "weight": "heavy", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "executeAtOrBelowPercent": 30, "effects": [ { "status": "stunned", "durationMs": 100 } ] }""")]
    [InlineData("""{ "id": "x", "weight": "heavy", "range": 2, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "executeAtOrBelowPercent": 30, "zone": { "shape": "square", "size": 1, "throughWalls": false } }""")]
    public void InvalidExecutions_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }
}
