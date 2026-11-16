using System.Text.Json;
using Game.Core.Combat;
using Game.Core.Objectives;
using Game.Core.Scenarios;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Objectives;

public class MissionDataTests
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
        ["civilian"] = Unit(0, 0, 0, 0).Definition with
        {
            Id = "civilian", IsCivilian = true, Behavior = Behaviors.Flee, Skills = Array.Empty<SkillDefinition>(),
            OpportunityAttacksPerCycle = 0,
        },
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

    private const string Hero = """{ "unit": "hero", "team": 0,""";

    /// <summary>Le scénario de test dont le voyou (camp 1) est un chef dont les sbires se rendent.</summary>
    private static string WithLeader() =>
        Scenario.Replace("OBJECTIVES", "").Replace("\"unit\": \"thug\",", "\"unit\": \"thug\", \"leader\": \"surrender\",");

    [Fact]
    public void AScenario_NamesTheLeaderOfATeam()
    {
        var setup = ScenarioDefinition.FromJson(WithLeader()).ToSetup(Rules, Catalog);

        Assert.Equal(new MinionReaction?[] { null, MinionReaction.Surrender }, setup.Units.Select(u => u.MinionsOnFall));
    }

    [Theory]
    [InlineData("""{ "unit": "thug", "leader": "flee", "team": 1,""")]    // deux chefs dans un camp
    [InlineData("""{ "hero": "someone", "leader": "flee", "team": 0,""")] // un héros
    [InlineData("""{ "unit": "hero", "leader": "flee", "team": 0,""")]    // le camp des joueurs
    public void OnlyOneEnemyUnitPerTeam_CanLead(string invalid)
    {
        Assert.Throws<ArgumentException>(() => ScenarioDefinition.FromJson(WithLeader().Replace(Hero, invalid)));
    }

    [Fact]
    public void ACivilian_CannotLead()
    {
        var scenario = ScenarioDefinition.FromJson(WithLeader().Replace("\"thug\"", "\"civilian\""));

        Assert.Throws<ArgumentException>(() => scenario.ToSetup(Rules, Catalog));
    }

    [Fact]
    public void AnUnknownMinionReaction_IsRejected()
    {
        Assert.Throws<JsonException>(() => ScenarioDefinition.FromJson(WithLeader().Replace("\"surrender\"", "\"obey\"")));
    }

    [Theory]
    [InlineData("[]")]                                             // aucun objectif principal
    [InlineData("""[ { "kind": "eliminate", "secondary": true } ]""")] // que des secondaires
    [InlineData("""[ { "kind": "infiltrate" } ]""")]                // type inconnu
    [InlineData("""[ { "secondary": true } ]""")]                    // type manquant
    [InlineData("[ null ]")]
    [InlineData("""[ { "kind": "rescue" } ]""")]                    // sauvetage sans minimum
    public void InvalidObjectives_AreRejected(string objectives)
    {
        Assert.ThrowsAny<Exception>(() => WithObjectives(objectives));
    }

    [Fact]
    public void ARescue_ReadsItsMinimum_AndNeedsThatManyCivilians()
    {
        var rescue = """[ { "kind": "rescue", "minimum": 1 } ]""";

        Assert.Equal(1, Assert.Single(WithObjectives(rescue).Objectives!).Minimum);
        Assert.Throws<ArgumentException>(() => WithObjectives(rescue).ToSetup(Rules, Catalog)); // aucun civil placé
        var withCivilian = Scenario.Replace("OBJECTIVES", $", \"objectives\": {rescue}")
            .Replace("""{ "unit": "thug",""", """{ "unit": "civilian", "team": 2, "x": 1, "y": 0 }, { "unit": "thug",""");
        Assert.Single(ScenarioDefinition.FromJson(withCivilian).ToSetup(Rules, Catalog).Units, u => u.Definition.IsCivilian);
    }
}
