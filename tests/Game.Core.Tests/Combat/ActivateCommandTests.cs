using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ActivateCommandTests
{
    private static readonly UnitId Id1 = new(1);
    private static readonly UnitId Id2 = new(2);

    [Fact]
    public void Move_WhenReady_MovesAndRestartsGaugeAtMoveOnlyPercent()
    {
        var sim = Simulation(new Grid(5, 1), Unit(1, team: 0, 0, 0, initiative: 10, mobility: 3));
        sim.TickTimes(10); // prête au tick 10

        sim.Submit(new ActivateCommand(Id1, new Cell(2, 0)));
        var events = sim.Tick();

        Assert.Equal(
            new CombatEvent[] { new UnitMoved(11, Id1, new Cell(0, 0), new[] { new Cell(1, 0), new Cell(2, 0) }) },
            events);
        var unit = sim.Get(1);
        Assert.Equal(new Cell(2, 0), unit.Position);
        // Repart à 50, puis gagne son initiative dans le même tick.
        Assert.Equal(60, unit.Gauge);

        // 60 → 100 en 4 ticks : prête de nouveau au tick 15.
        Assert.Equal(new CombatEvent[] { new UnitReady(15, Id1) }, sim.TickTimes(4));
    }

    [Fact]
    public void ReadyUnit_ActsOnlyOncePerTick()
    {
        // Initiative = capacité : la jauge se remplit en un tick, mais seulement après les commandes.
        var sim = Simulation(new Grid(4, 1), Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity));

        var first = new ActivateCommand(Id1, new Cell(1, 0));
        var second = new ActivateCommand(Id1, new Cell(2, 0));
        sim.Submit(first);
        sim.Submit(second);

        Assert.Equal(
            new CombatEvent[]
            {
                new UnitMoved(1, Id1, new Cell(0, 0), new[] { new Cell(1, 0) }),
                new CommandRejected(1, second, RejectionReason.NotReady),
                new UnitReady(1, Id1),
            },
            sim.Tick());
    }

    [Fact]
    public void Submit_RejectsUnknownCommandTypes()
    {
        var sim = Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0));

        Assert.Throws<ArgumentException>(() => sim.Submit(new UnsupportedCommand(Id1)));
    }

    private sealed record UnsupportedCommand(UnitId Unit) : CombatCommand(Unit);

    [Fact]
    public void Move_WhenNotReady_IsRejected()
    {
        var sim = Simulation(new Grid(5, 1), Unit(1, team: 0, 0, 0));

        var command = new ActivateCommand(Id1, new Cell(1, 0));
        sim.Submit(command);

        Assert.Equal(new CombatEvent[] { new CommandRejected(1, command, RejectionReason.NotReady) }, sim.Tick());
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
    }

    [Fact]
    public void Move_BeyondMobility_IsRejected_AndGaugeStaysFull()
    {
        var sim = Simulation(new Grid(6, 1), Unit(1, team: 0, 0, 0, mobility: 3, gauge: Capacity));

        var command = new ActivateCommand(Id1, new Cell(4, 0));
        sim.Submit(command);

        Assert.Equal(new CombatEvent[] { new CommandRejected(1, command, RejectionReason.Unreachable) }, sim.Tick());
        Assert.True(sim.Get(1).IsReady);
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
    }

    [Fact]
    public void Move_CrossesAlly_ButNotEnemy()
    {
        var grid = Grid.FromRows(
            "#####",
            ".....",
            "#####");
        var sim = Simulation(grid,
            Unit(1, team: 0, 0, 1, mobility: 4, gauge: Capacity),
            Unit(2, team: 0, 1, 1),
            Unit(3, team: 1, 3, 1));

        sim.Submit(new ActivateCommand(Id1, new Cell(2, 1)));
        sim.Tick();
        Assert.Equal(new Cell(2, 1), sim.Get(1).Position);

        var sim2 = Simulation(grid,
            Unit(1, team: 0, 0, 1, mobility: 4, gauge: Capacity),
            Unit(3, team: 1, 2, 1));
        var blocked = new ActivateCommand(Id1, new Cell(4, 1));
        sim2.Submit(blocked);
        Assert.Equal(new CommandRejected(1, blocked, RejectionReason.Unreachable), sim2.Tick().Single());
    }

    [Fact]
    public void Move_OntoAnotherUnit_IsRejected()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(2, team: 0, 1, 0));

        var command = new ActivateCommand(Id1, new Cell(1, 0));
        sim.Submit(command);

        Assert.Equal(new CommandRejected(1, command, RejectionReason.Unreachable), sim.Tick().Single());
    }

    [Fact]
    public void Conflict_FirstValidatedCommandWins_SecondUnitStaysReady()
    {
        var sim = Simulation(new Grid(3, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity),
            Unit(2, team: 1, 2, 0, gauge: Capacity));

        var first = new ActivateCommand(Id1, new Cell(1, 0));
        var second = new ActivateCommand(Id2, new Cell(1, 0));
        sim.Submit(first);
        sim.Submit(second);
        var events = sim.Tick();

        Assert.Equal(
            new CombatEvent[]
            {
                new UnitMoved(1, Id1, new Cell(0, 0), new[] { new Cell(1, 0) }),
                new CommandRejected(1, second, RejectionReason.Unreachable),
            },
            events);
        Assert.True(sim.Get(2).IsReady);
    }

    [Fact]
    public void InvalidCommands_AreRejected()
    {
        var sim = Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, gauge: Capacity));

        var unknown = new ActivateCommand(new UnitId(99), new Cell(1, 0));
        var empty = new ActivateCommand(Id1, MoveTo: null);
        var stay = new ActivateCommand(Id1, new Cell(0, 0));
        sim.Submit(unknown);
        sim.Submit(empty);
        sim.Submit(stay);

        Assert.Equal(
            new CombatEvent[]
            {
                new CommandRejected(1, unknown, RejectionReason.UnknownUnit),
                new CommandRejected(1, empty, RejectionReason.Empty),
                new CommandRejected(1, stay, RejectionReason.InvalidDestination),
            },
            sim.Tick());
        Assert.True(sim.Get(1).IsReady);
    }

    [Fact]
    public void Simulation_OwnsACopyOfTheGrid()
    {
        var grid = new Grid(3, 1);
        var sim = Simulation(grid, Unit(1, team: 0, 0, 0, gauge: Capacity));

        grid.SetBlocked(new Cell(1, 0), true);
        sim.Submit(new ActivateCommand(Id1, new Cell(2, 0)));

        Assert.IsType<UnitMoved>(sim.Tick().Single());
    }
}
