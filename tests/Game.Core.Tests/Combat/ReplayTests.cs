using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class ReplayTests
{
    [Fact]
    public void Replay_FromInitialStateAndCommandLog_ReproducesTheCombat()
    {
        var grid = Grid.FromRows(
            "........",
            "..##....",
            "........",
            "....#...");
        UnitSetup[] units =
        {
            Unit(1, team: 0, 0, 0, initiative: 20, mobility: 3),
            Unit(2, team: 0, 0, 2, initiative: 15, mobility: 4),
            Unit(3, team: 1, 7, 0, initiative: 12, mobility: 2),
            Unit(4, team: 1, 7, 3, initiative: 25, mobility: 3),
        };
        var original = new CombatSimulation(Rules, grid, units);

        // Chaque unité prête avance d'une case vers l'autre camp ; quelques commandes sont refusées.
        for (var tick = 0; tick < 60; tick++)
        {
            foreach (var unit in original.Units.Where(u => u.IsReady))
            {
                var step = unit.Team == 0 ? 1 : -1;
                original.Submit(new ActivateCommand(unit.Id, new Cell(unit.Position.X + step, unit.Position.Y)));
            }
            original.Tick();
        }
        Assert.Contains(original.EventLog, e => e is UnitMoved);
        Assert.Contains(original.EventLog, e => e is CommandRejected);

        var replayed = CombatSimulation.Replay(Rules, grid, units, original.CommandLog, original.CurrentTick);

        Assert.Equal(original.EventLog, replayed.EventLog);
        Assert.Equal(
            original.Units.Select(u => (u.Id, u.Position, u.Gauge)),
            replayed.Units.Select(u => (u.Id, u.Position, u.Gauge)));
    }

    [Fact]
    public void Replay_RejectsUnorderedCommandLog()
    {
        UnitSetup[] units = { Unit(1, team: 0, 0, 0) };
        var log = new[]
        {
            new LoggedCommand(5, new ActivateCommand(new UnitId(1), new Cell(1, 0))),
            new LoggedCommand(3, new ActivateCommand(new UnitId(1), new Cell(2, 0))),
        };

        Assert.Throws<ArgumentException>(() => CombatSimulation.Replay(Rules, new Grid(3, 1), units, log, untilTick: 10));
    }

    [Fact]
    public void Logs_CannotBeModifiedFromOutside()
    {
        var sim = new CombatSimulation(Rules, new Grid(3, 1), new[] { Unit(1, team: 0, 0, 0) });

        Assert.IsNotType<List<CombatEvent>>(sim.EventLog);
        Assert.IsNotType<List<LoggedCommand>>(sim.CommandLog);
        Assert.IsNotType<List<Unit>>(sim.Units);
    }
}
