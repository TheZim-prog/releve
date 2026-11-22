using Game.Core.Campaign;
using Game.Core.Progression;
using Game.Core.Tests.Progression;

namespace Game.Core.Tests.Campaign;

/// <summary>Les récompenses d'une quête, avec la progression de test (100 XP par niveau, défaite : 50 %).</summary>
public class QuestRewardsTests
{
    private static readonly ProgressionRules Rules = ProgressionTestKit.Rules;

    private static readonly QuestDefinition Quest = new()
    {
        Id = "q", Name = "Quête", Scenario = "s", Difficulty = QuestDifficulty.Easy, Xp = 60,
        Materials = new Dictionary<string, int> { ["materials"] = 3 },
    };

    /// <summary>Alice a deux héros (hero-1, hero-2), Bob un (hero-3) ; le pot a déjà 1 matériau.</summary>
    private static CampaignState Campaign()
    {
        var state = CampaignState.New(CampaignStateTests.Scale).WithPlayer("uid-a", "Alice").WithPlayer("uid-b", "Bob");
        var catalog = ProgressionTestKit.Catalog();
        var recruit = new Recruit("Héros", "hero", new[] { "a1", "a2", "a3" });
        state = Academy.Enlist(Academy.Enlist(state, catalog, "uid-a", recruit), catalog, "uid-a", recruit);
        state = Academy.Enlist(state, catalog, "uid-b", recruit);
        return state with { Materials = new Dictionary<string, int> { ["materials"] = 1 } };
    }

    private static CampaignHero Hero(CampaignState state, string id) => state.Heroes.Single(h => h.Id == id);

    [Fact]
    public void AWonQuest_GivesItsFullXpToEachHeroWhoWent_AndItsMaterialsToThePot()
    {
        var after = QuestRewards.Grant(Campaign(), Quest, new[] { "hero-1", "hero-3" }, victory: true, Rules);

        Assert.Equal((60, 60, 0), (Hero(after, "hero-1").Xp, Hero(after, "hero-3").Xp, Hero(after, "hero-2").Xp));
        Assert.Equal(4, after.Materials["materials"]);
    }

    [Fact]
    public void ALostQuest_GivesAShareOfTheXp_AndNoMaterials()
    {
        var after = QuestRewards.Grant(Campaign(), Quest, new[] { "hero-1" }, victory: false, Rules);

        Assert.Equal(30, Hero(after, "hero-1").Xp);
        Assert.Equal(1, after.Materials["materials"]);
    }

    [Fact]
    public void EnoughXp_RaisesTheLevel_AndLeavesPointsToSpend()
    {
        var state = Campaign();
        state = QuestRewards.Grant(state, Quest, new[] { "hero-1" }, victory: true, Rules); // 60
        state = QuestRewards.Grant(state, Quest with { Xp = 150 }, new[] { "hero-1" }, victory: true, Rules); // 210 : niveau 3

        var hero = Hero(state, "hero-1");
        Assert.Equal(3, hero.Sheet.Level);
        Assert.Equal(2, QuestRewards.UnspentStatPoints(hero.Sheet, Rules));
        Assert.Equal(1, QuestRewards.UnspentSkillPoints(hero.Sheet, Rules)); // 3 + 3/2 = 4 points, 3 dépensés
        ProgressionTestKit.Catalog().Check(hero.Sheet);
    }

    [Fact]
    public void ADeadHero_GainsNothing()
    {
        var state = Campaign();
        state = state with { Heroes = state.Heroes.Select(h => h.Id == "hero-1" ? h with { IsDead = true } : h).ToList() };

        Assert.Equal(0, Hero(QuestRewards.Grant(state, Quest, new[] { "hero-1" }, victory: true, Rules), "hero-1").Xp);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(1899, 19)]
    [InlineData(1900, 20)]
    [InlineData(99999, 20)]
    public void TheLevel_FollowsTheXpTable(int xp, int level) => Assert.Equal(level, Rules.LevelFor(xp));

    [Fact]
    public void TheXpTable_HasOneIncreasingValuePerLevel()
    {
        Assert.Throws<ArgumentException>(() => (Rules with { XpToReach = Rules.XpToReach.Take(5).ToList() }).Validate());
        Assert.Throws<ArgumentException>(() => (Rules with { XpToReach = Rules.XpToReach.Reverse().ToList() }).Validate());
        Assert.Throws<ArgumentException>(() => (Rules with { DefeatXpPercent = 150 }).Validate());
        Assert.Equal(100, Rules.XpForNextLevel(1));
        Assert.Null(Rules.XpForNextLevel(20));
    }
}
