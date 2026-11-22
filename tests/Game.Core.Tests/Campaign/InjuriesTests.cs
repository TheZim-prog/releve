using Game.Core.Campaign;
using Game.Core.Progression;
using Game.Core.Tests.Progression;

namespace Game.Core.Tests.Campaign;

/// <summary>Chutes, mort et repos des héros, avec la progression de test (3 chutes tuent, 2 quêtes de repos en rendent une).</summary>
public class InjuriesTests
{
    private static readonly ProgressionRules Rules = ProgressionTestKit.Rules;

    /// <summary>Deux héros d'Alice (hero-1, hero-2), avec ces demi-chutes.</summary>
    private static CampaignState Duo(int halfFalls1 = 0, int halfFalls2 = 0)
    {
        var catalog = ProgressionTestKit.Catalog();
        var recruit = new Recruit("Héros", "hero", new[] { "a1", "a2", "a3" });
        var state = CampaignState.New(CampaignStateTests.Scale).WithPlayer("uid-a", "Alice");
        state = Academy.Enlist(Academy.Enlist(state, catalog, "uid-a", recruit), catalog, "uid-a", recruit);
        return state with
        {
            Heroes = state.Heroes.Select(h => h with { HalfFalls = h.Id == "hero-1" ? halfFalls1 : halfFalls2 }).ToList(),
        };
    }

    private static CampaignHero Hero(CampaignState state, string id) => state.Heroes.Single(h => h.Id == id);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(3, 1, 5)]
    public void AFall_CountsFull_OrHalfIfRescued(int falls, int rescued, int halves) =>
        Assert.Equal(halves, Injuries.HalfFallsOf(falls, rescued));

    [Fact]
    public void TheQuestsHeroes_TakeTheirFalls_AndDieAtTheThreshold()
    {
        var after = Injuries.Apply(Duo(halfFalls1: 3), new Dictionary<string, int> { ["hero-1"] = 3 }, Rules);

        var hero = Hero(after, "hero-1");
        Assert.Equal(6, hero.HalfFalls); // 3 chutes
        Assert.True(hero.IsDead);
        Assert.False(Injuries.Apply(Duo(halfFalls1: 3), new Dictionary<string, int> { ["hero-1"] = 2 }, Rules).Heroes[0].IsDead);
    }

    [Fact]
    public void AHeroWhoStaysAtTheHq_RecoversAFall_EveryTwoQuests()
    {
        var going = new Dictionary<string, int> { ["hero-1"] = 0 };
        var state = Duo(halfFalls2: 3);

        state = Injuries.Apply(state, going, Rules);
        Assert.Equal((3, 1), (Hero(state, "hero-2").HalfFalls, Hero(state, "hero-2").QuestsRested));
        state = Injuries.Apply(state, going, Rules);
        Assert.Equal((1, 0), (Hero(state, "hero-2").HalfFalls, Hero(state, "hero-2").QuestsRested));
        state = Injuries.Apply(Injuries.Apply(state, going, Rules), going, Rules);

        Assert.Equal(0, Hero(state, "hero-2").HalfFalls); // jamais en dessous de zéro
    }

    [Fact]
    public void GoingOnAQuest_InterruptsTheRest_AndRestWithoutFallsIsNotBanked()
    {
        var state = Injuries.Apply(Duo(halfFalls2: 2), new Dictionary<string, int> { ["hero-1"] = 0 }, Rules);
        Assert.Equal(1, Hero(state, "hero-2").QuestsRested);

        state = Injuries.Apply(state, new Dictionary<string, int> { ["hero-2"] = 0 }, Rules);

        Assert.Equal(0, Hero(state, "hero-2").QuestsRested);
        Assert.Equal(0, Hero(state, "hero-1").QuestsRested); // sans chute, son repos ne s'accumule pas
    }

    [Fact]
    public void TheDead_StayDead()
    {
        var state = Duo() with { Heroes = Duo().Heroes.Select(h => h with { IsDead = true, HalfFalls = 6 }).ToList() };

        Assert.Equal(state.Heroes, Injuries.Apply(state, new Dictionary<string, int>(), Rules).Heroes);
    }
}
