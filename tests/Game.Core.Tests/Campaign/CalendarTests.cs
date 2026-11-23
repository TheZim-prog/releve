using Game.Core.Campaign;
using Game.Core.Progression;
using Game.Core.Tests.Progression;

namespace Game.Core.Tests.Campaign;

/// <summary>
/// Le temps de la campagne : la date de chaque joueur, le panneau qu'elle lui montre, le départ et le retour d'un groupe,
/// et le repos (progression de test : une chute récupérée tous les 2 jours).
/// </summary>
public class CalendarTests
{
    private static readonly ProgressionRules Rules = ProgressionTestKit.Rules;

    private static QuestDefinition Quest(string id, int day = 0) =>
        new() { Id = id, Name = id, Scenario = "s", Difficulty = QuestDifficulty.Easy, Xp = 10, DurationHours = 24, PublishedDay = day };

    private static readonly QuestDefinition[] Quests = { Quest("a"), Quest("b"), Quest("later", day: 3) };

    /// <summary>Alice (avec hero-1 et hero-2) à cette heure, et Bob à celle-ci.</summary>
    private static CampaignState Two(int aliceHour, int bobHour)
    {
        var state = InjuriesTests.Duo().WithPlayer("uid-b", "Bob");
        return state with
        {
            Players = state.Players.Select(p => p with { Hour = p.Uid == "uid-a" ? aliceHour : bobHour }).ToList(),
        };
    }

    private static int HourOf(CampaignState state, string uid) => state.PlayerOf(uid)!.Hour;

    private static CampaignHero Hero(CampaignState state, string id) => state.Heroes.Single(h => h.Id == id);

    [Fact]
    public void TheBoard_ShowsWhatIsPublishedAtThePlayersDate_NotPlayedNorTaken()
    {
        var state = Two(aliceHour: 3 * Calendar.HoursPerDay, bobHour: 2 * Calendar.HoursPerDay + 23) with { DoneQuests = new[] { "a" } };

        Assert.Equal(new[] { "b", "later" }, Calendar.Board(state, Quests, "uid-a").Select(q => q.Id));
        Assert.Equal(new[] { "b" }, Calendar.Board(state, Quests, "uid-b").Select(q => q.Id)); // pas encore le jour 3
        Assert.Equal(new[] { "later" }, Calendar.Board(state, Quests, "uid-a", taken: new[] { "b" }).Select(q => q.Id));
    }

    [Fact]
    public void AGroup_LeavesAtTheLatestDateOfItsPlayers_ThenEachAdvancesByTheQuest()
    {
        var state = Calendar.Depart(Two(aliceHour: 12, bobHour: 60), new[] { "uid-a", "uid-b" }, Quest("a"), Rules);
        Assert.Equal((60, 60), (HourOf(state, "uid-a"), HourOf(state, "uid-b")));

        state = Calendar.Return(state, new[] { "uid-a" }, 36, new[] { "hero-1" }, Rules);

        Assert.Equal((96, 60), (HourOf(state, "uid-a"), HourOf(state, "uid-b")));
    }

    [Fact]
    public void AGroup_NeverLeavesBeforeTheQuestIsPublished()
    {
        var state = Calendar.Depart(Two(aliceHour: 12, bobHour: 30), new[] { "uid-a", "uid-b" }, Quests[2], Rules);

        Assert.Equal((72, 72), (HourOf(state, "uid-a"), HourOf(state, "uid-b"))); // le jour 3
    }
    [Fact]
    public void AHeroWhoStaysBehind_RecoversAFall_EveryTwoDays_ButNotTheOneWhoWent()
    {
        var state = InjuriesTests.Duo(halfFalls1: 4, halfFalls2: 3);

        state = Calendar.Return(state, new[] { "uid-a" }, 36, new[] { "hero-1" }, Rules);
        Assert.Equal((3, 36), (Hero(state, "hero-2").HalfFalls, Hero(state, "hero-2").HoursRested));
        Assert.Equal((4, 0), (Hero(state, "hero-1").HalfFalls, Hero(state, "hero-1").HoursRested));

        state = Calendar.Return(state, new[] { "uid-a" }, 12, new[] { "hero-1" }, Rules);
        Assert.Equal((1, 0), (Hero(state, "hero-2").HalfFalls, Hero(state, "hero-2").HoursRested));

        state = Calendar.PassTime(state, "uid-a", 10 * Calendar.HoursPerDay, Array.Empty<string>(), Rules);
        Assert.Equal((0, 0), (Hero(state, "hero-2").HalfFalls, Hero(state, "hero-1").HalfFalls)); // jamais sous zéro
    }

    [Fact]
    public void TheWaitBeforeLeaving_IsRestToo_AndRestWithoutFallsIsNotBanked()
    {
        var state = Two(aliceHour: 0, bobHour: 48);
        state = state with { Heroes = state.Heroes.Select(h => h.Id == "hero-2" ? h with { HalfFalls = 2 } : h).ToList() };

        state = Calendar.Depart(state, new[] { "uid-a", "uid-b" }, Quest("a"), Rules);

        Assert.Equal(0, Hero(state, "hero-2").HalfFalls); // 2 jours d'attente : une chute de moins
        Assert.Equal(0, Hero(state, "hero-1").HoursRested); // sans chute, rien ne s'accumule
    }

    [Fact]
    public void TimeOnlyMovesForward()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.PassTime(Two(0, 0), "uid-a", -1, Array.Empty<string>(), Rules));
        Assert.Equal(1, Calendar.DayOf(Calendar.HoursPerDay));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(18)]
    public void AQuestLasts_HalfDays(int hours) =>
        Assert.Throws<ArgumentException>(() => (Quest("q") with { DurationHours = hours }).Validate());
}
