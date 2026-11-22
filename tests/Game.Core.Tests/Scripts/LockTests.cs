using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Scripts;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

public class LockTests
{
    private static readonly UnitId Hero = new(1), Slow = new(2), Thug = new(3);

    private static ScriptTrigger Trigger(string id, ScriptCondition? when, params ScriptEffect[] effects) =>
        new() { Id = id, When = when, Do = effects };

    private static readonly ScriptEffect LockOn = new() { Lock = true }, LockOff = new() { Lock = false };

    /// <summary>
    /// Un héros rapide « hero » (1, initiative 20) en (0, 1), un héros lent « slow » (2, initiative 10) en (0, 3), et un
    /// voyou « thug » (3, IA de base) au contact du premier ; tous à jauge vide.
    /// </summary>
    private static CombatSetup Fight(params ScriptTrigger[] script) =>
        Setup(new Grid(8, 5),
                Unit(1, team: 0, 0, 1, initiative: 20) with { Tag = "hero" },
                Unit(2, team: 0, 0, 3) with { Tag = "slow" },
                Unit(3, team: 1, 1, 1, behavior: Behaviors.Basic) with { Tag = "thug" })
            with { Script = script };

    private static CombatSimulation Locked(params ScriptTrigger[] more) =>
        new(Fight(new[] { Trigger("lock", null, LockOn) }.Concat(more).ToArray()));

    [Fact]
    public void UnderTheLock_TimeFlows_ButTheEnemiesWait()
    {
        var sim = Locked();

        var events = sim.TickTimes(30); // le voyou est prêt depuis le tick 10

        Assert.True(sim.IsLocked);
        Assert.False(sim.IsTimeFrozen);
        Assert.Equal(30, sim.CurrentTick);
        Assert.True(sim.Get(3).IsReady);
        Assert.Contains(new ScriptLockChanged(0, true), events);
        Assert.DoesNotContain(events, e => e is SkillUsed or UnitMoved);
    }

    [Fact]
    public void UnderTheLock_AHeroCanOnlyDoItsImposedAction()
    {
        var sim = Locked();
        sim.TickTimes(5);

        sim.Submit(Use(Hero, "strike", Thug));
        var events = sim.Tick();

        Assert.Equal(RejectionReason.ScriptLocked, Assert.Single(events.OfType<CommandRejected>()).Reason);
        Assert.Equal(100, sim.Get(3).Hp);
    }

    [Fact]
    public void UnderTheLock_AnOrderWaits_ForTheLockToBeLifted()
    {
        var sim = Locked(Trigger("free", new ScriptCondition { AtMs = 1000 }, LockOff));
        sim.Submit(new OrderCommand(Use(Hero, "strike", Thug)));

        sim.TickTimes(19); // prêt au tick 5, mais verrouillé jusqu'au tick 20
        Assert.Equal(100, sim.Get(3).Hp);

        var events = sim.TickTimes(2);

        Assert.Contains(new ScriptLockChanged(20, false), events);
        Assert.Equal(90, sim.Get(3).Hp);
    }

    [Fact]
    public void UnderTheLock_ImposedActions_AndOrders_StillWork()
    {
        var sim = Locked(
            Trigger("lesson", null, new ScriptEffect { Say = "Frappe !", Impose = new[] { new ScriptAction { Unit = "hero", Skill = "strike", Target = "thug" } } }),
            Trigger("riposte", new ScriptCondition { After = "lesson" },
                new ScriptEffect { Act = new[] { new ScriptAction { Unit = "thug", Skill = "strike", Target = "hero" } } }));
        sim.Tick();

        sim.Submit(Use(Hero, "strike", Thug));
        sim.Tick();

        Assert.Equal(90, sim.Get(3).Hp);
        Assert.Equal(90, sim.Get(1).Hp);
        Assert.True(sim.IsLocked);
    }

    [Fact]
    public void LiftingTheLock_GivesTheCombatBack_ToTheAi()
    {
        var sim = Locked(Trigger("free", new ScriptCondition { AtMs = 1000 }, LockOff));

        sim.TickTimes(20);
        Assert.Equal(100, sim.Get(1).Hp);
        sim.TickTimes(2);

        Assert.False(sim.IsLocked);
        Assert.True(sim.Get(1).Hp < 100);
    }

    [Fact]
    public void AGadget_IsNotHeldByTheLock()
    {
        var sim = Locked();
        var gadget = sim.Get(3);
        gadget.SummonedBy = Hero; // un gadget ennemi : il agit seul, verrou ou pas

        sim.TickTimes(11);

        Assert.True(sim.Get(1).Hp < 100);
    }

    [Fact]
    public void UnitReady_WaitsForTheUnitsGauge()
    {
        var sim = new CombatSimulation(Fight(
            Trigger("lock", null, LockOn),
            Trigger("fast", new ScriptCondition { UnitReady = "hero" }, new ScriptEffect { Say = "Rapide" }),
            Trigger("late", new ScriptCondition { UnitReady = "slow" }, new ScriptEffect { Say = "Enfin" })));

        var events = sim.TickTimes(10);

        // Le rapide est prêt au tick 5 (20 par tick), le lent au tick 10.
        Assert.Contains(events, e => e is ScriptMessageShown { Tick: 5, Message.Text: "Rapide" });
        Assert.Contains(events, e => e is ScriptMessageShown { Tick: 10, Message.Text: "Enfin" });
    }

    [Fact]
    public void UnitReady_IsNotMet_ByAStunnedUnit()
    {
        var sim = new CombatSimulation(Fight(
            Trigger("lock", null, LockOn),
            Trigger("ready", new ScriptCondition { UnitReady = "hero" }, new ScriptEffect { Say = "Prêt" })));
        sim.Get(1).Gauge = Capacity; // jauge pleine, mais étourdi jusqu'au tick 20
        sim.Get(1).SetStatus(StatusKind.Stunned, endTick: 20, source: null);

        var events = sim.TickTimes(20);

        Assert.Contains(events, e => e is ScriptMessageShown { Tick: 20, Message.Text: "Prêt" });
    }

    [Fact]
    public void UnitReady_IsMet_ByAUnitOutOfCombat_SoTheScriptGoesOn()
    {
        // Le héros attendu tombe avant d'être prêt : son action imposée est abandonnée, et le verrou est quand même levé.
        var sim = new CombatSimulation(Fight(
            Trigger("lock", null, LockOn),
            Trigger("turn", new ScriptCondition { UnitReady = "slow" },
                new ScriptEffect { Impose = new[] { new ScriptAction { Unit = "slow", MoveTo = new Cell(1, 3) } } }),
            Trigger("free", new ScriptCondition { After = "turn" }, LockOff)));
        sim.Get(2).Hp = 0;
        sim.Get(2).Status = UnitStatus.Removed;

        var events = sim.TickTimes(2);

        Assert.Contains(events, e => e is ImposedActionEnded { Done: false, Unit.Value: 2 });
        Assert.False(sim.IsLocked);
    }

    [Fact]
    public void ALockedCombat_ReplaysIdentically()
    {
        var setup = Fight(
            Trigger("lock", null, LockOn),
            Trigger("ready", new ScriptCondition { UnitReady = "hero" },
                new ScriptEffect { Impose = new[] { new ScriptAction { Unit = "hero", MoveTo = new Cell(0, 0) } } }),
            Trigger("free", new ScriptCondition { After = "ready", AtMs = 1500 }, LockOff));
        var sim = new CombatSimulation(setup);
        sim.TickTimes(6);
        sim.Submit(new ActivateCommand(Hero, new Cell(0, 0)));
        sim.TickTimes(40);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, sim.Step);

        Assert.Equal(sim.EventLog, replay.EventLog);
        Assert.Equal(sim.Checksum(), replay.Checksum());
        Assert.False(sim.IsLocked);
    }
}
