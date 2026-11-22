using Game.Core.Campaign;
using Game.Core.Progression;
using Game.Core.Tests.Progression;

namespace Game.Core.Tests.Campaign;

/// <summary>L'académie, sur le petit arbre de test (branches a, b, c ; voir <see cref="ProgressionTestKit"/>).</summary>
public class AcademyTests
{
    private static readonly HeroCatalog Catalog = ProgressionTestKit.Catalog();

    private static readonly CampaignState Enrolled =
        CampaignState.New(CampaignStateTests.Scale).WithPlayer("uid-a", "Alice").WithPlayer("uid-b", "Bob");

    private static Recruit Recruit(params string[] learned) => new("Rempart", "hero", learned);

    private static RecruitRefusal? Check(Recruit recruit, CampaignState? state = null, string owner = "uid-a") =>
        Academy.Check(state ?? Enrolled, Catalog, owner, recruit);

    [Fact]
    public void ARecruit_StartsAtLevelOne_EquippedWithWhatTheyLearned()
    {
        var state = Academy.Enlist(Enrolled, Catalog, "uid-a", Recruit("a1", "a2", "weakPoint"));

        var hero = Assert.Single(state.Heroes);
        Assert.Equal(("hero-1", "Rempart", "uid-a", 0, 0, false), (hero.Id, hero.Name, hero.Owner, hero.Xp, hero.HalfFalls, hero.IsDead));
        Assert.Equal(1, hero.Sheet.Level);
        Assert.Equal(new[] { "a1", "a2" }, hero.Sheet.Equipped);
        Assert.Equal("weakPoint", hero.Sheet.Passive);
        Catalog.Check(hero.Sheet);
    }

    [Fact]
    public void ASkillNotCodedYet_IsLearned_ButNotEquipped()
    {
        var hero = Academy.Enlist(Enrolled, Catalog, "uid-a", Recruit("c1", "c2", "a1")).Heroes.Single();

        Assert.Equal(new[] { "c1", "c2", "a1" }, hero.Sheet.Learned);
        Assert.Equal(new[] { "c1", "a1" }, hero.Sheet.Equipped);
    }

    [Fact]
    public void ARecruit_SpendsExactlyTheStartingPoints_InOpenTiers()
    {
        Assert.Equal(RecruitRefusal.InvalidSkills, Check(Recruit("a1", "a2")));
        Assert.Equal(RecruitRefusal.InvalidSkills, Check(Recruit("a1", "a2", "b1", "b2")));
        Assert.Equal(RecruitRefusal.InvalidSkills, Check(Recruit("a1", "b1", "a3"))); // a3 demande 2 points en a
        Assert.Equal(RecruitRefusal.InvalidSkills, Check(Recruit("a1", "a1", "a2")));
        Assert.Equal(RecruitRefusal.InvalidSkills, Check(Recruit("a1", "a2", "zz")));
        Assert.Null(Check(Recruit("a1", "a2", "a3")));
    }

    [Fact]
    public void ARecruit_NeedsANameAndAKnownArchetype_AndAnEnrolledPlayer()
    {
        Assert.Equal(RecruitRefusal.InvalidName, Check(Recruit("a1", "a2", "a3") with { Name = "  " }));
        Assert.Equal(RecruitRefusal.InvalidName, Check(Recruit("a1", "a2", "a3") with { Name = new string('x', Academy.MaxNameLength + 1) }));
        Assert.Equal(RecruitRefusal.InvalidName, Check(Recruit("a1", "a2", "a3") with { Name = "A\nB" }));
        Assert.Equal(RecruitRefusal.UnknownArchetype, Check(Recruit("a1", "a2", "a3") with { Archetype = "dragon" }));
        Assert.Equal(RecruitRefusal.NotEnrolled, Check(Recruit("a1", "a2", "a3"), owner: "uid-z"));
    }

    [Fact]
    public void APlayer_HasAtMostTheirDuoAlive_AndReplacesTheDead()
    {
        var recruit = Recruit("a1", "a2", "a3");
        var duo = Academy.Enlist(Academy.Enlist(Enrolled, Catalog, "uid-a", recruit), Catalog, "uid-a", recruit);

        Assert.Equal(RecruitRefusal.DuoFull, Check(recruit, duo));
        Assert.Null(Check(recruit, duo, owner: "uid-b")); // le duo de Bob est à part
        var oneDead = duo with { Heroes = duo.Heroes.Select((h, i) => i == 0 ? h with { IsDead = true } : h).ToList() };
        var successor = Academy.Enlist(oneDead, Catalog, "uid-a", recruit);
        Assert.Equal(new[] { "hero-1", "hero-2", "hero-3" }, successor.Heroes.Select(h => h.Id));
    }

    [Fact]
    public void ANewHero_TakesTheNumberAfterTheHighest()
    {
        var two = Academy.Enlist(Academy.Enlist(Enrolled, Catalog, "uid-a", Recruit("a1", "a2", "a3")), Catalog, "uid-b", Recruit("a1", "a2", "a3"));
        var gap = two with { Heroes = two.Heroes.Skip(1).ToList() }; // hero-1 a quitté la sauvegarde

        Assert.Equal("hero-3", Academy.Enlist(gap, Catalog, "uid-a", Recruit("a1", "a2", "a3")).Heroes[^1].Id);
    }

    [Fact]
    public void TheCreationScreen_OffersOnlyOpenTiers_AndDropsWhatALowerChoiceOpened()
    {
        Assert.DoesNotContain(Academy.Learnable(Catalog, "hero", new[] { "a1" }), n => n.Id == "a3");
        Assert.Contains(Academy.Learnable(Catalog, "hero", new[] { "a1", "a2" }), n => n.Id == "a3");
        Assert.Empty(Academy.Learnable(Catalog, "hero", new[] { "a1", "a2", "a3" }));

        // a2 retiré : a3 (palier 2 de a) n'est plus ouvert.
        Assert.Equal(new[] { "a1" }, Academy.Prune(Catalog, "hero", new[] { "a1", "a3" }));
    }
}
