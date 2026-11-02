using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class RescueTests
{
    private static readonly UnitId Speedster = new(1);
    private static readonly UnitId Fallen = new(2);

    private static readonly SkillDefinition FlashRescue = new()
    {
        Id = "flash-rescue", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 6,
        MinDamage = 0, MaxDamage = 0, CooldownMs = 0, RescueDropRadius = 4,
    };

    private static UnitSetup SpeedsterAt(int x, int y) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { FlashRescue });

    private static UnitSetup FallenAt(int x, int y) => Unit(2, team: 0, x, y, onDefeat: DefeatOutcome.Downed);

    private static CombatSimulation WithFallen(Grid grid, params UnitSetup[] units)
    {
        var sim = Simulation(grid, units);
        var fallen = sim.Get(2);
        fallen.Hp = 0;
        fallen.Status = UnitStatus.Downed;
        return sim;
    }

    private static ActivateCommand Rescue(Cell drop, int target = 2) => new(Speedster, null, new SkillUse("flash-rescue", new UnitId(target), drop));

    [Fact]
    public void Rescue_DropsTheDownedAlly_WhereChosen_AndMarksItRescued()
    {
        var sim = WithFallen(new Grid(8, 3), SpeedsterAt(0, 1), FallenAt(4, 1), Unit(9, team: 1, 5, 1));

        sim.Submit(Rescue(new Cell(0, 0)));
        var events = sim.Tick();

        Assert.Contains(new UnitRescued(1, Fallen, Speedster), events);
        Assert.Contains(new UnitDisplaced(1, Fallen, new Cell(4, 1), new Cell(0, 0)), events);
        Assert.DoesNotContain(events, e => e is UnitDisplaced { Unit.Value: 1 }); // déjà à côté du dépôt
        var fallen = sim.Get(2);
        Assert.True(fallen.WasRescued);
        Assert.Equal(UnitStatus.Downed, fallen.Status); // il reste à terre
    }

    [Fact]
    public void Rescuer_EndsNextToTheDropCell()
    {
        var sim = WithFallen(new Grid(8, 3), SpeedsterAt(0, 1), FallenAt(4, 1));

        sim.Submit(Rescue(new Cell(3, 2)));
        sim.Tick();

        Assert.Equal(new Cell(3, 2), sim.Get(2).Position);
        Assert.True(sim.Get(1).Position.IsAdjacentTo(new Cell(3, 2)));
    }

    [Fact]
    public void Rescue_NeedsAPathOnFoot()
    {
        // L'allié à terre est enfermé derrière une ligne d'ennemis.
        var grid = Grid.FromRows(
            "...#...",
            "...#...",
            ".......");
        var sim = WithFallen(grid, SpeedsterAt(0, 1), FallenAt(5, 1), Unit(9, team: 1, 3, 2));

        var command = Rescue(new Cell(1, 1));
        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.OutOfRange), sim.Tick().Single());
    }

    [Fact]
    public void Rescue_OnlyForADownedAlly()
    {
        var sim = WithFallen(new Grid(8, 3), SpeedsterAt(0, 1), FallenAt(4, 1), Unit(3, team: 0, 2, 1), Unit(9, team: 1, 3, 2));
        sim.Get(9).Status = UnitStatus.Downed;

        var standing = Rescue(new Cell(0, 0), 3);
        var enemy = Rescue(new Cell(0, 0), 9);
        var self = Rescue(new Cell(0, 0), 1);
        foreach (var command in new[] { standing, enemy, self })
            sim.Submit(command);

        Assert.Equal(
            new[] { standing, enemy, self }.Select(c => new CommandRejected(1, c, RejectionReason.InvalidTarget)),
            sim.Tick().OfType<CommandRejected>());
    }

    [Theory]
    [InlineData(5, 1)] // trop loin du speedster (rayon 4)
    [InlineData(2, 1)] // occupée
    [InlineData(4, 1)] // la case de l'allié lui-même
    [InlineData(1, 0)] // un mur
    [InlineData(3, 0)] // derrière le mur, pas en vue
    public void Rescue_RejectsBadDropCells(int x, int y)
    {
        var grid = Grid.FromRows(
            ".##.....",
            "........",
            "........");
        var sim = WithFallen(grid, SpeedsterAt(0, 1), FallenAt(4, 1), Unit(3, team: 0, 2, 1));

        var command = Rescue(new Cell(x, y));
        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.InvalidDestination), sim.Tick().Single());
    }

    [Fact]
    public void Rescue_RangeCountsStepsToTheAllyItself()
    {
        // Allié à terre à 6 pas exactement (portée 6) : accepté ; à 7 : refusé.
        var near = WithFallen(new Grid(10, 1), SpeedsterAt(0, 0), FallenAt(6, 0));
        near.Submit(Rescue(new Cell(1, 0)));
        Assert.Contains(near.Tick(), e => e is UnitRescued);

        var far = WithFallen(new Grid(10, 1), SpeedsterAt(0, 0), FallenAt(7, 0));
        var command = Rescue(new Cell(1, 0));
        far.Submit(command);
        Assert.Equal(new CommandRejected(1, command, RejectionReason.OutOfRange), far.Tick().Single());
    }

    [Fact]
    public void Rescue_CannotCutBetweenTwoEnemies_NorDropBehindAStandingEnemy()
    {
        // Sur cette grille 3x3, le seul accès à l'allié (2,2) passe en diagonale depuis (1,1), entre deux ennemis.
        var cornered = WithFallen(new Grid(3, 3), SpeedsterAt(0, 0), FallenAt(2, 2),
            Unit(8, team: 1, 2, 1), Unit(9, team: 1, 1, 2));
        var blocked = new ActivateCommand(Speedster, null, new SkillUse("flash-rescue", Fallen, new Cell(0, 1)));
        cornered.Submit(blocked);
        Assert.Contains(cornered.Tick(), e => e is CommandRejected { Reason: RejectionReason.OutOfRange });

        // Dépôt derrière un ennemi debout : pas en vue.
        var sim = WithFallen(new Grid(8, 1), SpeedsterAt(0, 0), FallenAt(1, 0), Unit(9, team: 1, 2, 0));
        var behind = Rescue(new Cell(3, 0));
        sim.Submit(behind);
        Assert.Equal(new CommandRejected(1, behind, RejectionReason.InvalidDestination), sim.Tick().Single());
    }

    [Fact]
    public void Rescue_CannotDropOnTheRescuersOwnCell()
    {
        var sim = WithFallen(new Grid(8, 3), SpeedsterAt(0, 1), FallenAt(3, 1));

        var command = Rescue(new Cell(0, 1));
        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.InvalidDestination), sim.Tick().Single());
    }

    [Fact]
    public void MoveThenRescue_PlansFromTheNewPosition()
    {
        var sim = WithFallen(new Grid(10, 3), SpeedsterAt(0, 1), FallenAt(8, 1));

        // De (0,1), l'allié est à 8 pas ; après un déplacement en (3,1), il n'est plus qu'à 5. Dépôt sur l'ancienne case.
        sim.Submit(new ActivateCommand(Speedster, new Cell(3, 1), new SkillUse("flash-rescue", Fallen, new Cell(0, 1))));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Fallen, new Cell(8, 1), new Cell(0, 1)), events);
        Assert.True(sim.Get(1).Position.IsAdjacentTo(new Cell(0, 1)));
    }

    [Fact]
    public void Rescue_TriggersNoOpportunityAttack()
    {
        // Dépôt en (4,2) : le speedster finit en (4,1), loin de l'ennemi qui était à son contact.
        var sim = WithFallen(new Grid(8, 3), SpeedsterAt(1, 1), FallenAt(5, 1), Unit(9, team: 1, 1, 0, basicAttack: "strike"));

        sim.Submit(Rescue(new Cell(4, 2)));
        var events = sim.Tick();

        Assert.Contains(new UnitDisplaced(1, Speedster, new Cell(1, 1), new Cell(4, 1)), events);
        Assert.DoesNotContain(events, e => e is OpportunityAttack);
    }

    [Theory]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 6, "minDamage": 5, "maxDamage": 5, "cooldownMs": 0, "rescueDropRadius": 4 }""")]
    [InlineData("""{ "id": "x", "weight": "light", "range": 6, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "rescueDropRadius": 4 }""")]
    [InlineData("""{ "id": "x", "target": "ally", "weight": "light", "range": 6, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "rescueDropRadius": 0 }""")]
    public void InvalidRescues_AreRejectedAtLoad(string skill)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson($"[{skill}]"));
    }
}
