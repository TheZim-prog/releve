using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class EnemyAiTests
{
    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 5 };

    /// <summary>Tir appuyé de test : ligne chargée de 6 cases, plus forte que le tir.</summary>
    private static readonly SkillDefinition Burst = Strike with
    {
        Id = "burst", Weight = SkillWeight.Heavy, MinDamage = 20, MaxDamage = 20, CooldownMs = 10000, ChargeMs = 1000,
        Zone = new ZoneDefinition { Shape = ZoneShape.Line, Size = 6, ThroughWalls = false },
    };

    private static UnitSetup Gunner(int x, int y, int mobility = 3, params SkillDefinition[] skills) =>
        Unit(2, team: 1, x, y, mobility: mobility, gauge: Capacity, behavior: Behaviors.Gunner,
            skills: skills.Length == 0 ? new[] { Shot } : skills);

    private static UnitSetup Fleeing(UnitSetup unit, int percent) =>
        unit with { Definition = unit.Definition with { FleeAtHpPercent = percent } };

    [Fact]
    public void Gunner_ShootsFromTheFarthestCellThatKeepsItsTarget()
    {
        var hero = new Cell(3, 1);
        var sim = Simulation(new Grid(12, 3), Unit(1, team: 0, hero.X, hero.Y), Gunner(5, 1));

        var events = sim.Tick();

        Assert.Equal(5, sim.Get(2).Position.DistanceTo(hero)); // il recule jusqu'à la portée de son tir
        Assert.Contains(new SkillUsed(1, new UnitId(2), "shot", new UnitId(1)), events);
    }

    [Fact]
    public void Gunner_AlreadyAtMaxRange_DoesNotMove()
    {
        var sim = Simulation(new Grid(12, 3), Unit(1, team: 0, 3, 1), Gunner(8, 1));

        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitMoved);
        Assert.Contains(new SkillUsed(1, new UnitId(2), "shot", new UnitId(1)), events);
    }

    [Fact]
    public void Gunner_UsesItsAimedBurst_WhenALineHitsItsTarget()
    {
        var sim = Simulation(new Grid(12, 3), Unit(1, team: 0, 3, 1), Gunner(5, 1, 3, Shot, Burst));

        var events = sim.Tick();

        Assert.Equal("burst", events.OfType<ChargeStarted>().Single().SkillId);
        Assert.Equal(new Cell(8, 1), sim.Get(2).Position); // le plus loin possible sur la ligne
    }

    [Fact]
    public void Gunner_DoesNotBurstThroughAnAlly()
    {
        // Toutes les lignes vers le héros passent par l'allié (4,0) : il tire normalement.
        var sim = Simulation(new Grid(12, 1), Unit(1, team: 0, 2, 0), Unit(3, team: 1, 4, 0), Gunner(6, 0, 1, Shot, Burst));

        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is ChargeStarted);
        Assert.Contains(new SkillUsed(1, new UnitId(2), "shot", new UnitId(1)), events);
        Assert.Equal(new Cell(7, 0), sim.Get(2).Position);
    }

    [Fact]
    public void BasicAi_StillIgnoresLineZones()
    {
        var basic = Unit(2, team: 1, 5, 1, gauge: Capacity, behavior: Behaviors.Basic, skills: new[] { Shot, Burst });
        var sim = Simulation(new Grid(12, 3), Unit(1, team: 0, 3, 1), basic);

        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is ChargeStarted);
        Assert.Contains(events, e => e is SkillUsed { SkillId: "shot" });
    }

    [Fact]
    public void UnderItsThreshold_TheUnitFlees_ThenRunsToTheEdgeAndEscapes()
    {
        // 40 PV, fuite sous 80 % (32 PV) : un coup de 10 suffit. Du centre d'une grille 7x7, le bord est à 3 pas.
        var enemy = Fleeing(Unit(2, team: 1, 3, 3, hp: 40, behavior: Behaviors.Basic), 80);
        var sim = Simulation(new Grid(7, 7), Unit(1, team: 0, 4, 3, gauge: Capacity), enemy);

        sim.Submit(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))));
        Assert.Contains(new UnitFleeing(1, new UnitId(2)), sim.Tick());
        Assert.True(sim.Get(2).IsFleeing);

        var events = sim.TickTimes(20);

        var moved = events.OfType<UnitMoved>().Single(e => e.Unit.Value == 2);
        Assert.True(sim.Grid.IsEdge(moved.Path[^1]));
        Assert.Contains(new UnitEscaped(moved.Tick, new UnitId(2)), events);
        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 2 }); // il ne fait que courir
        Assert.DoesNotContain(events, e => e is UnitRemoved); // enfui, pas vaincu
        var fled = sim.Get(2);
        Assert.True(fled.Escaped);
        Assert.Equal(UnitStatus.Removed, fled.Status);
        Assert.Null(sim.UnitAt(fled.Position));
    }

    [Fact]
    public void AFleeingUnitOnTheEdge_EscapesAtItsActivation_WithoutMoving()
    {
        var enemy = Fleeing(Unit(2, team: 1, 0, 3, hp: 40, behavior: Behaviors.Basic), 80);
        var sim = Simulation(new Grid(7, 7), Unit(1, team: 0, 1, 3, gauge: Capacity), enemy);

        sim.Submit(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))));
        sim.Tick();
        var events = sim.TickTimes(20);

        Assert.DoesNotContain(events, e => e is UnitMoved { Unit.Value: 2 } or SkillUsed { Unit.Value: 2 });
        Assert.Contains(events, e => e is UnitEscaped { Unit.Value: 2 });
    }

    [Fact]
    public void AFleeingUnitFarFromTheEdge_KeepsRunningOverSeveralActivations()
    {
        // Grille 11x11, du centre (5,5) avec 2 de mobilité : deux activations sans atteindre le bord, puis la fuite.
        var enemy = Fleeing(Unit(2, team: 1, 5, 5, hp: 40, mobility: 2, behavior: Behaviors.Basic), 80);
        var sim = Simulation(new Grid(11, 11), Unit(1, team: 0, 6, 5, gauge: Capacity), enemy);

        sim.Submit(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))));
        sim.Tick();
        var events = sim.TickTimes(100);

        Assert.Equal(3, events.Count(e => e is UnitMoved { Unit.Value: 2 }));
        Assert.Contains(events, e => e is UnitEscaped { Unit.Value: 2 });
        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 2 });
    }

    [Fact]
    public void Fleeing_AbandonsTheChargeInProgress()
    {
        var slam = Strike with { Id = "slam", Weight = SkillWeight.Heavy, ChargeMs = 1000 };
        var enemy = Fleeing(Unit(2, team: 1, 3, 3, hp: 40, gauge: Capacity, behavior: Behaviors.Basic, skills: new[] { slam }), 80);
        var sim = Simulation(new Grid(7, 7), Unit(1, team: 0, 4, 3, gauge: Capacity - 10), enemy);

        Assert.Contains(sim.Tick(), e => e is ChargeStarted { Unit.Value: 2 });
        sim.Submit(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))));
        var events = sim.Tick();

        Assert.Contains(new ChargeInterrupted(2, new UnitId(2), "slam"), events);
        Assert.Null(sim.Get(2).Charge);
    }

    [Fact]
    public void TheThreshold_IsStrictlyBelow()
    {
        // 40 PV, seuil 75 % = 30 PV : à 30 tout juste, il se bat encore.
        var enemy = Fleeing(Unit(2, team: 1, 3, 3, hp: 40, behavior: Behaviors.Basic), 75);
        var sim = Simulation(new Grid(7, 7), Unit(1, team: 0, 4, 3, gauge: Capacity), enemy);

        sim.Submit(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))));
        sim.Tick();

        Assert.Equal(30, sim.Get(2).Hp);
        Assert.False(sim.Get(2).IsFleeing);
    }

    [Fact]
    public void AFleeingUnit_MakesNoOpportunityAttack()
    {
        var enemy = Fleeing(Unit(2, team: 1, 3, 3, hp: 40, basicAttack: "strike", behavior: Behaviors.Basic), 80);
        var sim = Simulation(new Grid(7, 7), Unit(1, team: 0, 4, 3, gauge: Capacity), enemy);
        sim.Submit(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))));
        sim.Tick();
        while (!sim.Get(1).IsGaugeFull)
            sim.Tick();
        Assert.Equal(new Cell(3, 3), sim.Get(2).Position); // pas encore parti

        sim.Submit(new ActivateCommand(new UnitId(1), new Cell(6, 3)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 1 });
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void StartingToFleeDuringItsOwnMove_CancelsTheAction()
    {
        // Il part frapper le héros fragile (0,3) ; l'attaque d'opportunité du héros (4,3) le fait passer en fuite.
        var enemy = Fleeing(Unit(2, team: 1, 3, 3, hp: 40, gauge: Capacity, behavior: Behaviors.Basic), 80);
        var sim = Simulation(new Grid(7, 7),
            Unit(1, team: 0, 4, 3, basicAttack: "strike"), Unit(3, team: 0, 0, 3, hp: 50), enemy);

        var events = sim.Tick();

        Assert.Contains(events, e => e is OpportunityAttack);
        Assert.Contains(new UnitFleeing(1, new UnitId(2)), events);
        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 2 });
        Assert.DoesNotContain(events, e => e is SkillUsed { Unit.Value: 2 });
        Assert.Equal(50, sim.Get(3).Hp);
        Assert.Equal(Capacity / 2 + 10, sim.Get(2).Gauge); // reprise d'un déplacement seul, puis le remplissage du tick
    }

    [Fact]
    public void AConfusedFleeingUnit_StillActsConfused()
    {
        var enemy = Fleeing(Unit(2, team: 1, 3, 3, hp: 40, behavior: Behaviors.Basic), 80);
        var sim = Simulation(new Grid(7, 7), Unit(1, team: 0, 4, 3, gauge: Capacity), enemy);
        sim.Submit(new ActivateCommand(new UnitId(1), null, new SkillUse("strike", new UnitId(2))));
        sim.Tick();

        var fleeing = sim.Get(2);
        fleeing.SetStatus(StatusKind.Confused, int.MaxValue);

        Assert.Same(Behaviors.ConfusedInstance, sim.ControllerOf(fleeing));
    }

    [Theory]
    [InlineData(0, "basic")]
    [InlineData(100, "basic")]
    [InlineData(30, null)]
    public void InvalidFleeThresholds_AreRejected(int percent, string? behavior)
    {
        var unit = Unit(1, team: 1, 0, 0, behavior: behavior).Definition with { FleeAtHpPercent = percent };

        Assert.Throws<ArgumentException>(unit.Validate);
    }
}
