using Game.Core.Progression;
using static Game.Core.Tests.Progression.ProgressionTestKit;

namespace Game.Core.Tests.Progression;

public class ProgressionRulesTests
{
    [Fact]
    public void DataFile_Loads_WithTheDesignValues()
    {
        var rules = ProgressionRules.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "progression.json")));

        Assert.Equal(Rules.MaxLevel, rules.MaxLevel);
        Assert.Equal((19, 13, 4), (rules.StatPointsAt(20), rules.SkillPointsAt(20), rules.ActiveSlotsAt(20)));
        Assert.Equal(Rules.Tiers, rules.Tiers);
    }

    [Theory]
    [InlineData(1, 0, 3)]   // à la création : aucun point de stat, 3 points de compétence
    [InlineData(2, 1, 4)]   // un point de compétence à chaque niveau pair
    [InlineData(3, 2, 4)]
    [InlineData(20, 19, 13)]
    public void Points_FollowTheLevel(int level, int statPoints, int skillPoints)
    {
        Assert.Equal((statPoints, skillPoints), (Rules.StatPointsAt(level), Rules.SkillPointsAt(level)));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)] // le quatrième emplacement au niveau 10, rien de plus ensuite
    [InlineData(20, 4)]
    public void ActiveSlots_FollowTheLevel(int level, int slots)
    {
        Assert.Equal(slots, Rules.ActiveSlotsAt(level));
    }

    [Fact]
    public void InvalidRules_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => (Rules with { MaxLevel = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Rules with { LevelsPerSkillPoint = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Rules with { ActiveSlots = Array.Empty<ActiveSlotsStep>() }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (Rules with { ActiveSlots = new[] { new ActiveSlotsStep { FromLevel = 2, Count = 3 } } }).Validate()); // rien au niveau 1
        Assert.Throws<ArgumentException>(() => (Rules with { Tiers = Array.Empty<TierRules>() }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (Rules with { Tiers = new[] { Rules.Tiers[1] } }).Validate()); // premier palier fermé au départ
    }
}
