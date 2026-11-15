using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Mouvements spéciaux qui visent un ennemi (Ruée, Bond, Repli) et réglages des autres (Slalom, Propulsion).</summary>
public class StrikeMoveTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId Target = new(2);

    /// <summary>Court sur 4 pas au plus, puis 10 dégâts.</summary>
    private static readonly SkillDefinition Dash = Strike with { Id = "dash", Range = 4, SelfMove = SelfMoveType.Dash };

    private static readonly SkillDefinition Pounce = Strike with { Id = "pounce", Range = 4, SelfMove = SelfMoveType.Pounce };

    /// <summary>10 dégâts au contact, puis 2 pas au plus.</summary>
    private static readonly SkillDefinition Retreat = Strike with
    {
        Id = "retreat", SelfMove = SelfMoveType.Retreat, SelfMoveOptions = new SelfMoveOptions { RetreatDistance = 2 },
    };

    private static UnitSetup CasterAt(int x, int y, SkillDefinition skill) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    private static UnitSetup Enemy(int id, int x, int y) => Unit(id, team: 1, x, y, basicAttack: "strike");

    [Fact]
    public void Dash_RunsNextToItsTarget_AndStrikes_WithoutOpportunityAttack()
    {
        var sim = Simulation(new Grid(8, 2), CasterAt(0, 0, Dash), Enemy(2, 4, 0), Enemy(3, 0, 1));

        sim.Submit(Use(Caster, "dash", Target));

        Assert.Equal(
            new CombatEvent[]
            {
                new SkillUsed(1, Caster, "dash", Target),
                new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(3, 0)),
                new UnitDamaged(1, Target, 10, 90),
            },
            sim.Tick().Where(e => e is not UnitReady));
    }

    [Fact]
    public void Dash_AlreadyNextToItsTarget_StrikesWithoutMoving()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(3, 0, Dash), Enemy(2, 4, 0));

        sim.Submit(Use(Caster, "dash", Target));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitDisplaced);
        Assert.Equal(90, sim.Get(2).Hp);
    }

    [Fact]
    public void Dash_GoesOnFoot_SoTooManyStepsOrABlockedWay_AreRefused()
    {
        var tooFar = Simulation(new Grid(8, 1), CasterAt(0, 0, Dash), Enemy(2, 6, 0));
        // Un obstacle bas barre le couloir : un bond passe par-dessus, pas une ruée.
        var blocked = Simulation(Grid.FromRows("..o....."), CasterAt(0, 0, Dash), Enemy(2, 5, 0));

        Assert.Equal(RejectionReason.OutOfRange, tooFar.Check(Use(Caster, "dash", Target)));
        Assert.Equal(RejectionReason.OutOfRange, blocked.Check(Use(Caster, "dash", Target)));
        Assert.Equal(RejectionReason.InvalidTarget, tooFar.Check(Use(Caster, "dash", Target, new Cell(1, 0))));
        Assert.Equal(RejectionReason.InvalidTarget, tooFar.Check(Use(Caster, "dash", cell: new Cell(1, 0))));
    }

    [Fact]
    public void RoundTrip_ComesBackToItsStartingCell()
    {
        var roundTrip = Dash with { SelfMoveOptions = new SelfMoveOptions { ReturnsToStart = true } };
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, roundTrip), Enemy(2, 4, 0));

        sim.Submit(Use(Caster, "dash", Target));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(3, 0)), events);
        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(3, 0), new Cell(0, 0)), events);
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
        Assert.Equal(90, sim.Get(2).Hp);
    }

    [Fact]
    public void RunningStrike_HitsHarderForEachCellRun()
    {
        var runningStrike = Dash with { SelfMoveOptions = new SelfMoveOptions { DamagePercentPerCell = 50 } };
        var far = Simulation(new Grid(8, 1), CasterAt(0, 0, runningStrike), Enemy(2, 4, 0));
        var close = Simulation(new Grid(8, 1), CasterAt(3, 0, runningStrike), Enemy(2, 4, 0));

        far.Submit(Use(Caster, "dash", Target));
        far.Tick();
        close.Submit(Use(Caster, "dash", Target));
        close.Tick();

        Assert.Equal(75, far.Get(2).Hp); // 3 cases : 10 × 250 %
        Assert.Equal(90, close.Get(2).Hp);
    }

    [Fact]
    public void TheMomentum_DoesNotBoostTheCollisionOfAPush()
    {
        var skill = Dash with
        {
            SelfMoveOptions = new SelfMoveOptions { DamagePercentPerCell = 50 },
            ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2, CollisionMinDamage = 4, CollisionMaxDamage = 4 },
        };
        var sim = Simulation(Grid.FromRows(".....#.."), CasterAt(0, 0, skill), Enemy(2, 4, 0));

        sim.Submit(Use(Caster, "dash", Target));
        sim.Tick();

        Assert.Equal(100 - 25 - 4, sim.Get(2).Hp); // le coup avec l'élan (3 cases), la collision sans
    }

    [Fact]
    public void Clearance_PushesItsTargetAwayFromWhereTheDashEnded()
    {
        var clearance = Dash with { ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2 } };
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, clearance), Enemy(2, 4, 0));

        sim.Submit(Use(Caster, "dash", Target));
        sim.Tick();

        Assert.Equal(new Cell(3, 0), sim.Get(1).Position);
        Assert.Equal(new Cell(6, 0), sim.Get(2).Position);
    }

    [Fact]
    public void Pounce_JumpsOverObstacles_NextToItsTarget_AndHitsOnlyIt()
    {
        var sim = Simulation(Grid.FromRows("..o.....", "........"),
            CasterAt(0, 0, Pounce), Enemy(2, 4, 0), Enemy(3, 4, 1), Unit(4, team: 0, 3, 1));

        sim.Submit(Use(Caster, "pounce", Target));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(3, 0)), events);
        Assert.Equal(new[] { Target }, events.OfType<UnitDamaged>().Select(e => e.Unit));
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void Pounce_NeedsAFreeCellInRange_NextToItsTarget()
    {
        var tooFar = Simulation(new Grid(8, 1), CasterAt(0, 0, Pounce), Enemy(2, 6, 0));
        var behindAWall = Simulation(Grid.FromRows("..#....."), CasterAt(0, 0, Pounce), Enemy(2, 4, 0));

        Assert.Equal(RejectionReason.OutOfRange, tooFar.Check(Use(Caster, "pounce", Target)));
        Assert.Equal(RejectionReason.OutOfRange, behindAWall.Check(Use(Caster, "pounce", Target)));
    }

    [Fact]
    public void Retreat_StrikesThenWalksAway_WithoutOpportunityAttack()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(2, 0, Retreat), Enemy(2, 3, 0));

        sim.Submit(Use(Caster, "retreat", Target, new Cell(0, 0)));

        Assert.Equal(
            new CombatEvent[]
            {
                new SkillUsed(1, Caster, "retreat", Target),
                new UnitDamaged(1, Target, 10, 90),
                new UnitDisplaced(1, Caster, new Cell(2, 0), new Cell(0, 0)),
            },
            sim.Tick().Where(e => e is not UnitReady));
    }

    [Fact]
    public void Retreat_NeedsAnAdjacentEnemy_AndACellItCanWalkTo()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(3, 0, Retreat), Enemy(2, 4, 0), Enemy(3, 6, 0));

        Assert.Null(sim.Check(Use(Caster, "retreat", Target, new Cell(1, 0))));
        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Use(Caster, "retreat", Target, new Cell(0, 0)))); // 3 pas
        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Use(Caster, "retreat", Target, new Cell(5, 0)))); // derrière l'ennemi
        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Use(Caster, "retreat", Target, new Cell(3, 0)))); // sur place
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Use(Caster, "retreat", Target))); // sans case
        Assert.Equal(RejectionReason.OutOfRange, sim.Check(Use(Caster, "retreat", new UnitId(3), new Cell(1, 0))));
    }

    [Fact]
    public void Slalom_HitsOnlyTheEnemiesItCrosses()
    {
        var slalom = Strike with { Id = "slalom", Range = 5, SelfMove = SelfMoveType.Pierce, SelfMoveOptions = new SelfMoveOptions { EnemiesOnly = true } };
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, slalom), Enemy(2, 1, 0), Unit(3, team: 0, 2, 0));

        sim.Submit(Use(Caster, "slalom", cell: new Cell(1, 0)));
        sim.Tick();

        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void Propulsion_ExplodesAroundTheCellItLeaves()
    {
        var propulsion = Strike with
        {
            Id = "propulsion", Range = 4, SelfMove = SelfMoveType.Leap, SelfMoveOptions = new SelfMoveOptions { ZoneAtStart = true },
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(8, 1), CasterAt(1, 0, propulsion), Enemy(2, 0, 0), Enemy(3, 6, 0));

        sim.Submit(Use(Caster, "propulsion", cell: new Cell(5, 0)));
        sim.Tick();

        Assert.Equal(new Cell(5, 0), sim.Get(1).Position);
        Assert.Equal(90, sim.Get(2).Hp); // au contact du départ
        Assert.Equal(100, sim.Get(3).Hp); // au contact de l'arrivée
    }

    [Theory]
    // Une ruée sans cible ennemie, un retour pour un bond, un repli sans distance, à distance, ou une distance sans repli.
    [InlineData(""" "target": "ally", "range": 3, "minDamage": 0, "maxDamage": 0, "selfMove": "dash", "effects": [ { "status": "hasted", "durationMs": 1000 } ] """)]
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMove": "pounce", "selfMoveOptions": { "returnsToStart": true } """)]
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMove": "pounce", "selfMoveOptions": { "damagePercentPerCell": 20 } """)]
    [InlineData(""" "range": 1, "minDamage": 5, "maxDamage": 5, "selfMove": "retreat" """)]
    [InlineData(""" "range": 2, "minDamage": 5, "maxDamage": 5, "selfMove": "retreat", "selfMoveOptions": { "retreatDistance": 2 } """)]
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMove": "dash", "selfMoveOptions": { "retreatDistance": 2 } """)]
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMove": "dash", "selfMoveOptions": { "enemiesOnly": true } """)]
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMove": "pierce", "selfMoveOptions": { "zoneAtStart": true } """)]
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMoveOptions": { "enemiesOnly": true } """)]
    // Seuls une ruée ou un bond repoussent (sans retour) ; pas de renversement ni de pose en courant.
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMove": "dash", "selfMoveOptions": { "returnsToStart": true }, "forcedMove": { "type": "push", "distance": 2 } """)]
    [InlineData(""" "range": 3, "minDamage": 5, "maxDamage": 5, "selfMove": "charge", "forcedMove": { "type": "push", "distance": 2 } """)]
    [InlineData(""" "range": 1, "minDamage": 5, "maxDamage": 5, "selfMove": "dash", "forcedMove": { "type": "throw", "distance": 2 } """)]
    public void InvalidMoveSkills_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() =>
            SkillDefinition.ListFromJson($$"""[ { "id": "x", "weight": "light", "cooldownMs": 0, {{fields}} } ]"""));
    }
}
