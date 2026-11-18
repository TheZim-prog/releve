using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Scripts;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

public class ScriptValidationTests
{
    private static readonly ScriptEffect Hello = new() { Say = "Bonjour" };

    private static ScriptTrigger Trigger(string id, ScriptCondition? when = null, params ScriptEffect[] effects) =>
        new() { Id = id, When = when, Do = effects.Length > 0 ? effects : new[] { Hello } };

    private static CombatSetup Fight(params ScriptTrigger[] script) =>
        Setup(new Grid(4, 2), Unit(1, team: 0, 0, 0), Unit(2, team: 1, 3, 0) with { Tag = "boss" }) with { Script = script };

    private static string Refusal(CombatSetup setup) =>
        Assert.Throws<ArgumentException>(() => new CombatSimulation(setup)).Message;

    [Fact]
    public void AWellFormedScript_IsAccepted()
    {
        var setup = Fight(
            Trigger("intro"),
            Trigger("end", new ScriptCondition { After = "intro", UnitDown = "boss", AtMs = 1000 },
                new ScriptEffect { Say = "Fini", Speaker = "Télépathe", Freeze = true }));

        _ = new CombatSimulation(setup);
    }

    [Fact]
    public void TriggerIds_MustBeUnique() =>
        Assert.Contains("Duplicate", Refusal(Fight(Trigger("a"), Trigger("a"))));

    [Fact]
    public void ATrigger_NeedsAnId() =>
        Assert.Contains("needs an id", Refusal(Fight(Trigger(" "))));

    [Fact]
    public void ATrigger_NeedsEffects() =>
        Assert.Contains("needs effects", Refusal(Fight(new ScriptTrigger { Id = "a", Do = Array.Empty<ScriptEffect>() })));

    [Fact]
    public void AMessage_NeedsAText() =>
        Assert.Contains("text to say", Refusal(Fight(Trigger("a", null, new ScriptEffect { Say = "" }))));

    [Fact]
    public void ATime_CannotBeNegative() =>
        Assert.Contains("negative", Refusal(Fight(Trigger("a", new ScriptCondition { AtMs = -1 }))));

    [Fact]
    public void FollowingAnUnknownTrigger_IsRefused() =>
        Assert.Contains("unknown trigger 'ghost'", Refusal(Fight(Trigger("a", new ScriptCondition { After = "ghost" }))));

    [Fact]
    public void AnUnknownTag_IsRefused() =>
        Assert.Contains("unknown unit tag 'king'", Refusal(Fight(Trigger("a", new ScriptCondition { UnitDown = "king" }))));

    [Fact]
    public void ALoopOfTriggers_IsRefused()
    {
        var setup = Fight(
            Trigger("a", new ScriptCondition { After = "b" }),
            Trigger("b", new ScriptCondition { After = "a" }));

        Assert.Contains("loop", Refusal(setup));
    }

    [Fact]
    public void TwoUnitsWithTheSameTag_AreRefused()
    {
        var setup = Fight() with { Units = new[] { Unit(1, team: 0, 0, 0) with { Tag = "boss" }, Unit(2, team: 1, 3, 0) with { Tag = "boss" } } };

        Assert.Contains("share the tag", Refusal(setup));
    }

    [Fact]
    public void AReinforcementTag_CanBeWatched()
    {
        var setup = Fight(Trigger("a", new ScriptCondition { UnitDown = "late" }))
            with { Waves = new[] { new WaveSetup(1000, new[] { Unit(0, team: 1, 3, 1) with { Tag = "late" } }) } };

        _ = new CombatSimulation(setup);
    }
}
