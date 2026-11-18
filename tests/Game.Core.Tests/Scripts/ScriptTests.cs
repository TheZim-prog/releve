using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Scripts;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

public class ScriptTests
{
    private static readonly UnitId Hero = new(1);

    private static ScriptTrigger Trigger(string id, ScriptCondition? when, params ScriptEffect[] effects) =>
        new() { Id = id, When = when, Do = effects };

    private static ScriptEffect Say(string text, bool freeze = false, string? speaker = null) =>
        new() { Say = text, Freeze = freeze, Speaker = speaker };

    /// <summary>Un héros (1) à jauge vide, un voyou (2, repère « thug ») à son contact, et le script.</summary>
    private static CombatSetup WithScript(params ScriptTrigger[] script) =>
        Setup(new Grid(6, 3), Unit(1, team: 0, 0, 1), Unit(2, team: 1, 1, 1, hp: 10) with { Tag = "thug" })
            with { Script = script };

    private static IEnumerable<string> Said(IEnumerable<CombatEvent> events) =>
        events.OfType<ScriptMessageShown>().Select(e => e.Message.Text);

    [Fact]
    public void ATriggerWithoutCondition_SpeaksBeforeTheFirstTick_Once()
    {
        var sim = new CombatSimulation(WithScript(Trigger("intro", null, Say("Bonjour", speaker: "Télépathe"))));

        var first = sim.Tick();
        var later = sim.TickTimes(5);

        Assert.Equal(new ScriptMessageShown(0, new ScriptMessage(1, "Télépathe", "Bonjour", Freezes: false)), first[0]);
        Assert.Empty(Said(later));
    }

    [Fact]
    public void ATimedTrigger_WaitsForItsTime()
    {
        var sim = new CombatSimulation(WithScript(Trigger("late", new ScriptCondition { AtMs = 200 }, Say("Enfin"))));

        Assert.Empty(Said(sim.TickTimes(3)));
        Assert.Equal(new[] { "Enfin" }, Said(sim.Tick())); // 200 ms = tick 4
    }

    [Fact]
    public void AFrozenBriefing_ComesBeforeAnyoneActs()
    {
        // Le voyou, IA à jauge pleine, frapperait le héros dès le premier tick.
        var setup = WithScript(Trigger("intro", null, Say("Écoutez", freeze: true)))
            with { Units = new[] { Unit(1, team: 0, 0, 1), Unit(2, team: 1, 1, 1, gauge: Capacity, behavior: "basic") } };
        var sim = new CombatSimulation(setup);

        var events = sim.TickTimes(5);

        Assert.True(sim.IsTimeFrozen);
        Assert.Equal(0, sim.CurrentTick);
        Assert.DoesNotContain(events, e => e is SkillUsed or UnitMoved);
    }

    [Fact]
    public void AFrozenMessage_StopsTime_UntilAPlayerContinues()
    {
        var sim = new CombatSimulation(WithScript(Trigger("intro", new ScriptCondition { AtMs = 50 }, Say("Écoutez", freeze: true))));
        sim.Tick();
        var gauge = sim.Get(1).Gauge;

        sim.TickTimes(10);

        Assert.True(sim.IsTimeFrozen);
        Assert.Equal((1, 11), (sim.CurrentTick, sim.Step));
        Assert.Equal(gauge, sim.Get(1).Gauge);

        sim.Submit(new ContinueCommand(1));
        var resumed = sim.Tick();
        sim.Tick();

        Assert.Contains(new TimeResumed(1, 1), resumed);
        Assert.False(sim.IsTimeFrozen);
        Assert.Null(sim.FrozenMessage);
        Assert.Equal(2, sim.CurrentTick);
        Assert.True(sim.Get(1).Gauge > gauge);
    }

    [Fact]
    public void WhileFrozen_ActionsAreRefused_ButOrdersWaitForTime()
    {
        var setup = WithScript(Trigger("intro", null, Say("Écoutez", freeze: true)))
            with { Units = new[] { Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 1, 4, 1) } };
        var sim = new CombatSimulation(setup);
        sim.Tick();

        sim.Submit(new ActivateCommand(Hero, new Cell(1, 1)));
        var refused = sim.Tick();
        sim.Submit(new OrderCommand(new ActivateCommand(Hero, new Cell(1, 1))));
        sim.Tick();

        Assert.Contains(refused, e => e is CommandRejected { Reason: RejectionReason.TimeFrozen });
        Assert.Equal(new Cell(0, 1), sim.Get(1).Position);
        Assert.Single(sim.PlannedOrders());

        sim.Submit(new ContinueCommand(1));
        sim.TickTimes(2);

        Assert.Equal(new Cell(1, 1), sim.Get(1).Position);
    }

    [Fact]
    public void WhileFrozen_AnImpossibleAction_GivesItsRealReason()
    {
        var setup = WithScript(Trigger("intro", null, Say("Écoutez", freeze: true)))
            with { Units = new[] { Unit(1, team: 0, 0, 1, gauge: Capacity, mobility: 1), Unit(2, team: 1, 5, 1) } };
        var sim = new CombatSimulation(setup);
        sim.Tick();

        sim.Submit(new ActivateCommand(Hero, new Cell(4, 1)));

        Assert.Contains(sim.Tick(), e => e is CommandRejected { Reason: RejectionReason.Unreachable });
    }

    [Fact]
    public void AStaleContinue_DoesNothing()
    {
        var sim = new CombatSimulation(WithScript(
            Trigger("one", null, Say("Un", freeze: true)),
            Trigger("two", new ScriptCondition { After = "one" }, Say("Deux", freeze: true))));
        sim.Tick();

        // Deux joueurs continuent le premier message au même moment : le second clic ne saute pas le message suivant.
        sim.Submit(new ContinueCommand(1));
        sim.Submit(new ContinueCommand(1));
        sim.Tick();

        Assert.Equal("Deux", sim.FrozenMessage?.Text);
    }

    [Fact]
    public void EffectsAfterAFrozenMessage_WaitForContinue()
    {
        var sim = new CombatSimulation(WithScript(Trigger("intro", null, Say("Un", freeze: true), Say("Deux"))));

        Assert.Equal(new[] { "Un" }, Said(sim.Tick()));
        sim.Submit(new ContinueCommand(1));

        Assert.Equal(new[] { "Deux" }, Said(sim.Tick()));
    }

    [Fact]
    public void AFollowingTrigger_StartsWhenTheFirstIsDone_AtTheSameStep()
    {
        var sim = new CombatSimulation(WithScript(
            Trigger("second", new ScriptCondition { After = "first" }, Say("Deux")),
            Trigger("first", null, Say("Un"))));

        Assert.Equal(new[] { "Un", "Deux" }, Said(sim.Tick()));
    }

    [Fact]
    public void AFollowingTrigger_WaitsForAFrozenMessageToBeContinued()
    {
        var sim = new CombatSimulation(WithScript(
            Trigger("first", null, Say("Un", freeze: true)),
            Trigger("second", new ScriptCondition { After = "first" }, Say("Deux"))));
        sim.TickTimes(5);

        Assert.Equal(1, sim.FrozenMessage?.Number);
        sim.Submit(new ContinueCommand(1));
        Assert.Equal(new[] { "Deux" }, Said(sim.Tick()));
    }

    [Fact]
    public void AUnitDownTrigger_StartsWhenTheTaggedUnitFalls()
    {
        var setup = WithScript(Trigger("victory", new ScriptCondition { UnitDown = "thug" }, Say("Il est tombé")))
            with { Units = new[] { Unit(1, team: 0, 0, 1, gauge: Capacity), Unit(2, team: 1, 1, 1, hp: 10) with { Tag = "thug" } } };
        var sim = new CombatSimulation(setup);

        Assert.Empty(Said(sim.Tick()));
        sim.Submit(Use(Hero, "strike", new UnitId(2)));

        Assert.Equal(new[] { "Il est tombé" }, Said(sim.Tick()));
    }

    [Fact]
    public void AFall_StillCounts_AfterTheUnitIsRevived()
    {
        // Le voyou (3) met l'allié (2) à terre, le héros (1) le relève ; le déclencheur attend aussi 500 ms.
        var setup = Setup(new Grid(6, 3),
                Unit(1, team: 0, 0, 1, gauge: Capacity),
                Unit(2, team: 0, 1, 1, hp: 10, onDefeat: DefeatOutcome.Downed) with { Tag = "ally" },
                Unit(3, team: 1, 2, 1, gauge: Capacity))
            with { Script = new[] { Trigger("late", new ScriptCondition { UnitDown = "ally", AtMs = 500 }, Say("Il était tombé")) } };
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(new UnitId(3), "strike", new UnitId(2)));
        sim.Tick();
        sim.Submit(new ActivateCommand(Hero, null, Interaction: new InteractionUse(InteractionKind.Revive, new UnitId(2))));
        sim.Tick();
        Assert.Equal(UnitStatus.Active, sim.Get(2).Status);

        Assert.Equal(new[] { "Il était tombé" }, Said(sim.TickTimes(10)));
    }

    [Fact]
    public void AllConditions_MustBeMet()
    {
        var sim = new CombatSimulation(WithScript(
            Trigger("first", null, Say("Un")),
            Trigger("both", new ScriptCondition { After = "first", AtMs = 100 }, Say("Deux"))));

        Assert.Equal(new[] { "Un" }, Said(sim.Tick()));
        Assert.Equal(new[] { "Deux" }, Said(sim.Tick()));
    }

    [Fact]
    public void AFrozenCombat_ReplaysIdentically()
    {
        var setup = WithScript(
            Trigger("intro", null, Say("Un", freeze: true)),
            Trigger("later", new ScriptCondition { AtMs = 300 }, Say("Deux", freeze: true)));
        var sim = new CombatSimulation(setup);
        for (var step = 0; step < 40; step++)
        {
            if (step % 7 == 6 && sim.FrozenMessage is { } message)
                sim.Submit(new ContinueCommand(message.Number));
            sim.Tick();
        }

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, sim.Step);

        Assert.True(sim.CurrentTick < sim.Step);
        Assert.Equal(sim.EventLog, replay.EventLog);
        Assert.Equal(sim.Checksum(), replay.Checksum());
    }
}
