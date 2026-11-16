using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class PursuitTests
{
    private static readonly Cell Exit = new(9, 1);

    private static ObjectiveDefinition Pursuit(params Cell[] exits) =>
        new() { Kind = ObjectiveKind.Pursuit, Exits = exits.Length > 0 ? exits : new[] { Exit } };

    private static readonly ObjectiveDefinition Eliminate = new() { Kind = ObjectiveKind.Eliminate };

    private static UnitSetup Villain(int x, int y, int gauge = 0, int hp = 10, DefeatOutcome onDefeat = DefeatOutcome.Removed) =>
        Unit(2, team: 1, x, y, gauge: gauge, hp: hp, onDefeat: onDefeat, behavior: Behaviors.Basic) with { IsPursued = true };

    /// <summary>Un héros prêt en (0, 1), le vilain poursuivi, un sbire en (5, 2) ; la sortie en (9, 1).</summary>
    private static CombatSimulation Chase(UnitSetup villain, params ObjectiveDefinition[] objectives) =>
        new(Setup(new Grid(10, 3), Unit(1, team: 0, 0, 1, gauge: Capacity), villain, Unit(3, team: 1, 5, 2))
            with { Objectives = objectives.Length > 0 ? objectives : new[] { Pursuit() } });

    [Fact]
    public void ThePursuedVillain_FleesFromTheStart()
    {
        var sim = Chase(Villain(4, 1));

        Assert.True(sim.Get(2).IsFleeing);
        Assert.Equal(new HashSet<Cell> { Exit }, sim.PursuitExits);
    }

    [Fact]
    public void ThePursuedVillain_RunsToAnExit_NotToTheEdge()
    {
        // Au bord de la carte dès le départ : un fuyard ordinaire s'enfuirait sur place.
        var sim = Chase(Villain(4, 0, gauge: Capacity));

        sim.Tick();

        Assert.False(sim.Get(2).Escaped);
        Assert.Equal(7, sim.Get(2).Position.X); // 3 cases de mobilité vers la sortie
    }

    [Fact]
    public void ReachingAnExit_FailsThePursuit_AndAMainOneLosesTheCombat()
    {
        var sim = Chase(Villain(7, 1, gauge: Capacity));

        var events = sim.Tick();

        Assert.True(sim.Get(2).Escaped);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[0].State);
        Assert.False(sim.Outcome);
        Assert.Contains(new UnitEscaped(1, new UnitId(2)), events);
    }

    [Fact]
    public void FellingTheVillain_CompletesThePursuit_EvenWithMinionsLeft()
    {
        var sim = Chase(Villain(1, 1));
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(2)));

        sim.Tick();

        Assert.Equal(ObjectiveState.Completed, sim.Objectives[0].State);
        Assert.True(sim.Outcome);
        Assert.Equal(UnitStatus.Active, sim.Get(3).Status);
    }

    [Fact]
    public void AVillainDownedOrSurrendered_IsCaughtToo()
    {
        var downed = Chase(Villain(1, 1, onDefeat: DefeatOutcome.Downed));
        downed.Submit(Use(new UnitId(1), "strike", new UnitId(2)));
        downed.Tick();
        Assert.True(downed.Outcome);

        var surrendered = Chase(Villain(4, 1));
        surrendered.Get(2).Surrendered = true;
        surrendered.Get(2).Status = UnitStatus.Removed;
        surrendered.Tick();
        Assert.True(surrendered.Outcome);
    }

    [Fact]
    public void AHeroOnTheExit_BlocksIt()
    {
        var sim = new CombatSimulation(Setup(new Grid(10, 3), Unit(1, team: 0, 9, 1), Villain(8, 1, gauge: Capacity))
            with { Objectives = new[] { Pursuit() } });

        sim.TickTimes(5);

        Assert.False(sim.Get(2).Escaped);
        Assert.Null(sim.Outcome);
    }

    [Fact]
    public void WhenEveryExitIsHeld_TheVillainStillClosesIn()
    {
        var sim = new CombatSimulation(Setup(new Grid(10, 3), Unit(1, team: 0, 9, 1), Villain(2, 1, gauge: Capacity))
            with { Objectives = new[] { Pursuit() } });

        sim.Tick();

        Assert.Equal(5, sim.Get(2).Position.X); // 3 cases vers la sortie tenue, pour s'y glisser dès qu'elle se libère
    }

    [Fact]
    public void AtVictory_ASecondaryPursuitStillRunning_IsFailed()
    {
        var sim = Chase(Villain(4, 1), Eliminate, new ObjectiveDefinition { Kind = ObjectiveKind.Pursuit, Exits = new[] { Exit }, Secondary = true });
        sim.Get(3).Status = UnitStatus.Removed;

        sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[1].State); // il court encore : il s'échappe
    }

    [Fact]
    public void TheRunningVillain_DoesNotCountForElimination_ButThePursuitGoesOn()
    {
        var sim = Chase(Villain(4, 1), Eliminate, Pursuit());
        sim.Get(3).Status = UnitStatus.Removed;

        sim.Tick();

        Assert.Equal(ObjectiveState.Completed, sim.Objectives[0].State);
        Assert.Equal(ObjectiveState.InProgress, sim.Objectives[1].State);
        Assert.Null(sim.Outcome);
    }

    [Fact]
    public void ExitsGoWithAPursuitOnly()
    {
        Assert.Throws<ArgumentException>(() => new ObjectiveDefinition { Kind = ObjectiveKind.Pursuit }.Validate());
        Assert.Throws<ArgumentException>(() => new ObjectiveDefinition { Kind = ObjectiveKind.Pursuit, Exits = Array.Empty<Cell>() }.Validate());
        Assert.Throws<ArgumentException>(() => new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate, Exits = new[] { Exit } }.Validate());
    }

    public static TheoryData<string> InvalidChases => new()
    {
        "no pursued unit", "pursued without pursuit", "two pursued", "two pursuits", "pursued hero", "exit on a wall", "exit off the map",
        "starts on an exit",
    };

    [Theory]
    [MemberData(nameof(InvalidChases))]
    public void AChaseThatCannotWork_IsRejected(string invalid)
    {
        var grid = Grid.FromRows("..........", ".........#", "..........");
        var hero = Unit(1, team: 0, 0, 1);
        var villain = Villain(4, 1);
        var other = Unit(3, team: 1, 5, 2);
        var (units, objectives) = invalid switch
        {
            "no pursued unit" => (new[] { hero, villain with { IsPursued = false } }, new[] { Pursuit() }),
            "pursued without pursuit" => (new[] { hero, villain }, new[] { Eliminate }),
            "two pursued" => (new[] { hero, villain, other with { IsPursued = true } }, new[] { Pursuit() }),
            "two pursuits" => (new[] { hero, villain }, new[] { Pursuit(), Pursuit() }),
            "pursued hero" => (new[] { hero with { IsPursued = true } }, new[] { Pursuit() }),
            "exit on a wall" => (new[] { hero, villain }, new[] { Pursuit(new Cell(9, 1)) }),
            "starts on an exit" => (new[] { hero, villain }, new[] { Pursuit(new Cell(4, 1)) }),
            _ => (new[] { hero, villain }, new[] { Pursuit(new Cell(10, 1)) }),
        };

        Assert.Throws<ArgumentException>(() => new CombatSimulation(Setup(grid, units) with { Objectives = objectives }));
    }
}
