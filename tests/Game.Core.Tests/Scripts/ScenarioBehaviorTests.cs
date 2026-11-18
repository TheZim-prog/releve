using Game.Core.Combat;
using Game.Core.Scenarios;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Scripts;

public class ScenarioBehaviorTests
{
    private static readonly Dictionary<string, UnitDefinition> Units = new()
    {
        ["hero"] = Unit(0, 0, 0, 0).Definition with { Id = "hero" },
        ["thug"] = Unit(0, 1, 0, 0, behavior: Behaviors.Basic).Definition with { Id = "thug" },
    };

    private static string Scenario(string heroBehavior) => $$"""
        {
          "id": "s", "name": "S", "seed": 1, "map": [ "....", "...." ],
          "units": [
            { "unit": "hero", "team": 0, "x": 0, "y": 0, "behavior": "{{heroBehavior}}" },
            { "unit": "thug", "team": 1, "x": 3, "y": 1 }
          ]
        }
        """;

    [Fact]
    public void AScenario_CanGiveAUnitAnotherBehavior()
    {
        var setup = ScenarioDefinition.FromJson(Scenario(Behaviors.Scripted)).ToSetup(Rules, Units);

        Assert.Equal(Behaviors.Scripted, setup.Units[0].Definition.Behavior);
        Assert.Equal(Behaviors.Basic, setup.Units[1].Definition.Behavior); // les autres gardent celui de leur type
    }

    [Fact]
    public void AnUnknownBehavior_IsRefusedWhenLoading() =>
        Assert.Contains("unknown behavior 'dancing'",
            Assert.Throws<ArgumentException>(() => ScenarioDefinition.FromJson(Scenario("dancing")).ToSetup(Rules, Units)).Message);

    [Fact]
    public void AReinforcement_CanGetABehaviorToo_CheckedAgainstItsType()
    {
        var civilian = Unit(0, 2, 0, 0, behavior: Behaviors.Flee, initiative: 10, skills: Array.Empty<SkillDefinition>(), opportunityAttacks: 0)
            .Definition with { Id = "civilian", IsCivilian = true };
        var units = new Dictionary<string, UnitDefinition>(Units) { ["civilian"] = civilian };
        string WithWave(string unit, string behavior) => Scenario(Behaviors.Basic).Replace("\"units\": [", $$"""
            "waves": [ { "atMs": 1000, "units": [ { "unit": "{{unit}}", "team": 1, "x": 3, "y": 0, "behavior": "{{behavior}}" } ] } ],
            "units": [
            """);

        var setup = ScenarioDefinition.FromJson(WithWave("thug", Behaviors.Scripted)).ToSetup(Rules, units);
        Assert.Equal(Behaviors.Scripted, setup.Waves[0].Units[0].Definition.Behavior);
        // Un civil fuit toujours : lui donner un autre comportement est refusé au chargement.
        Assert.Contains("a civilian flees",
            Assert.Throws<ArgumentException>(() => ScenarioDefinition.FromJson(WithWave("civilian", Behaviors.Basic)).ToSetup(Rules, units)).Message);
    }
}
