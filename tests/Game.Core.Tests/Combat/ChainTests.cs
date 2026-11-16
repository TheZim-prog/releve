using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ChainTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId First = new(2);

    /// <summary>Court jusqu'à la première cible (4 pas), puis jusqu'à 3 cibles, à 3 cases au plus l'une de l'autre ; 10 dégâts.</summary>
    private static readonly SkillDefinition ChainStrike = Strike with
    {
        Id = "chain", Range = 4, SelfMove = SelfMoveType.Dash, Chain = new ChainDefinition { MaxTargets = 3, BounceRange = 3 },
    };

    /// <summary>Éclair à 6 cases, qui saute sur 2 cases au plus, jusqu'à 3 cibles, et étourdit.</summary>
    private static readonly SkillDefinition ChainLightning = Strike with
    {
        Id = "chain", Range = 6, Chain = new ChainDefinition { MaxTargets = 3, BounceRange = 2 },
        Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 500 } },
    };

    private static UnitSetup CasterAt(int x, int y, SkillDefinition skill) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    private static UnitSetup Enemy(int id, int x, int y) => Unit(id, team: 1, x, y, basicAttack: "strike");

    private static int[] Damaged(IEnumerable<CombatEvent> events) =>
        events.OfType<UnitDamaged>().Select(e => e.Unit.Value).ToArray();

    /// <summary>Quatre ennemis en ligne sur la rangée du haut, deux cases entre chacun.</summary>
    private static CombatSimulation FourInARow(SkillDefinition skill) =>
        Simulation(new Grid(10, 2), CasterAt(0, 0, skill), Enemy(2, 3, 0), Enemy(3, 5, 0), Enemy(4, 7, 0), Enemy(5, 9, 0));

    [Fact]
    public void AChainStrike_RunsFromEnemyToEnemy_UpToItsLimit_AndEndsNextToTheLast()
    {
        var sim = FourInARow(ChainStrike);

        sim.Submit(Use(Caster, "chain", First));
        var events = sim.Tick();

        Assert.Equal(new[] { 2, 3, 4 }, Damaged(events)); // le quatrième est à portée de rebond, mais la chaîne s'arrête à 3
        Assert.True(sim.Get(1).Position.IsAdjacentTo(sim.Get(4).Position));
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void ThePreview_ShowsTheWholeChain()
    {
        var sim = FourInARow(ChainStrike);

        var preview = sim.PreviewAction(Caster, new Cell(0, 0), new SkillUse("chain", First));

        Assert.Equal(new[] { 2, 3, 4 }, preview.Chain!.Select(u => u.Value));
        Assert.Equal(3, preview.Cells!.Count); // les cases où il s'arrête pour frapper
        Assert.Equal(First, preview.Target);
        Assert.True(preview.Destination!.Value.IsAdjacentTo(new Cell(7, 0)));
    }

    [Fact]
    public void AChain_SkipsAllies_AndStopsWhenNoEnemyIsCloseEnough()
    {
        var sim = Simulation(new Grid(10, 2), CasterAt(0, 0, ChainStrike), Enemy(2, 3, 0), Unit(3, team: 0, 5, 0), Enemy(4, 8, 0));

        sim.Submit(Use(Caster, "chain", First));

        Assert.Equal(new[] { 2 }, Damaged(sim.Tick()));
    }

    [Fact]
    public void Ricochet_HitsTheChain_ThenComesBackToItsStart()
    {
        var sim = FourInARow(ChainStrike with { SelfMoveOptions = new SelfMoveOptions { ReturnsToStart = true } });

        sim.Submit(Use(Caster, "chain", First));
        var events = sim.Tick();

        Assert.Equal(new[] { 2, 3, 4 }, Damaged(events));
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
    }

    [Fact]
    public void AChainOnFoot_StopsWhereTheGroundHoldsTheCaster_AndDoesNotComeBack()
    {
        // Un sol qui immobilise sous la case d'où il frappe la deuxième cible.
        var frost = new SkillDefinition
        {
            Id = "frost", Weight = SkillWeight.Light, Range = 9, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false },
            Ground = new GroundZoneDefinition { DurationMs = 2000, Effects = new[] { new StatusEffect { Status = StatusKind.Immobilized, DurationMs = 1000 } } },
        };
        var ricochet = ChainStrike with { SelfMoveOptions = new SelfMoveOptions { ReturnsToStart = true } };
        var sim = FourInARow(ricochet);
        var stops = sim.PreviewAction(Caster, new Cell(0, 0), new SkillUse("chain", First)).Cells!;
        var layer = Simulation(new Grid(10, 2), CasterAt(0, 0, ricochet), Enemy(2, 3, 0), Enemy(3, 5, 0), Enemy(4, 7, 0), Enemy(5, 9, 0),
            Unit(6, team: 1, 9, 1, gauge: Capacity, skills: new[] { frost }));
        layer.Submit(Use(new UnitId(6), "frost", cell: stops[1]));
        layer.Tick();

        layer.Submit(Use(Caster, "chain", First));
        var events = layer.Tick();

        Assert.Equal(new[] { 2, 3 }, Damaged(events));
        Assert.Equal(stops[1], layer.Get(1).Position);
    }

    [Fact]
    public void EachBounce_IsAnnounced()
    {
        var sim = FourInARow(ChainStrike);

        sim.Submit(Use(Caster, "chain", First));

        Assert.Equal(new[] { (2, 3), (3, 4) },
            sim.Tick().OfType<ChainBounced>().Select(b => (b.From.Value, b.To.Value)));
    }

    [Fact]
    public void AChainLightning_DoesNotJumpThroughSmoke()
    {
        var smoke = new SkillDefinition
        {
            Id = "smoke", Weight = SkillWeight.Light, Range = 9, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false },
            Ground = new GroundZoneDefinition { DurationMs = 2000, BlocksSight = true },
        };
        // La fumée entre les deux ennemis : l'éclair touche le premier, mais ne saute pas sur le second.
        var sim = Simulation(new Grid(8, 2), CasterAt(0, 0, ChainLightning), Enemy(2, 3, 0), Enemy(3, 5, 0),
            Unit(4, team: 0, 4, 1, gauge: Capacity, skills: new[] { smoke }));
        sim.Submit(new ActivateCommand(new UnitId(4), null, new SkillUse("smoke", null, new Cell(4, 0))));
        sim.Tick();
        sim.Submit(Use(Caster, "chain", First));

        Assert.Equal(new[] { 2 }, Damaged(sim.Tick()));
    }

    [Fact]
    public void AChainOnFoot_NeedsTheFirstEnemyWithinItsSteps()
    {
        var sim = Simulation(new Grid(10, 2), CasterAt(0, 0, ChainStrike), Enemy(2, 6, 0));

        Assert.Equal(RejectionReason.OutOfRange, sim.Check(Use(Caster, "chain", First)));
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Caster, "chain", First, new Cell(5, 0))));
    }

    [Fact]
    public void AChainLightning_JumpsWithoutMovingTheCaster_NotThroughWalls()
    {
        // L'ennemi 3 est à 2 cases de l'ennemi 2, mais derrière un mur ; l'ennemi 4 est à 2 cases, à découvert.
        var sim = Simulation(Grid.FromRows("........", "....#...", "........", "........"),
            CasterAt(0, 0, ChainLightning), Enemy(2, 3, 1), Enemy(3, 5, 1), Enemy(4, 2, 3));

        sim.Submit(Use(Caster, "chain", First));
        var events = sim.Tick();

        Assert.Equal(new[] { 2, 4 }, Damaged(events));
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
        Assert.True(sim.Get(2).HasStatus(StatusKind.Stunned));
        Assert.True(sim.Get(4).HasStatus(StatusKind.Stunned));
        Assert.False(sim.Get(3).HasStatus(StatusKind.Stunned));
    }

    [Fact]
    public void AChainLightning_NeedsItsFirstTargetInRangeAndInSight()
    {
        var sim = Simulation(Grid.FromRows("...#...."), CasterAt(0, 0, ChainLightning), Enemy(2, 5, 0));

        Assert.Equal(RejectionReason.NoLineOfSight, sim.Check(Use(Caster, "chain", First)));
    }

    [Fact]
    public void AChain_ReplaysIdentically()
    {
        var setup = Setup(new Grid(10, 2), CasterAt(0, 0, ChainStrike), Enemy(2, 3, 0), Enemy(3, 5, 0), Enemy(4, 7, 0));
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(Caster, "chain", First));
        sim.TickTimes(10);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, 10);

        Assert.Equal(sim.Checksum(), replay.Checksum());
        Assert.Equal(sim.EventLog, replay.EventLog);
    }

    [Theory]
    // Une cible, sur un allié, chargée, avec une zone, avec un bond, sans effet, sans rebond, mentale, sans portée.
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "isMental": true, "chain": { "maxTargets": 3, "bounceRange": 3 } """)]
    [InlineData(""" "range": 0, "minDamage": 5, "maxDamage": 5, "chain": { "maxTargets": 3, "bounceRange": 3 } """)]
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "chain": { "maxTargets": 1, "bounceRange": 3 } """)]
    [InlineData(""" "target": "ally", "range": 4, "minDamage": 0, "maxDamage": 0, "effects": [ { "status": "hasted", "durationMs": 500 } ], "chain": { "maxTargets": 3, "bounceRange": 3 } """)]
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "chargeMs": 500, "chain": { "maxTargets": 3, "bounceRange": 3 } """)]
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "zone": { "shape": "square", "size": 1, "throughWalls": false }, "chain": { "maxTargets": 3, "bounceRange": 3 } """)]
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "selfMove": "pounce", "chain": { "maxTargets": 3, "bounceRange": 3 } """)]
    [InlineData(""" "range": 4, "minDamage": 0, "maxDamage": 0, "gaugeLossPercent": 10, "chain": { "maxTargets": 3, "bounceRange": 3 } """)]
    [InlineData(""" "range": 4, "minDamage": 5, "maxDamage": 5, "chain": { "maxTargets": 3, "bounceRange": 0 } """)]
    public void InvalidChains_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() =>
            SkillDefinition.ListFromJson($$"""[ { "id": "x", "weight": "light", "cooldownMs": 0, {{fields}} } ]"""));
    }
}
