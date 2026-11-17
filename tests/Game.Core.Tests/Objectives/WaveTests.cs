using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class WaveTests
{
    /// <summary>Une vague arrive à 100 ms : au tick 2 (20 ticks/s).</summary>
    private static WaveSetup Wave(int atMs, params UnitSetup[] units) => new(atMs, units);

    /// <summary>Un héros en (0, 1), un voyou de 10 PV à son contact, et des renforts.</summary>
    private static CombatSimulation WithWaves(params WaveSetup[] waves) =>
        new(Setup(new Grid(8, 3), Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 1, 1, 1, hp: 10))
            with { Objectives = ObjectiveDefinition.Default, Waves = waves });

    [Fact]
    public void AWave_ArrivesOnTime_WithTheNextIds()
    {
        var sim = WithWaves(Wave(100, Unit(0, team: 1, 7, 0), Unit(0, team: 1, 7, 2)));

        Assert.DoesNotContain(sim.Tick(), e => e is WaveArrived);
        var events = sim.Tick();

        Assert.Equal(new WaveArrived(2, 1, new[] { new UnitId(3), new UnitId(4) }), events[0]);
        Assert.Equal(new Cell(7, 0), sim.Get(3).Position);
        Assert.Equal(new Cell(7, 2), sim.Get(4).Position);
        Assert.Equal((1, 1), (sim.WaveCount, sim.WavesArrived));
    }

    [Fact]
    public void AReinforcementWhoseCellIsTaken_ArrivesOnTheNearestFreeCell()
    {
        var sim = WithWaves(Wave(50, Unit(0, team: 1, 1, 1))); // la case du voyou

        sim.Tick();

        Assert.Equal(1, sim.Get(3).Position.DistanceTo(new Cell(1, 1))); // à un pas
    }

    [Fact]
    public void AReinforcement_NeverLandsBehindAWall()
    {
        // La case prévue est prise ; la plus proche à vol d'oiseau est derrière le mur, dans une poche fermée.
        var grid = Grid.FromRows("..###...", "..#.#...", "..###...");
        var sim = new CombatSimulation(Setup(grid, Unit(1, team: 0, 0, 1), Unit(2, team: 1, 1, 1))
            with { Waves = new[] { Wave(50, Unit(0, team: 1, 1, 1)) } });

        sim.Tick();

        Assert.NotEqual(new Cell(3, 1), sim.Get(3).Position);
        Assert.True(sim.Get(3).Position.X < 2, "The reinforcement crossed the wall.");
    }

    [Fact]
    public void AWaveWithNoRoom_ArrivesSilently()
    {
        var sim = new CombatSimulation(Setup(new Grid(2, 1), Unit(1, team: 0, 0, 0), Unit(2, team: 1, 1, 0))
            with { Waves = new[] { Wave(50, Unit(0, team: 1, 1, 0)) } });

        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is WaveArrived);
        Assert.Equal(2, sim.Units.Count);
        Assert.Equal(1, sim.WavesArrived);
    }

    [Fact]
    public void ReinforcementsWhoseLeaderHasFallen_ReactAtOnce()
    {
        var sim = new CombatSimulation(Setup(new Grid(8, 3), Unit(1, team: 0, 0, 1, gauge: Capacity),
                Unit(2, team: 1, 1, 1, hp: 10) with { MinionsOnFall = MinionReaction.Surrender })
            with { Objectives = ObjectiveDefinition.Default, Waves = new[] { Wave(100, Unit(0, team: 1, 7, 1)) } });
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(2)));
        sim.Tick();

        var events = sim.Tick();

        Assert.Contains(new UnitSurrendered(2, new UnitId(3)), events);
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void TheElimination_WaitsForTheEnemiesStillToCome()
    {
        var sim = WithWaves(Wave(200, Unit(0, team: 1, 7, 1)));
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(2)));

        sim.Tick();
        Assert.Equal(UnitStatus.Removed, sim.Get(2).Status);
        Assert.True(sim.EnemiesStillToCome);
        Assert.Null(sim.Outcome);

        sim.TickTimes(3); // la vague arrive au tick 4
        sim.Get(3).Status = UnitStatus.Removed;
        sim.Tick();
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void WavesArrive_InTheOrderOfTheirTime()
    {
        var sim = WithWaves(Wave(100, Unit(0, team: 1, 7, 1)), Wave(50, Unit(0, team: 1, 6, 1)));

        sim.TickTimes(2);

        Assert.Equal(new Cell(6, 1), sim.Get(3).Position); // la vague de 50 ms d'abord
        Assert.Equal(new Cell(7, 1), sim.Get(4).Position);
    }

    [Fact]
    public void AReplay_BringsTheSameReinforcements()
    {
        var setup = Setup(new Grid(8, 3), Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 1, 1, 1, hp: 10))
            with { Objectives = ObjectiveDefinition.Default, Waves = new[] { Wave(100, Unit(0, team: 1, 7, 1, behavior: Behaviors.Basic)) } };
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(2)));
        sim.TickTimes(30);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, sim.CurrentTick);

        Assert.Equal(sim.Checksum(), replay.Checksum());
        Assert.Equal(sim.EventLog, replay.EventLog);
    }

    [Theory]
    [InlineData(0, 7, 1)] // à l'instant 0
    [InlineData(100, 3, 1)] // sur un mur
    public void InvalidWaves_AreRejected(int atMs, int x, int y)
    {
        var grid = Grid.FromRows("........", "...#....", "........");

        Assert.Throws<ArgumentException>(() => new CombatSimulation(Setup(grid, Unit(1, team: 0, 0, 1))
            with { Waves = new[] { Wave(atMs, Unit(0, team: 1, x, y)) } }));
    }
}
