using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ForcedMoveTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId Target = new(2);

    /// <summary>10 dégâts, puis pousse de 3 cases ; collision 5.</summary>
    private static readonly SkillDefinition Projection = Strike with
    {
        Id = "projection",
        ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 3, CollisionMinDamage = 5, CollisionMaxDamage = 5 },
    };

    /// <summary>Pose la cible à 3 cases au plus, portée 5.</summary>
    private static readonly SkillDefinition Telekinesis = new()
    {
        Id = "telekinesis", Target = SkillTargeting.Any, Weight = SkillWeight.Light, Range = 5,
        MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Place, Distance = 3 },
    };

    private static UnitSetup CasterAt(int x, int y, SkillDefinition skill) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    private static ActivateCommand Hit(UnitId target, Cell? destination = null, string skill = "projection") =>
        new(Caster, null, new SkillUse(skill, target, destination));

    [Fact]
    public void Projection_PushesInAStraightLine()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Projection), Unit(2, team: 1, 1, 0));

        sim.Submit(Hit(Target));

        Assert.Equal(
            new CombatEvent[]
            {
                new SkillUsed(1, Caster, "projection", Target),
                new UnitDamaged(1, Target, 10, 90),
                new UnitDisplaced(1, Target, new Cell(1, 0), new Cell(4, 0)),
            },
            sim.Tick().Where(e => e is not UnitReady));
    }

    [Fact]
    public void Projection_AgainstAWall_StopsAndHurtsTheTarget()
    {
        var sim = Simulation(Grid.FromRows("...#...."), CasterAt(0, 0, Projection), Unit(2, team: 1, 1, 0));

        sim.Submit(Hit(Target));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Target, new Cell(1, 0), new Cell(2, 0)), events);
        Assert.Contains(new UnitCollided(1, Target, null), events);
        Assert.Equal(85, sim.Get(2).Hp); // 10 + 5 de collision
    }

    [Fact]
    public void Projection_IntoAnotherUnit_HurtsBoth()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Projection), Unit(2, team: 1, 1, 0), Unit(3, team: 1, 3, 0));

        sim.Submit(Hit(Target));
        var events = sim.Tick();

        Assert.Contains(new UnitCollided(1, Target, new UnitId(3)), events);
        Assert.Equal(new Cell(2, 0), sim.Get(2).Position);
        Assert.Equal(85, sim.Get(2).Hp);
        Assert.Equal(95, sim.Get(3).Hp);
    }

    [Fact]
    public void Projection_DiagonalPush_DoesNotCutCorners()
    {
        // Diagonale (1,1) → (2,2) : elle frôle le mur (2,1), la poussée s'arrête tout de suite.
        var grid = Grid.FromRows(
            "....",
            "..#.",
            "....",
            "....");
        var sim = Simulation(grid, CasterAt(0, 0, Projection), Unit(2, team: 1, 1, 1));

        sim.Submit(Hit(Target));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitDisplaced);
        Assert.Contains(new UnitCollided(1, Target, null), events);
    }

    [Fact]
    public void ForcedMoves_TriggerNoOpportunityAttack()
    {
        // La cible, poussée loin de son voisin ennemi (un héros avec attaque de base), n'est pas punie.
        var sim = Simulation(new Grid(8, 2),
            CasterAt(0, 0, Projection),
            Unit(2, team: 1, 1, 0),
            Unit(3, team: 0, 1, 1, basicAttack: "strike"));

        sim.Submit(Hit(Target));

        Assert.DoesNotContain(sim.Tick(), e => e is OpportunityAttack);
    }

    [Fact]
    public void Telekinesis_PlacesAllyOrEnemyOnAChosenFreeCell()
    {
        var sim = Simulation(new Grid(8, 3), CasterAt(0, 1, Telekinesis), Unit(2, team: 1, 3, 1), Unit(3, team: 0, 1, 0));

        sim.Submit(Hit(Target, new Cell(5, 2), "telekinesis"));
        Assert.Contains(new UnitDisplaced(1, Target, new Cell(3, 1), new Cell(5, 2)), sim.Tick());

        sim.TickTimes(9); // le télépathe (initiative 10, reprise 40 %) est de nouveau prêt
        sim.Submit(Hit(new UnitId(3), new Cell(3, 0), "telekinesis"));
        Assert.Contains(sim.Tick(), e => e is UnitDisplaced { Unit.Value: 3 });
    }

    [Theory]
    [InlineData(1, 1)] // sa propre case
    [InlineData(7, 1)] // trop loin (4 cases)
    [InlineData(1, 0)] // occupée
    [InlineData(3, 1)] // à 2 cases, mais cachée par le mur (2,1) vue depuis la cible
    public void Telekinesis_RejectsBadDestinations(int x, int y)
    {
        var grid = Grid.FromRows(
            "........",
            "..#.....",
            "........");
        var sim = Simulation(grid, CasterAt(0, 2, Telekinesis), Unit(2, team: 1, 1, 1), Unit(3, team: 1, 1, 0));

        var command = Hit(Target, new Cell(x, y), "telekinesis");
        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.InvalidDestination), sim.Tick().Single());
    }

    [Fact]
    public void Telekinesis_NeedsADestination_AndNotOnSelfOrDowned()
    {
        var sim = Simulation(new Grid(8, 1),
            CasterAt(0, 0, Telekinesis),
            Unit(2, team: 1, 2, 0),
            Unit(3, team: 0, 4, 0));
        sim.Get(3).Status = UnitStatus.Downed;

        var noCell = Hit(Target, null, "telekinesis");
        var self = Hit(Caster, new Cell(1, 0), "telekinesis");
        var downed = Hit(new UnitId(3), new Cell(5, 0), "telekinesis");
        foreach (var command in new[] { noCell, self, downed })
            sim.Submit(command);

        Assert.Equal(
            new[] { noCell, self, downed }.Select(c => new CommandRejected(1, c, RejectionReason.InvalidTarget)),
            sim.Tick().OfType<CommandRejected>());
    }

    [Fact]
    public void MoveThenTelekinesis_ChecksTheDestinationAfterTheMove()
    {
        var sim = Simulation(new Grid(8, 3), CasterAt(0, 1, Telekinesis), Unit(2, team: 1, 3, 1));

        // La case où le télépathe arrive n'est plus libre…
        var ontoArrival = new ActivateCommand(Caster, new Cell(2, 2), new SkillUse("telekinesis", Target, new Cell(2, 2)));
        sim.Submit(ontoArrival);
        Assert.Equal(new CommandRejected(1, ontoArrival, RejectionReason.InvalidDestination), sim.Tick().Single());

        // … mais celle qu'il vient de quitter l'est.
        sim.Submit(new ActivateCommand(Caster, new Cell(1, 2), new SkillUse("telekinesis", Target, new Cell(0, 1))));
        Assert.Contains(new UnitDisplaced(2, Target, new Cell(3, 1), new Cell(0, 1)), sim.Tick());
    }

    [Fact]
    public void Projection_Diagonal_IsStoppedByTheTargetsEnemiesOnTheCorners()
    {
        // La cible (équipe 1) en (1,1), poussée vers (2,2) ; des héros (ses ennemis) en (2,1) et (1,2) bouchent la diagonale.
        var sim = Simulation(new Grid(4, 4),
            CasterAt(0, 0, Projection), Unit(2, team: 1, 1, 1), Unit(3, team: 0, 2, 1), Unit(4, team: 0, 1, 2));

        sim.Submit(Hit(Target));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is UnitDisplaced);
        Assert.Contains(new UnitCollided(1, Target, null), events);
    }

    [Fact]
    public void Collision_CanDefeatTheStruckUnit()
    {
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, Projection), Unit(2, team: 1, 1, 0), Unit(3, team: 1, 3, 0, hp: 5));

        sim.Submit(Hit(Target));

        Assert.Contains(new UnitRemoved(1, new UnitId(3)), sim.Tick());
    }

    [Fact]
    public void ChargedProjection_PushesAtRelease()
    {
        var charged = Projection with { Id = "charged-projection", Weight = SkillWeight.Heavy, ChargeMs = 500 };
        var sim = Simulation(new Grid(8, 1), CasterAt(0, 0, charged), Unit(2, team: 1, 1, 0, initiative: 0));

        sim.Submit(Hit(Target, null, "charged-projection"));
        var events = sim.TickTimes(11);

        Assert.Contains(new UnitDisplaced(11, Target, new Cell(1, 0), new Cell(4, 0)), events);
    }

    [Fact]
    public void ForcedMove_InterruptsACharge()
    {
        var quake = new SkillDefinition
        {
            Id = "quake", Weight = SkillWeight.Heavy, Range = 0, MinDamage = 30, MaxDamage = 30, CooldownMs = 0, ChargeMs = 1000,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var sim = Simulation(new Grid(8, 3),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Telekinesis }),
            Unit(2, team: 1, 3, 1, gauge: Capacity, skills: new[] { quake }));
        sim.Submit(new ActivateCommand(Target, null, new SkillUse("quake", null, new Cell(3, 1))));
        sim.Tick();

        sim.Submit(Hit(Target, new Cell(5, 1), "telekinesis"));
        var events = sim.Tick();

        Assert.Contains(new ChargeInterrupted(2, Target, "quake"), events);
        Assert.Null(sim.Get(2).Charge);
    }

    [Theory]
    [InlineData("""{ "id": "x", "weight": "light", "range": 2, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "forcedMove": { "type": "push", "distance": 2 } }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 1, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "forcedMove": { "type": "push", "distance": 2 } }""")]
    [InlineData("""{ "id": "x", "target": "any", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "chargeMs": 500, "forcedMove": { "type": "place", "distance": 2 } }""")]
    [InlineData("""{ "id": "x", "target": "any", "weight": "light", "range": 3, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "forcedMove": { "type": "place", "distance": 2 } }""")]
    [InlineData("""{ "id": "x", "target": "any", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0 }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 1, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "forcedMove": { "type": "push", "distance": 0 } }""")]
    [InlineData("""{ "id": "x", "target": "any", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "forcedMove": { "type": "place", "distance": 2, "collisionMinDamage": 1, "collisionMaxDamage": 2 } }""")]
    public void InvalidForcedMoveSkills_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }
}
