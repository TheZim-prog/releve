using Game.Core.Reputation;
using Game.Core.Tests.Campaign;

namespace Game.Core.Tests.Reputation;

public class ReputationScaleTests
{
    private static readonly ReputationScale Scale = CampaignStateTests.Scale;

    [Theory]
    [InlineData(-100, "hated")]
    [InlineData(-16, "hated")]
    [InlineData(-15, "neutral")]
    [InlineData(14, "neutral")]
    [InlineData(15, "liked")]
    [InlineData(500, "liked")]
    public void AValue_FallsInTheLastTierItReaches(int value, string tier) => Assert.Equal(tier, Scale.TierOf(value).Id);

    [Fact]
    public void AScale_NeedsOrderedTiers_FromItsMin()
    {
        Assert.Throws<ArgumentException>(() => (Scale with { Tiers = new[] { new ReputationTier("a", "A", -50) } }).Validate());
        Assert.Throws<ArgumentException>(() => (Scale with
        {
            Tiers = new[] { new ReputationTier("a", "A", -100), new ReputationTier("b", "B", 20), new ReputationTier("c", "C", 10) },
        }).Validate());
        Assert.Throws<ArgumentException>(() => (Scale with { Start = 200 }).Validate());
    }

    [Fact]
    public void TheGamesScale_StartsNeutral()
    {
        var scale = ContentTestKit.Content.Reputation!.Scale;

        Assert.Equal("neutral", scale.TierOf(scale.Start).Id);
    }
}
