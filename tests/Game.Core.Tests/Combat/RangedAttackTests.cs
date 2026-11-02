using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class RangedAttackTests
{
    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 4 };
    private static readonly UnitId Shooter = new(1);

    private static UnitSetup Gunner(int x, int y) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { Shot });

    private static ActivateCommand Shoot(int target, Cell? moveTo = null) =>
        new(Shooter, moveTo, new SkillUse("shot", new UnitId(target)));

    private static CombatEvent Outcome(CombatSimulation sim, ActivateCommand command)
    {
        sim.Submit(command);
        return sim.Tick().First();
    }

    [Fact]
    public void Shot_WithinRangeAndClearLine_Hits()
    {
        var sim = Simulation(new Grid(6, 1), Gunner(0, 0), Unit(2, team: 1, 4, 0));

        Assert.IsType<SkillUsed>(Outcome(sim, Shoot(2)));
        Assert.Equal(90, sim.Get(2).Hp);
    }

    [Fact]
    public void Shot_BeyondRange_IsRejected()
    {
        var sim = Simulation(new Grid(6, 1), Gunner(0, 0), Unit(2, team: 1, 5, 0));

        var command = Shoot(2);
        Assert.Equal(new CommandRejected(1, command, RejectionReason.OutOfRange), Outcome(sim, command));
    }

    [Fact]
    public void Shot_ThroughWall_IsRejected()
    {
        var sim = Simulation(Grid.FromRows("..#.."), Gunner(0, 0), Unit(2, team: 1, 4, 0));

        var command = Shoot(2);
        Assert.Equal(new CommandRejected(1, command, RejectionReason.NoLineOfSight), Outcome(sim, command));
    }

    [Fact]
    public void FirstEnemy_BlocksTheShotOnTheSecond_ButCanBeHit()
    {
        var sim = Simulation(new Grid(6, 1), Gunner(0, 0), Unit(2, team: 1, 2, 0), Unit(3, team: 1, 4, 0));

        var behind = Shoot(3);
        Assert.Equal(new CommandRejected(1, behind, RejectionReason.NoLineOfSight), Outcome(sim, behind));
        Assert.IsType<SkillUsed>(Outcome(sim, Shoot(2)));
    }

    [Fact]
    public void Ally_DoesNotBlockTheShot()
    {
        var sim = Simulation(new Grid(6, 1), Gunner(0, 0), Unit(4, team: 0, 2, 0), Unit(2, team: 1, 4, 0));

        Assert.IsType<SkillUsed>(Outcome(sim, Shoot(2)));
    }

    [Fact]
    public void DownedHero_DoesNotBlockEnemyShots()
    {
        // Le tireur est ici un ennemi (équipe 1) ; le héros à terre est entre lui et sa cible.
        var sim = Simulation(new Grid(6, 1),
            Unit(1, team: 1, 0, 0, initiative: Capacity, gauge: Capacity, skills: new[] { Shot, Strike }),
            Unit(5, team: 0, 1, 0, hp: 5, onDefeat: DefeatOutcome.Downed),
            Unit(2, team: 0, 4, 0, onDefeat: DefeatOutcome.Downed));

        sim.Submit(new ActivateCommand(Shooter, null, new SkillUse("strike", new UnitId(5))));
        Assert.Contains(new UnitDowned(1, new UnitId(5)), sim.Tick());

        Assert.IsType<SkillUsed>(Outcome(sim, Shoot(2)));
    }

    [Fact]
    public void MoveThenShoot_UsesTheLineOfSightFromTheDestination()
    {
        var grid = Grid.FromRows(
            "..#..",
            ".....");
        var sim = Simulation(grid, Gunner(0, 0), Unit(2, team: 1, 4, 0));

        // De (0,0), le mur (2,0) cache la cible. De (2,1), le segment vers (4,0) traverse (3,1) puis (3,0) : dégagé.
        var hidden = Shoot(2);
        Assert.Equal(new CommandRejected(1, hidden, RejectionReason.NoLineOfSight), Outcome(sim, hidden));

        sim.Submit(Shoot(2, moveTo: new Cell(2, 1)));
        var events = sim.Tick();
        Assert.IsType<UnitMoved>(events[0]);
        Assert.IsType<SkillUsed>(events[1]);
    }

    [Fact]
    public void AtDistanceOne_EnemiesOnTheCornersDoNotBlock()
    {
        // Même règle que le corps à corps : à une case, seuls les murs comptent.
        var sim = Simulation(new Grid(2, 2),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Shot }),
            Unit(2, team: 1, 1, 0),
            Unit(3, team: 1, 0, 0),
            Unit(4, team: 1, 1, 1));

        Assert.IsType<SkillUsed>(Outcome(sim, Shoot(2)));
    }
}
