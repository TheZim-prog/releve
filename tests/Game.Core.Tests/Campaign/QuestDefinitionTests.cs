using Game.Core.Campaign;
using Game.Core.Grids;
using Game.Core.Scenarios;

namespace Game.Core.Tests.Campaign;

/// <summary>Les quêtes et les cases que leurs scénarios réservent aux héros des joueurs.</summary>
public class QuestDefinitionTests
{
    private const string Scenario = """
        {
          "id": "street", "name": "Rue", "seed": 1,
          "map": [ "..#...", "......" ],
          "heroSlots": [ { "x": 0, "y": 0 }, { "x": 0, "y": 1 } ],
          "units": [ { "unit": "thug", "team": 1, "x": 5, "y": 1 } ]
        }
        """;

    private static readonly QuestDefinition Quest = new() { Id = "q", Name = "Quête", Scenario = "street", Difficulty = QuestDifficulty.Easy, Xp = 10, DurationHours = 24 };

    [Fact]
    public void AHeroSlot_MustBeAFreeWalkableCell()
    {
        Assert.Contains("blocked cell", Assert.Throws<ArgumentException>(() =>
            ScenarioDefinition.FromJson(Scenario.Replace("""{ "x": 0, "y": 1 }""", """{ "x": 2, "y": 0 }"""))).Message);
        Assert.Contains("already taken", Assert.Throws<ArgumentException>(() =>
            ScenarioDefinition.FromJson(Scenario.Replace("""{ "x": 0, "y": 1 }""", """{ "x": 5, "y": 1 }"""))).Message);
    }

    [Fact]
    public void DeployedHeroes_ComeAfterTheScenarioUnits()
    {
        var scenario = ScenarioDefinition.FromJson(Scenario);

        Assert.Equal(new Game.Core.Combat.UnitId(2), scenario.DeployedUnitId(0));
        Assert.Equal(new[] { new Cell(0, 0), new Cell(0, 1) }, scenario.HeroSlots);
    }

    [Fact]
    public void AQuest_NeedsAName_AKnownDifficulty_AndNoNegativeReward()
    {
        Quest.Validate();
        Assert.Throws<ArgumentException>(() => (Quest with { Name = " " }).Validate());
        Assert.Throws<ArgumentException>(() => (Quest with { Difficulty = (QuestDifficulty)42 }).Validate());
        Assert.Throws<ArgumentException>(() => (Quest with { Xp = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Quest with { Materials = new Dictionary<string, int> { ["materials"] = -2 } }).Validate());
    }

    [Fact]
    public void AQuest_PlaysAKnownScenario_WithRoomForTheHeroes()
    {
        var withSlots = ScenarioDefinition.FromJson(Scenario);
        var withoutSlots = withSlots with { Id = "plain", HeroSlots = null };
        var scenarios = new Dictionary<string, ScenarioDefinition> { ["street"] = withSlots, ["plain"] = withoutSlots };

        Quest.ValidateAgainst(scenarios);
        Assert.Contains("unknown scenario", Assert.Throws<ArgumentException>(() => (Quest with { Scenario = "nope" }).ValidateAgainst(scenarios)).Message);
        Assert.Contains("no hero slots", Assert.Throws<ArgumentException>(() => (Quest with { Scenario = "plain" }).ValidateAgainst(scenarios)).Message);
    }

    [Fact]
    public void MoreHeroesThanSlots_AreRefused()
    {
        var sheet = QuestContentTests.Recruit("colossus");

        var error = Assert.Throws<ArgumentException>(() =>
            ContentTestKit.Content.SetupFor("quest-thugs", Enumerable.Repeat(sheet, 6).ToList()));

        Assert.Contains("6 heroes deployed, 5 hero slots", error.Message);
    }
}
