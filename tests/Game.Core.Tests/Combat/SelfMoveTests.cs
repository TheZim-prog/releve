using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class SelfMoveTests
{
    private static readonly UnitId Caster = new(1);

    private static readonly SkillDefinition Breakthrough = new()
    {
        Id = "breakthrough", Weight = SkillWeight.Light, Range = 5, MinDamage = 10, MaxDamage = 10, CooldownMs = 0,
        SelfMove = SelfMoveType.Pierce,
    };

    private static readonly SkillDefinition Leap = new()
    {
        Id = "leap", Weight = SkillWeight.Light, Range = 4, MinDamage = 15, MaxDamage = 15, CooldownMs = 0,
        SelfMove = SelfMoveType.Leap,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
    };

    private static readonly SkillDefinition Charge = new()
    {
        Id = "charge", Weight = SkillWeight.Light, Range = 5, MinDamage = 14, MaxDamage = 14, CooldownMs = 0,
        SelfMove = SelfMoveType.Rush,
        Effects = new[] { new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 1000 } },
    };

    private static UnitSetup CasterAt(int x, int y, SkillDefinition skill) =>
        Unit(1, team: 0, x, y, gauge: Capacity, mobility: 3, skills: new[] { skill });

    private static ActivateCommand AtCell(string skill, Cell cell) => new(Caster, null, new SkillUse(skill, null, cell));

    [Fact]
    public void Breakthrough_RunsThroughUnits_HitsEveryoneCrossed_AndEndsOnTheLastFreeCell()
    {
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Breakthrough), Unit(2, team: 1, 1, 0), Unit(3, team: 0, 2, 0), Unit(4, team: 1, 5, 0));

        sim.Submit(AtCell("breakthrough", new Cell(1, 0)));
        var events = sim.Tick();

        // Course (1,0)…(5,0) : (5,0) est occupée, il s'arrête en (4,0) et frappe l'ennemi et l'allié traversés.
        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(4, 0)), events);
        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Equal(90, sim.Get(3).Hp);
        Assert.Equal(100, sim.Get(4).Hp);
    }

    [Fact]
    public void Breakthrough_IsStoppedByWallsAndLowObstacles_AndTriggersNoOpportunityAttack()
    {
        var sim = Simulation(Grid.FromRows("...o...."),
            CasterAt(0, 0, Breakthrough), Unit(2, team: 1, 1, 0, basicAttack: "strike"));

        sim.Submit(AtCell("breakthrough", new Cell(1, 0)));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(2, 0)), events);
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void Breakthrough_NeedsAnAlignedDirectionAndAFreeCell()
    {
        var sim = Simulation(new Grid(3, 3), CasterAt(0, 0, Breakthrough with { Range = 1 }), Unit(2, team: 1, 1, 0));

        var notAligned = AtCell("breakthrough", new Cell(2, 1));
        var noFreeCell = AtCell("breakthrough", new Cell(1, 0));
        sim.Submit(notAligned);
        sim.Submit(noFreeCell);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(1, notAligned, RejectionReason.InvalidTarget),
                new CommandRejected(1, noFreeCell, RejectionReason.Unreachable),
            },
            sim.Tick());
    }

    [Fact]
    public void Leap_GoesOverUnitsAndLowObstacles_NotWalls_AndHitsEveryoneAroundTheLanding()
    {
        // Le saut (0,1) → (3,1) passe au-dessus de l'ennemi en (1,1) et de l'obstacle bas en (2,1).
        var grid = Grid.FromRows(
            ".......",
            "..o....",
            ".......");
        var sim = Simulation(grid,
            CasterAt(0, 1, Leap), Unit(5, team: 1, 1, 1), Unit(2, team: 1, 4, 1), Unit(3, team: 0, 4, 2));

        sim.Submit(AtCell("leap", new Cell(3, 1)));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 1), new Cell(3, 1)), events);
        Assert.Equal(85, sim.Get(2).Hp);   // ennemi à côté de l'atterrissage
        Assert.Equal(85, sim.Get(3).Hp);   // allié aussi : l'atterrissage touche tout le monde
        Assert.Equal(100, sim.Get(5).Hp);  // celui par-dessus lequel il a sauté
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Fact]
    public void Leap_CannotCrossAWall_NorLandOnAUnit()
    {
        var sim = Simulation(Grid.FromRows("..#...."), CasterAt(0, 0, Leap), Unit(2, team: 1, 4, 0));

        var overWall = AtCell("leap", new Cell(3, 0));
        var ontoUnit = AtCell("leap", new Cell(4, 0));
        sim.Submit(overWall);
        sim.Submit(ontoUnit);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(1, overWall, RejectionReason.NoLineOfSight),
                new CommandRejected(1, ontoUnit, RejectionReason.InvalidDestination),
            },
            sim.Tick());
    }

    [Fact]
    public void Charge_StopsAgainstTheFirstUnitOfItsLine_WhichTakesTheHit()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Charge), Unit(2, team: 1, 3, 0), Unit(3, team: 1, 5, 0));

        sim.Submit(new ActivateCommand(Caster, null, new SkillUse("charge", new UnitId(3))));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(2, 0)), events);
        Assert.Contains(new SkillUsed(1, Caster, "charge", new UnitId(2)), events);
        Assert.Equal(86, sim.Get(2).Hp);
        Assert.True(sim.Get(2).HasStatus(StatusKind.OffBalance));
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void Charge_StoppedByTerrain_RunsAndStops_WithoutHitting()
    {
        var sim = Simulation(Grid.FromRows("..o...", "......"), CasterAt(0, 0, Charge), Unit(2, team: 1, 4, 0));

        sim.Submit(new ActivateCommand(Caster, null, new SkillUse("charge", new UnitId(2))));
        var events = sim.Tick();

        Assert.Contains(new SkillUsed(1, Caster, "charge", null), events);
        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(1, 0)), events);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void Charge_NotAligned_IsRefused()
    {
        var sim = Simulation(new Grid(6, 2), CasterAt(0, 0, Charge), Unit(3, team: 1, 3, 1));

        var notAligned = new ActivateCommand(Caster, null, new SkillUse("charge", new UnitId(3)));
        sim.Submit(notAligned);

        Assert.Equal(new CommandRejected(1, notAligned, RejectionReason.InvalidTarget), sim.Tick().Single());
    }

    [Fact]
    public void Charge_PassesOverDownedUnits()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Charge), Unit(5, team: 1, 2, 0, onDefeat: DefeatOutcome.Downed), Unit(2, team: 1, 4, 0));
        sim.Get(5).Status = UnitStatus.Downed;

        sim.Submit(new ActivateCommand(Caster, null, new SkillUse("charge", new UnitId(2))));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(3, 0)), events);
        Assert.Equal(86, sim.Get(2).Hp);
    }

    [Fact]
    public void Charge_DoesNotCutCorners_ByWallsOrStandingEnemies()
    {
        // Diagonale (0,0) → (1,1) : un mur en (1,0) l'arrête net ; plus loin, deux ennemis en barrage aussi.
        var walled = Simulation(Grid.FromRows(".#..", "....", "....", "...."), CasterAt(0, 0, Charge), Unit(2, team: 1, 1, 1));
        walled.Submit(new ActivateCommand(Caster, null, new SkillUse("charge", new UnitId(2))));
        var events = walled.Tick();
        Assert.Contains(new SkillUsed(1, Caster, "charge", null), events);
        Assert.Equal(100, walled.Get(2).Hp);

        var barrage = Simulation(new Grid(5, 5),
            CasterAt(0, 0, Charge), Unit(3, team: 1, 2, 1), Unit(4, team: 1, 1, 2), Unit(2, team: 1, 3, 3));
        barrage.Submit(new ActivateCommand(Caster, null, new SkillUse("charge", new UnitId(2))));
        events = barrage.Tick();
        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(1, 1)), events);
        Assert.Equal(100, barrage.Get(2).Hp);
    }

    [Fact]
    public void Breakthrough_IsStoppedByAWall_AndByAWallCornerOnTheDiagonal()
    {
        var wall = Simulation(Grid.FromRows("...#...."), CasterAt(0, 0, Breakthrough));
        wall.Submit(AtCell("breakthrough", new Cell(1, 0)));
        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(2, 0)), wall.Tick());

        var corner = Simulation(Grid.FromRows("....", "..#.", "....", "...."), CasterAt(0, 0, Breakthrough));
        corner.Submit(AtCell("breakthrough", new Cell(1, 1)));
        Assert.Contains(new UnitDisplaced(1, Caster, new Cell(0, 0), new Cell(1, 1)), corner.Tick());
    }

    [Fact]
    public void Melee_Diagonally_PastALowObstacle_IsAllowed()
    {
        // (0,1) frappe (1,0) en diagonale ; la voiture en (0,0) est sur un coin frôlé.
        var sim = Simulation(Grid.FromRows("o.", ".."), Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 1, 1, 0));

        sim.Submit(new ActivateCommand(Caster, null, new SkillUse("strike", new UnitId(2))));

        Assert.Contains(sim.Tick(), e => e is UnitDamaged { Unit.Value: 2 });
    }

    [Fact]
    public void BasicAi_UsesItsCharge()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 1, 0, 0, gauge: Capacity, mobility: 0, skills: new[] { Charge }, behavior: Behaviors.Basic),
            Unit(2, team: 0, 4, 0));

        Assert.Contains(sim.TickTimes(2), e => e is SkillUsed { SkillId: "charge" });
    }

    [Theory]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "selfMove": "leap" }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "selfMove": "pierce", "zone": { "shape": "square", "size": 1, "throughWalls": false } }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 3, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "chargeMs": 500, "selfMove": "rush" }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "minHeal": 5, "maxHeal": 5, "cooldownMs": 0, "selfMove": "rush" }""")]
    public void InvalidSelfMoves_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }
}
