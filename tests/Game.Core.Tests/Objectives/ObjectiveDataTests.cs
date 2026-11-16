using Game.Core.Combat;
using Game.Core.Objectives;
using Game.Core.Scenarios;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class ObjectiveDataTests
{
    private const string Scenario = """
        {
          "id": "test", "name": "Test", "seed": 7,
          "map": [ "...." ],
          "units": [
            { "unit": "hero", "team": 0, "x": 0, "y": 0 },
            { "unit": "thug", "team": 1, "x": 3, "y": 0 }
          ]
          OBJECTIVES
        }
        """;

    private static readonly IReadOnlyDictionary<string, UnitDefinition> Catalog = new Dictionary<string, UnitDefinition>
    {
        ["hero"] = Unit(0, 0, 0, 0).Definition with { Id = "hero" },
        ["thug"] = Unit(0, 0, 0, 0).Definition with { Id = "thug" },
    };

    private static ScenarioDefinition WithObjectives(string objectives) =>
        ScenarioDefinition.FromJson(Scenario.Replace("OBJECTIVES", objectives.Length > 0 ? $", \"objectives\": {objectives}" : ""));

    [Fact]
    public void AScenarioWithoutObjectives_EliminatesEveryEnemy()
    {
        var setup = WithObjectives("").ToSetup(Rules, Catalog);

        Assert.Equal(ObjectiveDefinition.Default, setup.Objectives);
    }

    [Fact]
    public void AScenario_PassesItsObjectivesToTheCombat()
    {
        var setup = WithObjectives("""[ { "kind": "eliminate" }, { "kind": "eliminate", "secondary": true } ]""").ToSetup(Rules, Catalog);

        Assert.Equal(new[] { (ObjectiveKind.Eliminate, false), (ObjectiveKind.Eliminate, true) },
            new CombatSimulation(setup).Objectives.Select(o => (o.Definition.Kind, o.Definition.Secondary)));
    }

    [Theory]
    [InlineData("[]")]                                              // aucun objectif principal
    [InlineData("""[ { "kind": "eliminate", "secondary": true } ]""")] // que des secondaires
    [InlineData("""[ { "kind": "infiltrate" } ]""")]                // type inconnu
    [InlineData("""[ { "secondary": true } ]""")]                    // type manquant
    [InlineData("[ null ]")]
    public void InvalidObjectives_AreRejected(string objectives)
    {
        Assert.ThrowsAny<Exception>(() => WithObjectives(objectives));
    }
}
