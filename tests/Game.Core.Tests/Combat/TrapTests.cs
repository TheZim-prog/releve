using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class TrapTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId Enemy = new(2);

    private static SkillDefinition Laying(GroundZoneDefinition trap) => new()
    {
        Id = "trap", Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false },
        Ground = trap,
    };

    /// <summary>7 dégâts et immobilisé 1 s (20 ticks).</summary>
    private static readonly SkillDefinition Trap = Laying(new GroundZoneDefinition
    {
        IsTrap = true, Affects = ZoneTargets.Enemies, MinDamage = 7, MaxDamage = 7,
        Effects = new[] { new StatusEffect { Status = StatusKind.Immobilized, DurationMs = 1000 } },
    });

    /// <summary>7 dégâts à tout le monde autour, sans état.</summary>
    private static readonly SkillDefinition ExplosiveTrap = Laying(new GroundZoneDefinition
    {
        IsTrap = true, Affects = ZoneTargets.Enemies, MinDamage = 7, MaxDamage = 7,
        Blast = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
    });

    private static UnitSetup CasterAt(int x, int y, SkillDefinition skill) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    private static UnitSetup Walker(int id, int team, int x, int y) => Unit(id, team, x, y, gauge: Capacity, mobility: 6);

    private static ActivateCommand Lay(int x, int y) => Use(Caster, "trap", cell: new Cell(x, y));

    [Fact]
    public void TheFirstEnemyToEnter_TakesTheDamage_IsImmobilized_AndStopsThere()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Trap), Walker(2, team: 1, 6, 0));
        sim.Submit(Lay(3, 0));
        sim.Tick();
        var trap = Assert.Single(sim.GroundZones);
        Assert.Null(trap.EndTick); // il attend

        sim.TickTimes(50);
        sim.Submit(new ActivateCommand(Enemy, new Cell(1, 0)));

        Assert.Equal(
            new CombatEvent[]
            {
                new UnitMoved(52, Enemy, new Cell(6, 0), new[] { new Cell(5, 0), new Cell(4, 0), new Cell(3, 0) }),
                new TrapTriggered(52, trap.Id, Enemy, Caster, new Cell(3, 0)),
                new GroundZoneEnded(52, trap.Id),
                new UnitDamaged(52, Enemy, 7, 93),
                new StatusApplied(52, Enemy, StatusKind.Immobilized, 72),
                new UnitStopped(52, Enemy, new Cell(3, 0)),
            },
            sim.Tick().Where(e => e is not UnitReady));
        Assert.Empty(sim.GroundZones);
    }

    [Fact]
    public void AnAlly_WalksOverIt_WithoutTriggeringIt()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Trap), Walker(3, team: 0, 1, 0));
        sim.Submit(Lay(3, 0));
        sim.Tick();

        sim.Submit(new ActivateCommand(new UnitId(3), new Cell(5, 0)));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is TrapTriggered);
        Assert.Single(sim.GroundZones);
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void AnEnemyPushedOntoIt_TriggersIt()
    {
        var projection = Strike with { Id = "projection", ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 1 } };
        var sim = Simulation(new Grid(8, 2),
            CasterAt(4, 1, Trap), Unit(3, team: 0, 1, 0, gauge: Capacity, skills: new[] { projection }), Unit(2, team: 1, 2, 0));
        sim.Submit(Lay(3, 0));
        sim.Tick();

        sim.Submit(Use(new UnitId(3), "projection", Enemy));
        var events = sim.Tick();

        Assert.Contains(events, e => e is TrapTriggered { Unit: var unit } && unit == Enemy);
        Assert.Equal(100 - 10 - 7, sim.Get(2).Hp);
        Assert.True(sim.Get(2).HasStatus(StatusKind.Immobilized));
    }

    [Fact]
    public void AnEnemyPushedAcrossIt_StopsOnIt()
    {
        var projection = Strike with { Id = "projection", ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 3 } };
        var sim = Simulation(new Grid(8, 2),
            CasterAt(4, 1, Trap), Unit(3, team: 0, 1, 0, gauge: Capacity, skills: new[] { projection }), Unit(2, team: 1, 2, 0));
        sim.Submit(Lay(4, 0));
        sim.Tick();

        sim.Submit(Use(new UnitId(3), "projection", Enemy));
        var events = sim.Tick();

        // Poussé de trois cases, il s'arrête sur le piège, à la deuxième.
        Assert.Equal(new Cell(4, 0), sim.Get(2).Position);
        Assert.Contains(events, e => e is TrapTriggered);
        Assert.DoesNotContain(events, e => e is UnitCollided);
    }

    [Fact]
    public void AnEnemyChargingAcrossIt_StopsOnIt_AndStrikesNoOne()
    {
        var charge = Strike with { Id = "charge", Range = 6, SelfMove = SelfMoveType.Charge };
        var sim = Simulation(new Grid(8, 2),
            CasterAt(1, 1, Trap), Unit(3, team: 0, 0, 0), Unit(2, team: 1, 6, 0, gauge: Capacity, skills: new[] { charge }));
        sim.Submit(Lay(3, 0));
        sim.Tick();

        sim.Submit(Use(Enemy, "charge", new UnitId(3)));
        var events = sim.Tick();

        Assert.Equal(new Cell(3, 0), sim.Get(2).Position);
        Assert.Contains(events, e => e is TrapTriggered);
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void AHero_HasOnlyOneTrap_ANewOneReplacesIt()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Trap));
        sim.Submit(Lay(3, 0));
        sim.TickTimes(10);

        sim.Submit(Lay(2, 0));
        sim.Tick();

        Assert.Equal(new[] { new Cell(2, 0) }, Assert.Single(sim.GroundZones).Cells);
    }

    [Fact]
    public void ATrap_NeedsACellWithoutAUnit()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Trap), Unit(2, team: 1, 2, 0));

        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Lay(2, 0)));
        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Lay(0, 0)));
        Assert.Null(sim.Check(Lay(1, 0)));
    }

    [Fact]
    public void AnExplosiveTrap_HurtsEveryoneAround_AlliesIncluded_WithoutImmobilizing()
    {
        // L'ennemi 2 marche jusqu'au piège (en traversant son équipier 4) ; l'allié 3 du poseur est à côté.
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, ExplosiveTrap), Walker(2, team: 1, 6, 0), Unit(3, team: 0, 2, 0), Unit(4, team: 1, 4, 0), Unit(5, team: 1, 7, 0));
        sim.Submit(Lay(3, 0));
        sim.Tick();

        sim.Submit(new ActivateCommand(Enemy, new Cell(3, 0)));
        var events = sim.Tick();

        Assert.Equal(new[] { 2, 3, 4 }, events.OfType<UnitDamaged>().Select(e => e.Unit.Value).Order());
        Assert.False(sim.Get(2).HasStatus(StatusKind.Immobilized));
        Assert.Equal(100, sim.Get(5).Hp);
    }

    [Fact]
    public void ADome_ProtectsFromATrap_LikeFromAHit()
    {
        var dome = new SkillDefinition
        {
            Id = "dome", Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 2, ThroughWalls = false }, CreatesDome = true,
        };
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Trap), Walker(2, team: 1, 6, 0), Unit(4, team: 1, 4, 0, gauge: Capacity, skills: new[] { dome }));
        sim.Submit(Lay(3, 0));
        sim.Submit(Use(new UnitId(4), "dome", cell: new Cell(4, 0)));
        sim.Tick();

        sim.Submit(new ActivateCommand(Enemy, new Cell(3, 0)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is TrapTriggered);
        Assert.Contains(events, e => e is HitBlocked);
        Assert.Contains(events, e => e is DomeEnded);
        Assert.Equal(100, sim.Get(2).Hp);
        Assert.False(sim.Get(2).HasStatus(StatusKind.Immobilized));
    }

    [Theory]
    // Un piège : pour les ennemis, une case, posé tout de suite, avec un effet ; l'explosion fait des dégâts.
    [InlineData(""" "zone": { "shape": "square", "size": 0, "throughWalls": false }, "ground": { "isTrap": true, "minDamage": 1, "maxDamage": 2 } """)]
    [InlineData(""" "zone": { "shape": "square", "size": 1, "throughWalls": false }, "ground": { "isTrap": true, "affects": "enemies", "minDamage": 1, "maxDamage": 2 } """)]
    [InlineData(""" "chargeMs": 500, "zone": { "shape": "square", "size": 0, "throughWalls": false }, "ground": { "isTrap": true, "affects": "enemies", "minDamage": 1, "maxDamage": 2 } """)]
    [InlineData(""" "zone": { "shape": "square", "size": 0, "throughWalls": false }, "ground": { "isTrap": true, "affects": "enemies" } """)]
    [InlineData(""" "zone": { "shape": "square", "size": 0, "throughWalls": false }, "ground": { "isTrap": true, "affects": "enemies", "effects": [ { "status": "immobilized", "durationMs": 1000 } ], "blast": { "shape": "square", "size": 1, "throughWalls": false } } """)]
    [InlineData(""" "zone": { "shape": "square", "size": 0, "throughWalls": false }, "ground": { "isTrap": true, "affects": "enemies", "minDamage": 1, "maxDamage": 2, "blocksSight": true } """)]
    public void InvalidTraps_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson(
            $$"""[ { "id": "x", "weight": "light", "cooldownMs": 0, "range": 3, "minDamage": 0, "maxDamage": 0, {{fields}} } ]"""));
    }
}
