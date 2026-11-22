using Game.Core.Campaign;
using Game.Core.Progression;
using Game.Core.Tests.Progression;

namespace Game.Core.Tests.Campaign;

/// <summary>L'entraînement à l'académie, sur le petit arbre de test (branches a, b, c ; voir <see cref="ProgressionTestKit"/>).</summary>
public class TrainingTests
{
    private static readonly HeroCatalog Catalog = ProgressionTestKit.Catalog();

    /// <summary>Alice a un héros (hero-1 : a1, a2, a3), monté au niveau donné par cette XP ; Bob est inscrit.</summary>
    private static CampaignState WithHero(int xp = 0)
    {
        var state = CampaignState.New(CampaignStateTests.Scale).WithPlayer("uid-a", "Alice").WithPlayer("uid-b", "Bob");
        state = Academy.Enlist(state, Catalog, "uid-a", new Recruit("Héros", "hero", new[] { "a1", "a2", "a3" }));
        var quest = new QuestDefinition { Id = "q", Name = "Q", Scenario = "s", Difficulty = QuestDifficulty.Easy, Xp = xp };
        return QuestRewards.Grant(state, quest, new[] { "hero-1" }, victory: true, Catalog.Rules);
    }

    private static HeroSheet Sheet(TrainingResult result) => Assert.Single(result.State!.Heroes).Sheet;

    [Fact]
    public void AStatPoint_GainedByLevelling_GoesIntoAStat()
    {
        var result = Training.SpendStatPoint(WithHero(xp: 100), Catalog, "uid-a", "hero-1", StatKind.Power);

        Assert.Null(result.Refusal);
        Assert.Equal(1, Sheet(result).Points.Power);
        Assert.Equal(TrainingRefusal.NoPointToSpend,
            Training.SpendStatPoint(result.State!, Catalog, "uid-a", "hero-1", StatKind.Hp).Refusal);
    }

    [Fact]
    public void ASkillPoint_LearnsAnOpenCase_AndEquipsItIfASlotIsFree()
    {
        var state = WithHero(xp: 100); // niveau 2 : un point de compétence (3 + 2/2 = 4)
        Assert.Equal(TrainingRefusal.NotAllowed, Training.Learn(state, Catalog, "uid-a", "hero-1", "b3").Refusal); // palier fermé

        var learned = Training.Learn(state, Catalog, "uid-a", "hero-1", "weakPoint");

        Assert.Contains("weakPoint", Sheet(learned).Learned);
        Assert.Equal("weakPoint", Sheet(learned).Passive); // il n'en avait pas : équipé
        Assert.Equal(TrainingRefusal.NoPointToSpend, Training.Learn(learned.State!, Catalog, "uid-a", "hero-1", "b1").Refusal);
    }

    [Fact]
    public void AtLevelOne_ThereIsNothingToSpend()
    {
        var state = WithHero();

        Assert.Equal(TrainingRefusal.NoPointToSpend, Training.SpendStatPoint(state, Catalog, "uid-a", "hero-1", StatKind.Hp).Refusal);
        Assert.Equal(TrainingRefusal.NoPointToSpend, Training.Learn(state, Catalog, "uid-a", "hero-1", "b1").Refusal);
    }

    [Fact]
    public void TheEquipment_ChangesFreely_WithinTheRules()
    {
        var state = WithHero();

        var variants = Training.Equip(state, Catalog, "uid-a", "hero-1", new[] { "a1v", "a3" }, passive: null);

        Assert.Equal(new[] { "a1v", "a3" }, Sheet(variants).Equipped);
        Assert.Equal(TrainingRefusal.NotAllowed, // b1 n'est pas appris
            Training.Equip(state, Catalog, "uid-a", "hero-1", new[] { "b1" }, null).Refusal);
        Assert.Equal(TrainingRefusal.NotAllowed, // une compétence et sa variante dans le même emplacement
            Training.Equip(state, Catalog, "uid-a", "hero-1", new[] { "a1", "a1v" }, null).Refusal);
        Assert.Equal(TrainingRefusal.NotAllowed, // passif pas appris
            Training.Equip(state, Catalog, "uid-a", "hero-1", new[] { "a1" }, "grip").Refusal);
    }

    [Fact]
    public void OnlyTheOwner_TrainsALivingHero()
    {
        var state = WithHero(xp: 100);

        Assert.Equal(TrainingRefusal.NotYourHero, Training.SpendStatPoint(state, Catalog, "uid-b", "hero-1", StatKind.Hp).Refusal);
        var dead = state with { Heroes = state.Heroes.Select(h => h with { IsDead = true }).ToList() };
        Assert.Equal(TrainingRefusal.HeroDead, Training.SpendStatPoint(dead, Catalog, "uid-a", "hero-1", StatKind.Hp).Refusal);
    }

    [Fact]
    public void TheScreen_KnowsWhatCanBeLearnedAndEquipped()
    {
        var sheet = Assert.Single(WithHero(xp: 100).Heroes).Sheet;

        Assert.Contains(Training.Learnable(Catalog, sheet), n => n.Id == "weakPoint");
        Assert.DoesNotContain(Training.Learnable(Catalog, sheet), n => n.Id == "a4"); // palier 3 : 4 points en a
        Assert.Equal(new[] { "a1", "a1v" }, Training.Equippable(Catalog, sheet).Single(o => o.Node.Id == "a1").Versions);
        Assert.Empty(Training.EquippablePassives(Catalog, sheet));
    }
}
