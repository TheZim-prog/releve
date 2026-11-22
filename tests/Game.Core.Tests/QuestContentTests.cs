using Game.Core.Campaign;
using Game.Core.Network;
using Game.Core.Progression;
using Xunit.Abstractions;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>
/// Les quêtes du panneau (data/quests) : chacune se joue jusqu'au bout avec des recrues de niveau 1 (créées comme à
/// l'académie, confiées à l'IA), et se rejoue à l'identique.
/// </summary>
public class QuestContentTests(ITestOutputHelper output)
{
    public static TheoryData<string> Quests()
    {
        var data = new TheoryData<string>();
        foreach (var id in Content.Quests.Keys.Order())
            data.Add(id);
        return data;
    }

    /// <summary>Une recrue de cet archétype, qui prend les premières cases ouvertes de son arbre.</summary>
    internal static HeroSheet Recruit(string archetype, string owner = "uid-test")
    {
        var catalog = Content.Heroes!;
        var learned = new List<string>();
        while (Academy.Learnable(catalog, archetype, learned).FirstOrDefault() is { } node)
            learned.Add(node.Id);
        var state = CampaignState.New(Content.Reputation!.Scale).WithPlayer(owner, "Test");
        return Academy.Enlist(state, catalog, owner, new Recruit(archetype, archetype, learned)).Heroes.Single().Sheet;
    }

    [Theory]
    [MemberData(nameof(Quests))]
    public void AQuest_PlaysToTheEnd_WithThreeRecruits(string questId)
    {
        var quest = Content.Quests[questId];
        var recruits = new[] { "colossus", "fighter", "energetic" }.Select(a => Recruit(a)).ToList();

        var setup = new CombatLaunch(quest.Scenario, Array.Empty<HeroAssignment>(), recruits).BuildSetup(Content);
        var (_, outcome) = PlayToTheEnd(setup, questId);

        output.WriteLine(outcome);
    }

    [Fact]
    public void EveryQuestScenario_HasRoomForAFullGroup() =>
        Assert.All(Content.Quests.Values, q => Assert.True(Content.Scenarios[q.Scenario].HeroSlots!.Count >= 5, q.Id));
}
