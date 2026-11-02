using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class GaugeTests
{
    [Fact]
    public void Gauge_FillsByInitiativeEachTick_AndAnnouncesReadyOnce()
    {
        var sim = Simulation(new Grid(3, 3), Unit(1, team: 0, 0, 0, initiative: 30));
        var unit = sim.Get(1);

        sim.TickTimes(3);
        Assert.Equal(90, unit.Gauge);
        Assert.False(unit.IsReady);

        Assert.Equal(new CombatEvent[] { new UnitReady(4, new UnitId(1)) }, sim.Tick());
        Assert.Equal(Capacity, unit.Gauge); // plafonnée, pas 120
        Assert.True(unit.IsReady);
    }

    [Fact]
    public void ReadyUnit_StaysReady_UntilItActs()
    {
        var sim = Simulation(new Grid(3, 3), Unit(1, team: 0, 0, 0, initiative: 50));
        sim.TickTimes(2);

        var events = sim.TickTimes(100);

        Assert.Empty(events);
        Assert.True(sim.Get(1).IsReady);
        Assert.Equal(Capacity, sim.Get(1).Gauge);
    }

    [Fact]
    public void HigherInitiative_IsReadyEarlier()
    {
        var sim = Simulation(new Grid(3, 3),
            Unit(1, team: 0, 0, 0, initiative: 25),
            Unit(2, team: 0, 1, 0, initiative: 50));

        var events = sim.TickTimes(4);

        Assert.Equal(
            new CombatEvent[] { new UnitReady(2, new UnitId(2)), new UnitReady(4, new UnitId(1)) },
            events);
    }

    [Fact]
    public void UnitsReadyOnTheSameTick_AreAnnouncedInSetupOrder()
    {
        var sim = Simulation(new Grid(3, 3),
            Unit(7, team: 0, 0, 0, initiative: 50),
            Unit(3, team: 1, 2, 2, initiative: 50));

        Assert.Equal(
            new CombatEvent[] { new UnitReady(2, new UnitId(7)), new UnitReady(2, new UnitId(3)) },
            sim.TickTimes(2));
    }

    [Fact]
    public void InitialGauge_CountsTowardsReadiness()
    {
        var sim = Simulation(new Grid(3, 3), Unit(1, team: 0, 0, 0, initiative: 10, gauge: 95));

        Assert.Equal(new CombatEvent[] { new UnitReady(1, new UnitId(1)) }, sim.Tick());
    }

    [Fact]
    public void ZeroInitiative_NeverFills()
    {
        var sim = Simulation(new Grid(3, 3), Unit(1, team: 0, 0, 0, initiative: 0));

        Assert.Empty(sim.TickTimes(50));
        Assert.Equal(0, sim.Get(1).Gauge);
    }

    [Fact]
    public void Setup_RejectsInvalidUnits()
    {
        var grid = Grid.FromRows("..#");

        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0), Unit(1, 0, 1, 0)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 2, 0)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0), Unit(2, 1, 0, 0)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0, initiative: -1)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0, mobility: -1)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0, initiative: Capacity + 1)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0, gauge: Capacity + 1)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0, hp: 0)));
        Assert.Throws<ArgumentException>(() => Simulation(grid, Unit(1, 0, 0, 0, skills: new[] { Strike, Strike })));
    }
}
