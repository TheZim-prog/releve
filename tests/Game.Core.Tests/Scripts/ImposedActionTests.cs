using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Scripts;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

public class ImposedActionTests
{
    private static readonly UnitId Fighter = new(1), Runner = new(2), ThugA = new(3), ThugB = new(4);

    private static ScriptTrigger Trigger(string id, ScriptCondition? when, params ScriptEffect[] effects) =>
        new() { Id = id, When = when, Do = effects };

    private static ScriptEffect Impose(string? say, params ImposedAction[] actions) => new() { Say = say, Impose = actions };

    private static ImposedAction Strike(string unit, string target) => new() { Unit = unit, Skill = "strike", Target = target };

    /// <summary>
    /// Deux héros à jauge vide, « fighter » (1) en (0, 1) et « runner » (2) en (0, 3), et deux voyous de 10 PV, « a » (3)
    /// au contact du premier et « b » (4) à trois cases du second.
    /// </summary>
    private static CombatSetup Fight(params ScriptTrigger[] script) =>
        Setup(new Grid(8, 5),
                Unit(1, team: 0, 0, 1) with { Tag = "fighter" },
                Unit(2, team: 0, 0, 3) with { Tag = "runner" },
                Unit(3, team: 1, 1, 1, hp: 10) with { Tag = "a" },
                Unit(4, team: 1, 3, 3, hp: 10) with { Tag = "b" })
            with { Script = script };

    private static CombatSimulation Imposing(params ImposedAction[] actions) =>
        new(Fight(Trigger("lesson", null, Impose("Frappez !", actions)), Trigger("after", new ScriptCondition { After = "lesson" },
            new ScriptEffect { Say = "Bravo" })));

    [Fact]
    public void AnImposedAction_FreezesTime_FillsTheGauge_AndShowsTheInstruction()
    {
        var sim = Imposing(Strike("fighter", "a"));

        var events = sim.TickTimes(3);

        Assert.True(sim.IsTimeFrozen);
        Assert.Equal(0, sim.CurrentTick);
        Assert.True(sim.Get(1).IsReady);
        Assert.Contains(new ActionImposed(0, Fighter), events);
        Assert.Equal("Frappez !", sim.Instruction?.Text);
        Assert.Equal(new Imposition(Fighter, "strike", ThugA, null, null), Assert.Single(sim.ImposedActions));
    }

    [Fact]
    public void OnlyTheImposedAction_IsAccepted()
    {
        var sim = Imposing(Strike("fighter", "a"));
        sim.Tick();

        sim.Submit(new ActivateCommand(Fighter, new Cell(0, 0)));
        sim.Submit(Use(Fighter, "smash", ThugA));
        sim.Submit(new OrderCommand(Use(Fighter, "strike", ThugA)));
        sim.Submit(new ActivateCommand(Runner, new Cell(1, 3)));
        var events = sim.Tick();

        var reasons = events.OfType<CommandRejected>().Select(r => r.Reason).ToList();
        Assert.Equal(new[]
        {
            RejectionReason.NotTheImposedAction, RejectionReason.NotTheImposedAction, RejectionReason.ImposedActionIsNow,
            RejectionReason.TimeFrozen, // l'autre héros n'a pas d'action imposée : il attend
        }, reasons);
        Assert.True(sim.IsTimeFrozen);
    }

    [Fact]
    public void DoingTheImposedAction_ResumesTime_AndTheScript()
    {
        var sim = Imposing(Strike("fighter", "a"));
        sim.Tick();

        sim.Submit(Use(Fighter, "strike", ThugA));
        var events = sim.Tick();

        Assert.Contains(events, e => e is SkillUsed { Unit.Value: 1 });
        Assert.Contains(new ImposedActionEnded(0, Fighter, Done: true), events);
        Assert.Contains(events, e => e is ScriptMessageShown { Message.Text: "Bravo" });
        Assert.False(sim.IsTimeFrozen);
        Assert.Null(sim.Instruction);
        sim.Tick();
        Assert.Equal(1, sim.CurrentTick);
    }

    [Fact]
    public void WithoutAnImposedCell_TheHeroMovesFreely_BeforeActing()
    {
        var sim = Imposing(Strike("runner", "b")); // « b » est à trois cases : il faut s'approcher
        sim.Tick();

        sim.Submit(new ActivateCommand(Runner, new Cell(2, 3), new SkillUse("strike", ThugB)));
        sim.Tick();

        Assert.Equal(UnitStatus.Removed, sim.Get(4).Status);
        Assert.False(sim.IsTimeFrozen);
    }

    [Fact]
    public void AnImposedCell_MustBeTheOneReached()
    {
        var sim = Imposing(new ImposedAction { Unit = "runner", MoveTo = new Cell(1, 4) });
        sim.Tick();

        sim.Submit(new ActivateCommand(Runner, new Cell(1, 3)));
        Assert.Contains(sim.Tick(), e => e is CommandRejected { Reason: RejectionReason.NotTheImposedAction });
        sim.Submit(new ActivateCommand(Runner, new Cell(1, 4)));
        sim.Tick();

        Assert.Equal(new Cell(1, 4), sim.Get(2).Position);
        Assert.False(sim.IsTimeFrozen);
    }

    [Fact]
    public void TimeResumes_OnlyWhenEveryImposedActionIsDone()
    {
        var sim = Imposing(Strike("fighter", "a"), Strike("runner", "b"));
        sim.Tick();

        sim.Submit(Use(Fighter, "strike", ThugA));
        sim.Tick();
        Assert.True(sim.IsTimeFrozen);
        Assert.Equal(Runner, Assert.Single(sim.ImposedActions).Unit);

        sim.Submit(new ActivateCommand(Runner, new Cell(2, 3), new SkillUse("strike", ThugB)));
        sim.Tick();
        Assert.False(sim.IsTimeFrozen);
    }

    [Fact]
    public void AnActionThatBecameImpossible_IsDropped()
    {
        // Les deux héros doivent frapper « a » ; le premier l'abat : le second n'a plus de cible.
        var sim = Imposing(Strike("fighter", "a"), Strike("runner", "a"));
        sim.Tick();

        sim.Submit(Use(Fighter, "strike", ThugA));
        var events = sim.Tick();

        Assert.Contains(new ImposedActionEnded(0, Runner, Done: false), events);
        Assert.False(sim.IsTimeFrozen);
    }

    [Fact]
    public void AnAlreadyImpossibleAction_DoesNotFreeze()
    {
        // « b » est hors d'atteinte : mobilité 1, et il est à six cases.
        var setup = Fight(Trigger("lesson", null, Impose(null, Strike("fighter", "b"))))
            with { Units = new[] { Unit(1, team: 0, 0, 1, mobility: 1) with { Tag = "fighter" }, Unit(4, team: 1, 6, 4, hp: 10) with { Tag = "b" } } };
        var sim = new CombatSimulation(setup);

        var events = sim.Tick();

        Assert.Contains(new ImposedActionEnded(0, Fighter, Done: false), events);
        Assert.False(sim.IsTimeFrozen);
        Assert.Equal(1, sim.CurrentTick);
        Assert.Equal(10, sim.Get(1).Gauge); // sa jauge d'avant (vide), plus le remplissage du tick : rien d'offert
    }

    [Fact]
    public void AnActionImposedOnAFallenHero_IsDroppedAtOnce()
    {
        var sim = new CombatSimulation(Fight(Trigger("lesson", null, Impose("Frappez !", Strike("fighter", "a")))));
        sim.Get(1).Status = UnitStatus.Downed;

        var events = sim.Tick();

        Assert.Contains(new ImposedActionEnded(0, Fighter, Done: false), events);
        Assert.DoesNotContain(events, e => e is ScriptMessageShown); // pas de consigne sans action
        Assert.False(sim.IsTimeFrozen);
    }

    [Fact]
    public void AConfusedHero_CannotObey_ItsActionIsDropped()
    {
        // Même joué par l'IA : confus, il n'obéit pas au script (il ne fait pas l'action à sa place).
        var setup = Fight(Trigger("lesson", null, Impose(null, Strike("fighter", "a"))))
            with { Units = new[] { Unit(1, team: 0, 0, 1, behavior: "basic") with { Tag = "fighter" }, Unit(3, team: 1, 1, 1, hp: 10) with { Tag = "a" } } };
        var sim = new CombatSimulation(setup);
        sim.Get(1).SetStatus(StatusKind.Confused, endTick: 100, source: null);

        var events = sim.Tick();

        Assert.Contains(new ImposedActionEnded(0, Fighter, Done: false), events);
        Assert.DoesNotContain(new ImposedActionEnded(0, Fighter, Done: true), events);
    }

    [Fact]
    public void CommandsOfTheStepThatEndsTheImposition_AreStillFrozen()
    {
        var setup = Fight(Trigger("lesson", null, Impose(null, Strike("fighter", "a"))))
            with { Units = new[] { Unit(1, team: 0, 0, 1) with { Tag = "fighter" }, Unit(2, team: 0, 0, 3, gauge: Capacity), Unit(3, team: 1, 1, 1, hp: 10) with { Tag = "a" } } };
        var sim = new CombatSimulation(setup);
        sim.Tick();

        sim.Submit(Use(Fighter, "strike", ThugA));
        sim.Submit(new ActivateCommand(Runner, new Cell(1, 3)));
        var events = sim.Tick();

        Assert.Contains(events, e => e is CommandRejected { Reason: RejectionReason.TimeFrozen });
        Assert.False(sim.IsTimeFrozen);
        Assert.Equal(new Cell(0, 3), sim.Get(2).Position);
    }

    [Fact]
    public void AHeroControlledByTheAi_DoesItsImposedActionItself()
    {
        var setup = Fight(Trigger("lesson", null, Impose(null, Strike("fighter", "a"))))
            with { Units = new[] { Unit(1, team: 0, 0, 1, behavior: "basic") with { Tag = "fighter" }, Unit(3, team: 1, 1, 1, hp: 10) with { Tag = "a" } } };
        var sim = new CombatSimulation(setup);

        var events = sim.Tick();

        Assert.Contains(new ImposedActionEnded(0, Fighter, Done: true), events);
        Assert.Equal(UnitStatus.Removed, sim.Get(3).Status);
    }

    [Fact]
    public void AnImposedHero_LosesItsOrderGivenInAdvance()
    {
        var sim = new CombatSimulation(Fight(Trigger("lesson", new ScriptCondition { AtMs = 100 }, Impose(null, Strike("fighter", "a")))));
        sim.Submit(new OrderCommand(new ActivateCommand(Fighter, new Cell(0, 0))));
        sim.Tick();

        var events = sim.Tick();

        Assert.Contains(new OrderCancelled(2, Fighter), events);
        Assert.Empty(sim.PlannedOrders());
    }

    [Fact]
    public void ACombatWithImposedActions_ReplaysIdentically()
    {
        var setup = Fight(Trigger("lesson", new ScriptCondition { AtMs = 200 }, Impose("Frappez !", Strike("fighter", "a"), Strike("runner", "b"))));
        var sim = new CombatSimulation(setup);
        sim.TickTimes(6);
        sim.Submit(Use(Fighter, "strike", ThugA));
        sim.TickTimes(3);
        sim.Submit(new ActivateCommand(Runner, new Cell(2, 3), new SkillUse("strike", ThugB)));
        sim.TickTimes(10);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, sim.Step);

        Assert.True(sim.CurrentTick < sim.Step);
        Assert.Equal(sim.EventLog, replay.EventLog);
        Assert.Equal(sim.Checksum(), replay.Checksum());
    }
}
