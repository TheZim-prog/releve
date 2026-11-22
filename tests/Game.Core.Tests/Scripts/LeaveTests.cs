using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using Game.Core.Scripts;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

public class LeaveTests
{
    private static readonly UnitId Boss = new(2), Thug = new(3);

    private static ScriptTrigger Trigger(string id, ScriptCondition? when, params ScriptEffect[] effects) =>
        new() { Id = id, When = when, Do = effects };

    private static ScriptEffect Leave(params string[] tags) => new() { Speaker = "Le caïd", Say = "On se reverra !", Leave = tags };

    /// <summary>Un héros (1) à gauche, le caïd « boss » (2) et un sbire « thug » (3) à droite, sans IA.</summary>
    private static CombatSetup Fight(params ScriptTrigger[] script) =>
        Setup(new Grid(8, 3),
                Unit(1, team: 0, 0, 1) with { Tag = "hero" },
                Unit(2, team: 1, 6, 1) with { Tag = "boss" },
                Unit(3, team: 1, 6, 2) with { Tag = "thug" })
            with { Script = script };

    [Fact]
    public void AUnit_LeavesTheCombatAtOnce_AsEscaped_AfterItsLine()
    {
        var sim = new CombatSimulation(Fight(Trigger("exit", null, Leave("boss"))));

        var events = sim.Tick().ToList();

        var said = events.FindIndex(e => e is ScriptMessageShown { Message.Speaker: "Le caïd" });
        var left = events.FindIndex(e => e is UnitEscaped { Unit.Value: 2 });
        Assert.InRange(said, 0, left - 1);
        Assert.Equal(UnitStatus.Removed, sim.Get(2).Status);
        Assert.True(sim.Get(2).Escaped);
        Assert.Equal(UnitStatus.Active, sim.Get(3).Status);
    }

    [Fact]
    public void ALeaderWhoLeaves_TakesHisFollowersAlong()
    {
        var setup = Fight(Trigger("exit", null, Leave("boss")));
        setup = setup with { Units = setup.Units.Select(u => u.Id == Boss ? u with { MinionsOnFall = MinionReaction.Flee } : u).ToArray() };
        var sim = new CombatSimulation(setup);

        var events = sim.Tick();

        Assert.Contains(new UnitFleeing(0, Thug), events);
    }

    [Fact]
    public void AnEscapedUnit_NoLongerCounts_ForAnElimination()
    {
        var sim = new CombatSimulation(Fight(Trigger("exit", null, Leave("boss", "thug"))) with { Objectives = ObjectiveDefinition.Default });

        sim.Tick();

        Assert.True(sim.Outcome);
    }

    [Fact]
    public void AUnitAlreadyGone_CannotLeave_AndTheScriptGoesOn()
    {
        var sim = new CombatSimulation(Fight(
            Trigger("exit", null, Leave("boss")),
            Trigger("next", new ScriptCondition { After = "exit" }, new ScriptEffect { Say = "Il est déjà loin" })));
        sim.Get(2).Status = UnitStatus.Removed;

        var events = sim.Tick();

        Assert.Contains(new ScriptedActionFailed(0, "boss", Boss), events);
        Assert.Contains(events, e => e is ScriptMessageShown { Message.Text: "Il est déjà loin" });
    }

    [Theory]
    [InlineData("hero", "a hero")]
    [InlineData("ghost", "unknown unit tag 'ghost'")]
    public void LeavingIsRefused_ForAHero_OrAnUnknownTag(string tag, string reason)
    {
        var error = Assert.Throws<ArgumentException>(() => new CombatSimulation(Fight(Trigger("exit", null, Leave(tag)))));

        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void LeavingGoesAlone_WithoutActions()
    {
        var effect = Leave("boss") with { Act = new[] { new ScriptAction { Unit = "thug", MoveTo = new Cell(5, 2) } } };

        var error = Assert.Throws<ArgumentException>(() => new CombatSimulation(Fight(Trigger("exit", null, effect))));

        Assert.Contains("\"leave\" goes alone", error.Message);
    }
}
