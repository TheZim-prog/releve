using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class EliminationTests
{
    private static CombatSetup HeroFacingThugSetup() =>
        Setup(new Grid(4, 1), Unit(1, team: 0, 0, 0, gauge: Capacity), Unit(2, team: 1, 1, 0, hp: 10))
            with { Objectives = ObjectiveDefinition.Default };

    private static CombatSimulation HeroFacingThug() => new(HeroFacingThugSetup());

    [Fact]
    public void ByDefault_TheCombatIsToEliminateEveryEnemy()
    {
        var sim = new CombatSimulation(new CombatSetup(Rules, new Grid(4, 1), new[] { Unit(1, team: 0, 0, 0) }, Seed: 1));

        var objective = Assert.Single(sim.Objectives);
        Assert.Equal(ObjectiveKind.Eliminate, objective.Definition.Kind);
        Assert.Equal(ObjectiveState.InProgress, objective.State);
        Assert.Null(sim.Outcome);
    }

    [Fact]
    public void FellingTheLastEnemy_CompletesTheObjective_AndWinsTheCombat()
    {
        var sim = HeroFacingThug();
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(2)));

        var events = sim.Tick();

        Assert.Equal(ObjectiveState.Completed, sim.Objectives[0].State);
        Assert.True(sim.Outcome);
        Assert.Equal(new CombatEvent[] { new ObjectiveCompleted(1, 0), new CombatEnded(1, true) }, events.TakeLast(2));
    }

    [Fact]
    public void AnEnemyThatEscaped_CountsAsEliminated()
    {
        var sim = HeroFacingThug();
        sim.Get(2).Escaped = true;
        sim.Get(2).Status = UnitStatus.Removed;

        sim.Tick();

        Assert.True(sim.Outcome);
    }

    [Fact]
    public void AnEnemyThatOnlyFlees_NoLongerCounts()
    {
        var sim = HeroFacingThug();
        sim.Get(2).IsFleeing = true; // pas encore au bord : il court

        sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(UnitStatus.Active, sim.Get(2).Status);
    }

    [Fact]
    public void WhenEveryHeroIsDown_TheCombatIsLost()
    {
        var sim = HeroFacingThug();
        sim.Get(1).Status = UnitStatus.Downed;

        var events = sim.Tick();

        Assert.False(sim.Outcome);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[0].State); // à la défaite, ce qui restait est raté
        Assert.Equal(new CombatEvent[] { new ObjectiveFailed(1, 0), new CombatEnded(1, false) }, events.TakeLast(2));
    }

    [Fact]
    public void WhenTheLastHeroAndTheLastEnemyFallTogether_TheCombatIsLost()
    {
        var sim = HeroFacingThug();
        sim.Get(1).Status = UnitStatus.Downed;
        sim.Get(2).Status = UnitStatus.Removed;

        var events = sim.Tick();

        Assert.False(sim.Outcome);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[0].State); // rien ne se réussit sur une défaite
        Assert.DoesNotContain(events, e => e is ObjectiveCompleted);
    }

    [Fact]
    public void AtVictory_ASecondaryEliminationNotDone_IsFailed()
    {
        // Mission réussie (un témoin protégé 50 ms, soit un tick) alors que le voyou est encore debout.
        var witness = Unit(5, team: 0, 1, 0, behavior: Behaviors.Cautious) with { IsProtected = true };
        var sim = new CombatSimulation(Setup(new Grid(8, 1), Unit(1, team: 0, 0, 0), witness, Unit(2, team: 1, 7, 0)) with
        {
            Objectives = new[]
            {
                new ObjectiveDefinition { Kind = ObjectiveKind.Protect, Until = ProtectUntil.Time, DurationMs = 50 },
                new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate, Secondary = true },
            },
        });

        sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[1].State);
    }

    [Fact]
    public void TheOutcome_NoLongerChangesOnceTheCombatIsOver()
    {
        var sim = HeroFacingThug();
        sim.Get(1).Status = UnitStatus.Downed;
        sim.Tick();
        sim.Get(1).Status = UnitStatus.Active;
        sim.Get(2).Status = UnitStatus.Removed;

        var events = sim.TickTimes(3);

        Assert.False(sim.Outcome);
        Assert.DoesNotContain(events, e => e is ObjectiveCompleted or CombatEnded);
    }

    [Fact]
    public void AReplay_EndsTheSameWay()
    {
        var sim = HeroFacingThug();
        sim.Submit(Use(new UnitId(1), "strike", new UnitId(2)));
        sim.Tick();

        var replay = CombatSimulation.Replay(HeroFacingThugSetup(), sim.CommandLog, sim.CurrentTick);

        Assert.True(replay.Outcome);
        Assert.Equal(sim.Checksum(), replay.Checksum());
    }

    [Fact]
    public void ObjectivesThatAreAllSecondary_AreRejected()
    {
        var secondaryOnly = new[] { new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate, Secondary = true } };

        Assert.Throws<ArgumentException>(() => new CombatSimulation(HeroFacingThugSetup() with { Objectives = secondaryOnly }));
    }

    [Fact]
    public void WithoutAnyObjective_TheCombatNeverEnds()
    {
        var sim = Simulation(new Grid(4, 1), Unit(1, team: 0, 0, 0, hp: 10));
        sim.Get(1).Status = UnitStatus.Downed;

        var events = sim.TickTimes(3);

        Assert.Empty(sim.Objectives);
        Assert.Null(sim.Outcome);
        Assert.DoesNotContain(events, e => e is CombatEnded);
    }
}
