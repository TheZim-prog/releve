using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class EvacuationTests
{
    private static ObjectiveDefinition Rescue(int minimum, bool secondary = false) =>
        new() { Kind = ObjectiveKind.Rescue, Minimum = minimum, Secondary = secondary };

    private static readonly ObjectiveDefinition Eliminate = new() { Kind = ObjectiveKind.Eliminate };

    private static UnitSetup Civilian(int id, int x, int y, int gauge = 0)
    {
        var setup = Unit(id, team: 2, x, y, gauge: gauge);
        return setup with
        {
            Definition = setup.Definition with
            {
                IsCivilian = true, Behavior = Behaviors.Flee, Skills = Array.Empty<SkillDefinition>(), OpportunityAttacksPerCycle = 0,
            },
        };
    }

    /// <summary>Un héros, un voyou au loin, trois civils (4, 5, 6) au milieu de la carte.</summary>
    private static CombatSimulation Street(params ObjectiveDefinition[] objectives) =>
        new(Setup(new Grid(9, 5), Unit(1, team: 0, 0, 2, gauge: Capacity), Unit(2, team: 1, 8, 2, hp: 10),
                Civilian(4, 4, 1), Civilian(5, 4, 2), Civilian(6, 4, 3))
            with { Objectives = objectives });

    private static void Evacuate(CombatSimulation sim, int id)
    {
        sim.Get(id).Escaped = true;
        sim.Get(id).Status = UnitStatus.Removed;
    }

    private static void Lose(CombatSimulation sim, int id) => sim.Get(id).Status = UnitStatus.Removed;

    [Fact]
    public void EvacuatingEnoughCivilians_CompletesTheRescue()
    {
        var sim = Street(Rescue(2));
        Evacuate(sim, 4);
        sim.Tick();
        Assert.Equal(ObjectiveState.InProgress, sim.Objectives[0].State);

        Evacuate(sim, 5);
        var events = sim.Tick();

        Assert.Equal(ObjectiveState.Completed, sim.Objectives[0].State);
        Assert.True(sim.Outcome); // le seul objectif principal : la mission est réussie, voyou ou pas
        Assert.Equal(new CombatEvent[] { new ObjectiveCompleted(2, 0), new CombatEnded(2, true) }, events.TakeLast(2));
    }

    [Fact]
    public void ACivilianWhoReachesTheEdge_IsEvacuated()
    {
        var sim = new CombatSimulation(Setup(new Grid(9, 5), Unit(1, team: 0, 0, 2), Unit(2, team: 1, 8, 2), Civilian(4, 4, 0, gauge: Capacity))
            with { Objectives = new[] { Rescue(1) } });

        sim.Tick();

        Assert.Equal(new RescueTally(Evacuated: 1, OnMap: 0, Lost: 0), RescueTally.Of(sim));
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void LosingTooManyCivilians_FailsTheRescue_AndAMainOneLosesTheCombat()
    {
        var sim = Street(Eliminate, Rescue(2));
        Lose(sim, 4);
        sim.Tick();
        Assert.Null(sim.Outcome); // encore possible : 2 civils sur la carte

        Lose(sim, 5);
        var events = sim.Tick();

        Assert.Equal(new RescueTally(Evacuated: 0, OnMap: 1, Lost: 2), RescueTally.Of(sim));
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[1].State);
        Assert.False(sim.Outcome);
        Assert.Contains(new ObjectiveFailed(2, 1), events);
    }

    [Fact]
    public void ASecondaryRescueThatFails_DoesNotLoseTheCombat()
    {
        var sim = Street(Eliminate, Rescue(3, secondary: true));
        Lose(sim, 4);
        sim.Tick();
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[1].State);
        Assert.Null(sim.Outcome);

        Lose(sim, 2);
        sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(ObjectiveState.Failed, sim.Objectives[1].State);
    }

    [Fact]
    public void AtVictory_CiviliansStillOnTheMap_CountAsEvacuated()
    {
        var sim = Street(Eliminate, Rescue(2, secondary: true));
        Lose(sim, 4);
        Lose(sim, 2); // le voyou tombe : plus de danger

        var events = sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(ObjectiveState.Completed, sim.Objectives[1].State); // 0 évacué, mais 2 sains et saufs sur la carte
        Assert.Equal(new CombatEvent[] { new ObjectiveCompleted(1, 0), new ObjectiveCompleted(1, 1), new CombatEnded(1, true) },
            events.TakeLast(3));
    }

    [Fact]
    public void OnceNoEnemyIsLeft_CiviliansStillOnTheMapAreSafe_AndAMainRescueSucceeds()
    {
        var sim = Street(Eliminate, Rescue(2));
        Lose(sim, 2); // le voyou tombe : pas besoin d'attendre que les civils sortent à pied

        sim.Tick();

        Assert.Equal(ObjectiveState.Completed, sim.Objectives[1].State);
        Assert.True(sim.Outcome);
    }

    [Fact]
    public void AtVictory_ASecondaryRescueStillInProgress_CountsCiviliansOnTheMap()
    {
        // Mission réussie (un civil évacué) alors que le voyou est encore debout : les deux autres civils sont saufs.
        var sim = Street(Rescue(1), Rescue(3, secondary: true));
        Evacuate(sim, 4);

        var events = sim.Tick();

        Assert.True(sim.Outcome);
        Assert.Equal(UnitStatus.Active, sim.Get(2).Status);
        Assert.Equal(new CombatEvent[] { new ObjectiveCompleted(1, 0), new ObjectiveCompleted(1, 1), new CombatEnded(1, true) },
            events.TakeLast(3));
    }

    [Theory]
    [InlineData(ObjectiveKind.Rescue, null)] // un sauvetage sans minimum
    [InlineData(ObjectiveKind.Rescue, 0)]
    [InlineData(ObjectiveKind.Eliminate, 2)] // un minimum sur autre chose qu'un sauvetage
    public void AMinimum_GoesWithARescueOnly(ObjectiveKind kind, int? minimum)
    {
        Assert.Throws<ArgumentException>(() => new ObjectiveDefinition { Kind = kind, Minimum = minimum }.Validate());
    }

    [Fact]
    public void ARescue_CannotAskForMoreCiviliansThanThereAre()
    {
        Assert.Throws<ArgumentException>(() => Street(Rescue(4)));
    }
}
