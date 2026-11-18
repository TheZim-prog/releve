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

    private static CombatSetup Imposing(params ImposedAction[] actions) =>
        Fight(Trigger("lesson", null, new ScriptEffect { Say = "Frappez", Impose = actions })) with
        {
            Units = new[] { Unit(1, team: 0, 0, 0) with { Tag = "hero" }, Unit(2, team: 1, 3, 0) with { Tag = "boss" } },
        };

    [Fact]
    public void WellFormedImposedActions_AreAccepted()
    {
        _ = new CombatSimulation(Imposing(new ImposedAction { Unit = "hero", Skill = "strike", Target = "boss", MoveTo = new Cell(1, 0) }));
        _ = new CombatSimulation(Imposing(new ImposedAction { Unit = "hero", MoveTo = new Cell(1, 1) }));
        // Une unité et une case : pour une compétence qui déplace sa cible quelque part (télékinésie).
        _ = new CombatSimulation(Imposing(new ImposedAction { Unit = "hero", Skill = "strike", Target = "boss", Cell = new Cell(2, 1) }));
    }

    [Theory]
    [InlineData("boss", "strike", "boss", null, "not a hero")]
    [InlineData("ghost", "strike", "boss", null, "unknown unit tag 'ghost'")]
    [InlineData("hero", "fly", "boss", null, "no skill 'fly'")]
    [InlineData("hero", null, null, null, "needs a skill or a cell")]
    [InlineData("hero", null, "boss", 1, "has a target but no skill")]
    [InlineData("hero", "strike", null, 1, "'strike' imposed on 'hero' must aim at a unit")]
    [InlineData("hero", "strike", "king", null, "unknown unit tag 'king'")]
    public void AMalformedImposedAction_IsRefused(string unit, string? skill, string? target, int? cell, string message)
    {
        var action = new ImposedAction
        {
            Unit = unit, Skill = skill, Target = target,
            Cell = cell is null ? null : new Cell(1, 1),
            MoveTo = skill is null && target is not null ? new Cell(1, 1) : null,
        };

        Assert.Contains(message, Refusal(Imposing(action)));
    }

    [Fact]
    public void AZoneSkill_MustAimAtACell_AndASelfSkill_AtNothing()
    {
        var zone = new SkillDefinition
        {
            Id = "blast", Weight = SkillWeight.Light, Range = 3, MinDamage = 5, MaxDamage = 5, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        };
        var self = new SkillDefinition
        {
            Id = "focus", Weight = SkillWeight.Light, Target = SkillTargeting.Self, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Effects = new[] { new StatusEffect { Status = StatusKind.Hasted, DurationMs = 1000 } },
        };
        var hero = Unit(1, team: 0, 0, 0, skills: new[] { zone, self }) with { Tag = "hero" };
        CombatSetup With(ImposedAction action) => Imposing(action) with { Units = new[] { hero, Unit(2, team: 1, 3, 0) with { Tag = "boss" } } };

        _ = new CombatSimulation(With(new ImposedAction { Unit = "hero", Skill = "blast", Cell = new Cell(2, 0) }));
        _ = new CombatSimulation(With(new ImposedAction { Unit = "hero", Skill = "focus" }));
        Assert.Contains("must aim at a cell", Refusal(With(new ImposedAction { Unit = "hero", Skill = "blast", Target = "boss" })));
        Assert.Contains("must aim at nothing", Refusal(With(new ImposedAction { Unit = "hero", Skill = "focus", Target = "boss" })));
    }

    [Fact]
    public void TwoActionsForTheSameHero_AreRefused() =>
        Assert.Contains("two actions are imposed on 'hero'", Refusal(Imposing(
            new ImposedAction { Unit = "hero", Skill = "strike", Target = "boss" },
            new ImposedAction { Unit = "hero", MoveTo = new Cell(1, 1) })));

    [Fact]
    public void ImposedActions_CannotAlsoFreeze()
    {
        var setup = Imposing() with
        {
            Script = new[]
            {
                Trigger("lesson", null, new ScriptEffect { Freeze = true, Impose = new[] { new ImposedAction { Unit = "hero", MoveTo = new Cell(1, 1) } } }),
            },
        };

        Assert.Contains("no \"freeze\"", Refusal(setup));
    }

    [Fact]
    public void AnEffect_NeedsATextOrImposedActions() =>
        Assert.Contains("a text to say, or actions to impose", Refusal(Fight(Trigger("a", null, new ScriptEffect()))));

    [Fact]
    public void AReinforcementTag_CanBeWatched()
    {
        var setup = Fight(Trigger("a", new ScriptCondition { UnitDown = "late" }))
            with { Waves = new[] { new WaveSetup(1000, new[] { Unit(0, team: 1, 3, 1) with { Tag = "late" } }) } };

        _ = new CombatSimulation(setup);
    }
}
