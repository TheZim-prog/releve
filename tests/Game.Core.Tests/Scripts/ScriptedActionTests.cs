using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Scripts;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

public class ScriptedActionTests
{
    private static readonly UnitId Ally = new(1), Thug = new(2);

    private static ScriptTrigger Trigger(string id, ScriptCondition? when, params ScriptEffect[] effects) =>
        new() { Id = id, When = when, Do = effects };

    private static ScriptEffect Act(params ScriptAction[] actions) => new() { Act = actions };

    private static ScriptAction Order(string unit, string skill, string target, Cell? moveTo = null) =>
        new() { Unit = unit, Skill = skill, Target = target, MoveTo = moveTo };

    /// <summary>
    /// Un allié du jeu (1, « ally », comportement scénarisé, jauge vide) en (0, 1) et un voyou (2, « thug », 100 PV, sans
    /// IA) à son contact.
    /// </summary>
    private static CombatSetup Fight(params ScriptTrigger[] script) =>
        Setup(new Grid(8, 3),
                Unit(1, team: 0, 0, 1, behavior: Behaviors.Scripted) with { Tag = "ally" },
                Unit(2, team: 1, 1, 1) with { Tag = "thug" })
            with { Script = script };

    [Fact]
    public void AScriptedUnit_NeverActsOnItsOwn()
    {
        var sim = new CombatSimulation(Fight());

        var events = sim.TickTimes(60); // sa jauge est pleine depuis longtemps

        Assert.True(sim.Get(1).IsGaugeFull);
        Assert.DoesNotContain(events, e => e is SkillUsed or UnitMoved);
    }

    [Fact]
    public void AnOrder_IsCarriedOutAtOnce_WithoutWaitingForTheGauge()
    {
        var sim = new CombatSimulation(Fight(Trigger("hit", new ScriptCondition { AtMs = 100 }, Act(Order("ally", "strike", "thug")))));

        sim.Tick();
        var events = sim.Tick();

        Assert.Contains(new SkillUsed(2, Ally, "strike", Thug), events);
        Assert.Equal(90, sim.Get(2).Hp);
        Assert.Equal(Capacity * 40 / 100, sim.Get(1).Gauge); // repartie comme après une action légère (le script passe en fin de tick)
    }

    [Fact]
    public void AnOrder_IgnoresTheCooldown()
    {
        var sim = new CombatSimulation(Fight(
            Trigger("first", null, Act(Order("ally", "smash", "thug"))),
            Trigger("second", new ScriptCondition { After = "first" }, Act(Order("ally", "smash", "thug")))));

        sim.Tick();

        Assert.Equal(40, sim.Get(2).Hp); // deux fois 30, la recharge de 5 s n'y fait rien
    }

    [Fact]
    public void WithoutAnImposedCell_TheUnitMovesToReachItsTarget()
    {
        var setup = Fight(Trigger("hit", null, Act(Order("ally", "strike", "thug"))))
            with { Units = new[] { Unit(1, team: 0, 0, 1, behavior: Behaviors.Scripted) with { Tag = "ally" }, Unit(2, team: 1, 3, 1) with { Tag = "thug" } } };
        var sim = new CombatSimulation(setup);

        sim.Tick();

        Assert.Equal(1, sim.Get(1).Position.DistanceTo(sim.Get(2).Position));
        Assert.Equal(90, sim.Get(2).Hp);
    }

    [Fact]
    public void AnImposedCell_IsWhereTheUnitGoes()
    {
        var sim = new CombatSimulation(Fight(Trigger("go", null, Act(new ScriptAction { Unit = "ally", MoveTo = new Cell(0, 2) }))));

        sim.Tick();

        Assert.Equal(new Cell(0, 2), sim.Get(1).Position);
    }

    [Fact]
    public void AStunnedUnit_CannotObey()
    {
        var sim = new CombatSimulation(Fight(Trigger("hit", new ScriptCondition { AtMs = 50 }, Act(Order("ally", "strike", "thug")))));
        sim.Get(1).SetStatus(StatusKind.Stunned, endTick: 100, source: null);

        var events = sim.Tick();

        Assert.Contains(new ScriptedActionFailed(1, "ally", Ally), events);
        Assert.Equal(100, sim.Get(2).Hp);
    }

    [Fact]
    public void AnImpossibleOrder_Fails_AndTheScriptGoesOn()
    {
        // Le voyou est hors d'atteinte (mobilité 3, frappe au contact) ; le message qui suit passe quand même.
        var setup = Fight(
                Trigger("hit", null, Act(Order("ally", "strike", "thug"))),
                Trigger("next", new ScriptCondition { After = "hit" }, new ScriptEffect { Say = "Raté" }))
            with { Units = new[] { Unit(1, team: 0, 0, 1, behavior: Behaviors.Scripted) with { Tag = "ally" }, Unit(2, team: 1, 7, 1) with { Tag = "thug" } } };
        var sim = new CombatSimulation(setup);

        var events = sim.Tick();

        Assert.Contains(new ScriptedActionFailed(0, "ally", Ally), events);
        Assert.Contains(events, e => e is ScriptMessageShown { Message.Text: "Raté" });
    }

    [Fact]
    public void ALine_IsSaidJustBeforeTheAction()
    {
        var sim = new CombatSimulation(Fight(Trigger("hit", null,
            new ScriptEffect { Speaker = "Allié", Say = "Je le tiens !", Act = new[] { Order("ally", "strike", "thug") } })));

        var events = sim.Tick().ToList();

        var said = events.FindIndex(e => e is ScriptMessageShown { Message: { Speaker: "Allié", Freezes: false } });
        var hit = events.FindIndex(e => e is SkillUsed);
        Assert.InRange(said, 0, hit - 1);
    }

    [Fact]
    public void AnEnemy_CanBeOrdered_AScriptOnlySkill_ThatItsAiNeverUses()
    {
        var treachery = new SkillDefinition
        {
            Id = "treachery", Weight = SkillWeight.Light, Range = 1, MinDamage = 80, MaxDamage = 80, CooldownMs = 0, ScriptOnly = true,
        };
        var enemy = Unit(2, team: 1, 1, 1, gauge: Capacity, behavior: Behaviors.Basic, skills: new[] { Strike, treachery }) with { Tag = "thug" };
        var hero = Unit(1, team: 0, 0, 1, hp: 200) with { Tag = "hero" };

        // Seule, son IA frappe avec la plus faible : la réservée au script n'est jamais choisie.
        var alone = new CombatSimulation(Setup(new Grid(8, 3), hero, enemy));
        Assert.DoesNotContain(alone.TickTimes(60).OfType<SkillUsed>(), e => e.SkillId == "treachery");

        // Sur ordre du script, elle s'en sert.
        var scripted = new CombatSimulation(Setup(new Grid(8, 3), hero, enemy)
            with { Script = new[] { Trigger("betrayal", null, Act(Order("thug", "treachery", "hero"))) } });
        Assert.Contains(scripted.Tick(), e => e is SkillUsed { SkillId: "treachery" });
    }

    [Fact]
    public void AFleeingUnit_CannotObey()
    {
        var setup = Fight(Trigger("hit", new ScriptCondition { AtMs = 50 }, Act(Order("thug", "strike", "ally"))))
            with { Units = new[] { Unit(1, team: 0, 0, 1, behavior: Behaviors.Scripted) with { Tag = "ally" }, Unit(2, team: 1, 1, 1, behavior: Behaviors.Basic) with { Tag = "thug" } } };
        var sim = new CombatSimulation(setup);
        sim.Get(2).IsFleeing = true;

        var events = sim.Tick();

        Assert.Contains(new ScriptedActionFailed(1, "thug", Thug), events);
        Assert.Equal(100, sim.Get(1).Hp);
    }

    [Fact]
    public void AnOrderForAUnitNotYetArrived_FailsWithItsTag()
    {
        var setup = Fight(Trigger("early", null, Act(Order("late", "strike", "ally"))))
            with { Waves = new[] { new WaveSetup(1000, new[] { Unit(0, team: 1, 7, 2) with { Tag = "late" } }) } };
        var sim = new CombatSimulation(setup);

        Assert.Contains(new ScriptedActionFailed(0, "late", null), sim.Tick());
    }

    [Fact]
    public void AScriptOnlySkill_IsRefusedToPlayers()
    {
        var treachery = new SkillDefinition
        {
            Id = "treachery", Weight = SkillWeight.Light, Range = 1, MinDamage = 80, MaxDamage = 80, CooldownMs = 0, ScriptOnly = true,
        };
        var sim = Simulation(new Grid(4, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { treachery }), Unit(2, team: 1, 1, 0));

        Assert.Equal(RejectionReason.ScriptOnly, sim.Check(Use(Ally, "treachery", Thug)));
    }

    [Fact]
    public void AScriptedAlly_DoesNotHoldOffDefeat()
    {
        // Le seul héros jouable tombe : le combat est perdu, même si l'allié scénarisé tient encore debout.
        var setup = Setup(new Grid(6, 3),
                Unit(1, team: 0, 0, 1, behavior: Behaviors.Scripted),
                Unit(2, team: 0, 0, 2, hp: 10, onDefeat: DefeatOutcome.Downed),
                Unit(3, team: 1, 1, 2, gauge: Capacity))
            with { Objectives = Game.Core.Objectives.ObjectiveDefinition.Default };
        var sim = new CombatSimulation(setup);

        sim.Submit(Use(new UnitId(3), "strike", new UnitId(2)));
        sim.Tick();

        Assert.False(sim.Outcome);
    }

    [Fact]
    public void ACombatWithOrders_ReplaysIdentically()
    {
        var setup = Fight(
            Trigger("first", new ScriptCondition { AtMs = 200 }, Act(Order("ally", "smash", "thug"))),
            Trigger("second", new ScriptCondition { AtMs = 600 }, Act(Order("ally", "strike", "thug"))));
        var sim = new CombatSimulation(setup);
        sim.TickTimes(20);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, sim.Step);

        Assert.Equal(sim.EventLog, replay.EventLog);
        Assert.Equal(sim.Checksum(), replay.Checksum());
        Assert.Equal(60, sim.Get(2).Hp);
    }
}
