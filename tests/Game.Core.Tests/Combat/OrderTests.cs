using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Ordres donnés à l'avance : ils partent seuls dès que l'unité peut agir.</summary>
public class OrderTests
{
    private static ActivateCommand Strike(int unit, int target, Cell? moveTo = null) =>
        new(new UnitId(unit), moveTo, new SkillUse("strike", new UnitId(target)));

    [Fact]
    public void AnOrderGivenEarly_LeavesAsSoonAsTheUnitCanAct()
    {
        var sim = Simulation(new Grid(5, 1), Unit(1, team: 0, 0, 0, gauge: 0), Unit(2, team: 1, 3, 0));
        sim.Submit(new OrderCommand(Strike(1, 2, moveTo: new Cell(2, 0))));

        sim.Tick();
        Assert.Contains(sim.EventLog, e => e is OrderGiven { Unit.Value: 1 });
        while (!sim.Get(1).IsReady)
        {
            Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
            sim.Tick();
        }

        var events = sim.Tick();

        Assert.Contains(events, e => e is OrderExecuted { Unit.Value: 1 });
        Assert.Equal(new Cell(2, 0), sim.Get(1).Position);
        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Empty(sim.PlannedOrders());
    }

    [Fact]
    public void AnOrderImpossibleForAnotherReasonThanTime_IsRefusedRightAway()
    {
        var sim = Simulation(new Grid(5, 1), Unit(1, team: 0, 0, 0, gauge: 0), Unit(2, team: 1, 4, 0));

        sim.Submit(new OrderCommand(Strike(1, 2))); // hors de portée, sans déplacement
        var events = sim.Tick();

        Assert.Contains(events, e => e is CommandRejected { Reason: RejectionReason.OutOfRange });
        Assert.Empty(sim.PlannedOrders());
    }

    [Fact]
    public void AnOrderThatBecameImpossible_IsCancelledWhenItLeaves_AndTheGaugeStaysFull()
    {
        var sim = Simulation(new Grid(5, 2),
            Unit(1, team: 0, 0, 0, gauge: 0), Unit(2, team: 1, 4, 1), Unit(3, team: 0, 2, 1, gauge: Capacity));
        sim.Submit(new OrderCommand(new ActivateCommand(new UnitId(1), new Cell(2, 0))));
        sim.Tick();

        // Un allié prend la case avant que la jauge soit pleine.
        sim.Submit(new ActivateCommand(new UnitId(3), new Cell(2, 0)));
        while (!sim.Get(1).IsReady)
            sim.Tick();
        var events = sim.Tick();

        Assert.Contains(events, e => e is CommandRejected { Command: ActivateCommand { Unit.Value: 1 } });
        Assert.DoesNotContain(events, e => e is OrderExecuted);
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
        Assert.True(sim.Get(1).IsReady);
        Assert.Empty(sim.PlannedOrders());
    }

    [Fact]
    public void ANewOrder_ReplacesThePreviousOne_AndAnOrderCanBeCancelled()
    {
        var sim = Simulation(new Grid(5, 1), Unit(1, team: 0, 0, 0, gauge: 0), Unit(2, team: 1, 4, 0));
        var first = new ActivateCommand(new UnitId(1), new Cell(1, 0));
        var second = new ActivateCommand(new UnitId(1), new Cell(2, 0));

        sim.Submit(new OrderCommand(first));
        sim.Submit(new OrderCommand(second));
        Assert.Equal(new[] { second }, sim.PlannedOrders()); // avant même le tick (pendant la pause)
        sim.Tick();
        Assert.Equal(new[] { second }, sim.PlannedOrders());

        sim.Submit(new CancelOrderCommand(new UnitId(1)));
        Assert.Empty(sim.PlannedOrders());
        var events = sim.Tick();

        Assert.Contains(events, e => e is OrderCancelled { Unit.Value: 1 });
        Assert.Empty(sim.PlannedOrders());
    }

    [Fact]
    public void Orders_LeaveInTheOrderTheyWereGiven()
    {
        var sim = Simulation(new Grid(3, 3), Unit(1, team: 0, 0, 0, gauge: Capacity), Unit(2, team: 0, 2, 0, gauge: Capacity),
            Unit(3, team: 1, 1, 2));
        var second = new ActivateCommand(new UnitId(1), new Cell(1, 0));

        sim.Submit(new OrderCommand(new ActivateCommand(new UnitId(2), new Cell(1, 0))));
        sim.Submit(new OrderCommand(second));
        var events = sim.Tick();

        Assert.Equal(new Cell(1, 0), sim.Get(2).Position);
        Assert.Equal(new Cell(0, 0), sim.Get(1).Position);
        Assert.Contains(events, e => e is CommandRejected r && r.Command == second);
        Assert.True(sim.Get(1).IsReady);
    }

    [Fact]
    public void AnOrderForASkillOnCooldown_WaitsForTheCooldown()
    {
        var sim = Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, initiative: 100, gauge: Capacity), Unit(2, team: 1, 1, 0));
        var smash = new ActivateCommand(new UnitId(1), null, new SkillUse("smash", new UnitId(2)));
        sim.Submit(smash);
        sim.Tick();
        var cooldownEnd = sim.Get(1).CooldownEndTick("smash");

        sim.Submit(new OrderCommand(smash));
        sim.Tick();
        while (!sim.EventLog.Any(e => e is OrderExecuted))
            sim.Tick();

        Assert.True(sim.CurrentTick >= cooldownEnd);
        Assert.Equal(40, sim.Get(2).Hp);
        Assert.DoesNotContain(sim.EventLog, e => e is CommandRejected);
    }

    [Fact]
    public void Orders_AreReplayedIdentically()
    {
        var setup = Setup(new Grid(5, 2), Unit(1, team: 0, 0, 0, gauge: 0), Unit(2, team: 1, 3, 0), Unit(3, team: 0, 0, 1, gauge: 0));
        var original = new CombatSimulation(setup);
        original.Submit(new OrderCommand(Strike(1, 2, moveTo: new Cell(2, 0))));
        original.Submit(new OrderCommand(new ActivateCommand(new UnitId(3), new Cell(1, 1))));
        original.TickTimes(3);
        original.Submit(new CancelOrderCommand(new UnitId(3)));
        original.TickTimes(20);
        Assert.Contains(original.EventLog, e => e is OrderExecuted);
        Assert.Contains(original.EventLog, e => e is OrderCancelled);

        var replayed = CombatSimulation.Replay(setup, original.CommandLog, original.CurrentTick);

        Assert.Equal(original.EventLog, replayed.EventLog);
    }
}
