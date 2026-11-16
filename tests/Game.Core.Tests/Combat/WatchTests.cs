using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Surveillances : Tir de couverture (une zone) et Protection rapprochée (un allié).</summary>
public class WatchTests
{
    private static readonly UnitId Tech = new(1);
    private static readonly UnitId Enemy = new(2);

    private static WatchDefinition Shot(int range = 7, StatusKind? status = null) => new()
    {
        ShotRange = range, MinDamage = 10, MaxDamage = 10,
        Effects = status is { } s ? new[] { new StatusEffect { Status = s, DurationMs = 1000 } } : Array.Empty<StatusEffect>(),
    };

    /// <summary>Surveille le carré de rayon 1 autour de la case visée (à 6 cases au plus).</summary>
    private static SkillDefinition Cover(WatchDefinition watch) => new()
    {
        Id = "cover", Weight = SkillWeight.Light, Range = 6, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        Watch = watch,
    };

    private static SkillDefinition Protect => new()
    {
        Id = "protect", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 6, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, Watch = Shot(),
    };

    /// <summary>Le tech (1) en (0,0) surveille autour de (4,1) ; les autres unités sont données.</summary>
    private static CombatSimulation Covering(SkillDefinition cover, params UnitSetup[] others)
    {
        var sim = Simulation(new Grid(8, 4), new[] { Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { cover }) }.Concat(others).ToArray());
        sim.Submit(Use(Tech, "cover", cell: new Cell(4, 1)));
        sim.Tick();
        return sim;
    }

    private static UnitSetup Walker(int id, int x, int y) => Unit(id, team: 1, x, y, gauge: Capacity, mobility: 6);

    [Fact]
    public void TheFirstEnemyToMoveInTheZone_TakesTheShot_AndTheWatchEnds()
    {
        var sim = Covering(Cover(Shot()), Walker(2, 7, 2), Walker(3, 7, 3));

        sim.Submit(new ActivateCommand(Enemy, new Cell(2, 2)));
        sim.Submit(new ActivateCommand(new UnitId(3), new Cell(2, 3)));
        var events = sim.Tick();

        Assert.Equal(new[] { new WatchFired(2, Tech, Enemy) }, events.OfType<WatchFired>());
        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Equal(new Cell(2, 2), sim.Get(2).Position); // il poursuit son chemin
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void AnEnemyActingInTheZone_IsShotBeforeItActs_AndAStunningShotCancelsTheAction()
    {
        var sim = Covering(Cover(Shot(status: StatusKind.Stunned)),
            Unit(2, team: 1, 4, 1, gauge: Capacity), Unit(3, team: 0, 4, 2));

        sim.Submit(Use(Enemy, "strike", new UnitId(3)));
        sim.Tick();

        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void AWatch_DoesNotFireOutOfRange_AndEndsAtTheTechsNextAction()
    {
        var sim = Covering(Cover(Shot(range: 2)), Walker(2, 7, 1));

        sim.Submit(new ActivateCommand(Enemy, new Cell(5, 1)));
        sim.Tick();
        Assert.Equal(100, sim.Get(2).Hp); // trop loin du tech
        Assert.Single(sim.EventLog.OfType<WatchSet>());

        sim.TickTimes(10);
        sim.Submit(new ActivateCommand(Tech, new Cell(1, 0)));
        Assert.Contains(sim.Tick(), e => e is WatchEnded);
    }

    [Fact]
    public void CloseProtection_ShootsTheFirstEnemyWhoAttacksTheAlly_AfterTheAttack()
    {
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Protect }), Unit(3, team: 0, 4, 1),
            Unit(2, team: 1, 5, 1, gauge: Capacity), Unit(4, team: 1, 3, 2, gauge: Capacity));
        sim.Submit(Use(Tech, "protect", new UnitId(3)));
        sim.Tick();

        sim.Submit(Use(Enemy, "strike", new UnitId(3)));
        sim.Submit(Use(new UnitId(4), "strike", new UnitId(3)));
        var events = sim.Tick();

        Assert.Equal(80, sim.Get(3).Hp);
        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Equal(100, sim.Get(4).Hp);
        Assert.Equal(new[] { new WatchFired(2, Tech, Enemy) }, events.OfType<WatchFired>());
    }

    [Fact]
    public void AWatch_EndsWhenItsOwnerFalls()
    {
        var sim = Covering(Cover(Shot()), Unit(2, team: 1, 0, 1, gauge: Capacity, skills: new[] { Strike with { MinDamage = 200, MaxDamage = 200 } }));

        sim.Submit(Use(Enemy, "strike", Tech));
        var events = sim.Tick();

        Assert.Contains(new WatchEnded(2, Tech), events);
        Assert.Empty(sim.Watches);
    }

    [Fact]
    public void AChargeThroughTheZone_IsShotToo()
    {
        var charge = Strike with { Id = "charge", Range = 7, SelfMove = SelfMoveType.Charge };
        var sim = Covering(Cover(Shot()), Unit(2, team: 1, 7, 1, gauge: Capacity, skills: new[] { charge }), Unit(3, team: 0, 1, 1));

        sim.Submit(Use(Enemy, "charge", new UnitId(3)));
        var events = sim.Tick();

        Assert.Single(events.OfType<WatchFired>());
        Assert.Equal(90, sim.Get(2).Hp);
    }

    [Fact]
    public void AWatch_ReplaysIdentically()
    {
        var setup = Setup(new Grid(8, 4), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Cover(Shot()) }), Walker(2, 7, 2));
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(Tech, "cover", cell: new Cell(4, 1)));
        sim.Tick();
        sim.Submit(new ActivateCommand(Enemy, new Cell(2, 2)));
        sim.TickTimes(10);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, 11);

        Assert.Equal(sim.Checksum(), replay.Checksum());
        Assert.Equal(sim.EventLog, replay.EventLog);
    }

    [Theory]
    // Avec des dégâts directs, sur un ennemi, sans tir, en ligne, chargée.
    [InlineData(""" "range": 6, "minDamage": 5, "maxDamage": 5, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "watch": { "shotRange": 7, "minDamage": 10, "maxDamage": 10 } """)]
    [InlineData(""" "range": 6, "minDamage": 0, "maxDamage": 0, "effects": [ { "status": "stunned", "durationMs": 500 } ], "watch": { "shotRange": 7, "minDamage": 10, "maxDamage": 10 } """)]
    [InlineData(""" "range": 6, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "watch": { "shotRange": 7, "minDamage": 0, "maxDamage": 0 } """)]
    [InlineData(""" "range": 6, "minDamage": 0, "maxDamage": 0, "zone": { "shape": "line", "size": 3, "throughWalls": false }, "watch": { "shotRange": 7, "minDamage": 10, "maxDamage": 10 } """)]
    [InlineData(""" "range": 6, "minDamage": 0, "maxDamage": 0, "chargeMs": 500, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "watch": { "shotRange": 7, "minDamage": 10, "maxDamage": 10 } """)]
    public void InvalidWatches_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() =>
            SkillDefinition.ListFromJson($$"""[ { "id": "x", "weight": "light", "cooldownMs": 0, {{fields}} } ]"""));
    }
}
