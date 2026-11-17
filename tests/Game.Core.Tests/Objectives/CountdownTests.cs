using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class CountdownTests
{
    private static readonly Cell Bomb = new(3, 1);

    /// <summary>Une bombe en (3, 1) qui demande deux interactions, et 1 s (20 ticks) de compte à rebours.</summary>
    private static ObjectiveDefinition Countdown(bool secondary = false, int interactions = 2) => new()
    {
        Kind = ObjectiveKind.Countdown, DurationMs = 1000, Secondary = secondary,
        Devices = new[] { new DeviceDefinition { X = Bomb.X, Y = Bomb.Y, Interactions = interactions } },
    };

    /// <summary>Deux héros prêts de part et d'autre de la bombe (2, 1) et (4, 1) ; un voyou au loin.</summary>
    private static CombatSimulation Defuse(params ObjectiveDefinition[] objectives) =>
        new(Setup(new Grid(8, 3), Unit(1, team: 0, 2, 1, gauge: Capacity), Unit(2, team: 1, 7, 0), Unit(4, team: 0, 4, 1, gauge: Capacity))
            with { Objectives = objectives.Length > 0 ? objectives : new[] { Countdown() } });

    private static ActivateCommand Operate(int unit, Cell cell, Cell? moveTo = null) =>
        new(new UnitId(unit), moveTo, Interaction: new InteractionUse(InteractionKind.Operate, Cell: cell));

    [Fact]
    public void ADevice_BlocksThePassage_ButNotTheSight()
    {
        var sim = Defuse();

        Assert.Equal(Terrain.Fixture, sim.Grid.TerrainAt(Bomb));
        Assert.False(sim.Grid.BlocksSight(Bomb));
        Assert.Null(sim.PathFor(new UnitId(1), Bomb));
        Assert.Same(sim.Devices[0], sim.DeviceAt(Bomb));
    }

    [Fact]
    public void Operating_IsALightActionAtContact_ThatAdvancesTheDevice()
    {
        var sim = Defuse();
        sim.Submit(Operate(1, Bomb));

        var events = sim.Tick();

        Assert.Contains(new DeviceOperated(1, new UnitId(1), Bomb, 1, 2), events);
        Assert.Equal(1, sim.Devices[0].Done);
        Assert.Equal(Rules.GaugeAt(Rules.LightActionRestartPercent) + 10, sim.Get(1).Gauge); // reprise légère, puis le tick la remplit
        Assert.Null(sim.Outcome);
    }

    [Fact]
    public void SeveralHeroes_ShareTheWork_AndNeutralizingEveryDeviceWins()
    {
        var sim = Defuse();
        sim.Submit(Operate(1, Bomb));
        sim.Submit(Operate(4, Bomb));

        var events = sim.Tick();

        Assert.True(sim.Devices[0].IsNeutralized);
        Assert.Equal(ObjectiveState.Completed, sim.Objectives[0].State);
        Assert.True(sim.Outcome); // le voyou est encore debout : seule la bombe comptait
        Assert.Contains(new ObjectiveCompleted(1, 0), events);
    }

    [Fact]
    public void Operating_NeedsContactWithAnUnfinishedDevice()
    {
        var sim = Defuse(Countdown(interactions: 1));

        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Operate(1, new Cell(2, 2)))); // pas d'objet là
        Assert.Equal(RejectionReason.OutOfRange, sim.Check(Operate(1, Bomb, moveTo: new Cell(1, 1))));
        Assert.Null(sim.Check(Operate(1, Bomb, moveTo: new Cell(2, 0)))); // en diagonale, au contact
        var corner = new CombatSimulation(Setup(Grid.FromRows("...#....", "........", "........"), Unit(1, team: 0, 2, 0, gauge: Capacity))
            with { Objectives = new[] { Countdown() } });
        Assert.Equal(RejectionReason.OutOfRange, corner.Check(Operate(1, Bomb))); // la diagonale coupe l'angle du mur

        sim.Submit(Operate(1, Bomb));
        sim.Tick();
        Assert.Equal(RejectionReason.InvalidTarget, sim.Check(Operate(4, Bomb))); // déjà neutralisé
    }

    [Fact]
    public void WhenTheCountdownRunsOut_TheObjectiveFails_AndAMainOneLosesTheCombat()
    {
        var sim = Defuse();

        sim.TickTimes(19);
        Assert.Null(sim.Outcome);
        var events = sim.Tick();

        Assert.Equal(ObjectiveState.Failed, sim.Objectives[0].State);
        Assert.False(sim.Outcome);
        Assert.Contains(new ObjectiveFailed(20, 0), events);
    }

    [Fact]
    public void OnTheTickTheCountdownRunsOut_ItIsTooLateToOperate()
    {
        var sim = Defuse(Countdown(interactions: 1));
        sim.Get(1).Gauge = 0;
        sim.Get(4).Gauge = 0;
        sim.TickTimes(19);
        sim.Get(1).Gauge = Capacity;
        sim.Submit(Operate(1, Bomb)); // appliquée au tick 20 : celui où le temps est écoulé

        var events = sim.Tick();

        Assert.Contains(events, e => e is CommandRejected { Reason: RejectionReason.InvalidTarget });
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[0].State);
    }

    [Fact]
    public void ADeviceWithNoFreeCellAround_IsRejected()
    {
        var walledIn = Grid.FromRows("..###...", "..#.#...", "..###...");

        Assert.Throws<ArgumentException>(() => new CombatSimulation(Setup(walledIn, Unit(1, team: 0, 0, 1), Unit(2, team: 1, 7, 0))
            with { Objectives = new[] { Countdown() } }));
    }

    [Fact]
    public void AtVictory_ASecondaryCountdownNotDone_IsFailed()
    {
        var sim = Defuse(new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate }, Countdown(secondary: true));
        sim.Get(2).Status = UnitStatus.Removed;

        sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[1].State);
    }

    [Theory]
    [InlineData(null, 2)]    // pas de durée
    [InlineData(0, 2)]
    [InlineData(1000, 0)]    // un objet sans interaction
    public void ACountdown_NeedsADurationAndDevicesThatTakeWork(int? durationMs, int interactions)
    {
        var objective = Countdown(interactions: interactions) with { DurationMs = durationMs };

        Assert.Throws<ArgumentException>(objective.Validate);
    }

    [Fact]
    public void DevicesAndDurations_GoWithACountdownOnly()
    {
        Assert.Throws<ArgumentException>(() => new ObjectiveDefinition { Kind = ObjectiveKind.Countdown, DurationMs = 1000 }.Validate());
        Assert.Throws<ArgumentException>(() => (Countdown() with { Kind = ObjectiveKind.Eliminate }).Validate());
    }

    [Theory]
    [InlineData(2, 1)] // sous un héros
    [InlineData(5, 0)] // sur un mur
    [InlineData(9, 1)] // hors de la carte
    public void ADevice_GoesOnAFreeCell(int x, int y)
    {
        var grid = Grid.FromRows(".....#..", "........", "........");
        var device = new DeviceDefinition { X = x, Y = y, Interactions = 1 };

        Assert.Throws<ArgumentException>(() => new CombatSimulation(Setup(grid, Unit(1, team: 0, 2, 1), Unit(2, team: 1, 7, 0))
            with { Objectives = new[] { Countdown() with { Devices = new[] { device } } } }));
    }

    [Fact]
    public void TwoDevices_CannotShareACell()
    {
        var device = new DeviceDefinition { X = 3, Y = 1, Interactions = 1 };

        Assert.Throws<ArgumentException>(() => Defuse(Countdown() with { Devices = new[] { device, device } }));
    }
}
