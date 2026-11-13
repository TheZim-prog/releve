using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Progression;
using Game.Core.Scenarios;
using Game.Core.Tests.Combat;
using static Game.Core.Tests.Progression.ProgressionTestKit;

namespace Game.Core.Tests.Progression;

/// <summary>Fiches de héros : ce qu'elles ont le droit de contenir, et l'unité de combat qu'elles donnent.</summary>
public class HeroSheetTests
{
    private static readonly HeroCatalog Heroes = Catalog();

    /// <summary>Toute la branche a (6 cases), dans l'ordre des paliers.</summary>
    private static readonly string[] BranchA = { "a1", "a2", "a3", "weakPoint", "a4", "aMaster" };

    [Fact]
    public void ASheet_GivesTheArchetypeWithItsEquippedSkillsPassiveAndPoints()
    {
        var points = new StatPoints { Hp = 3, Power = 2 };
        var sheet = Sheet(level: 20, learned: BranchA.Append("b1").ToArray(), equipped: new[] { "a2", "a1v", "a4", "b1" },
            passive: "weakPoint", points: points);

        var hero = Heroes.Unit(sheet);

        Assert.Equal("hero", hero.Definition.Id);
        // L'attaque de base, puis l'équipement dans l'ordre choisi (ici la variante de a1 à la place de a1).
        Assert.Equal(new[] { "strike", "a2", "a1v", "a4", "b1" }, hero.Definition.Skills.Select(s => s.Id));
        Assert.Equal("strike", hero.Definition.BasicAttack);
        Assert.Equal(PassiveKind.WeakPoint, hero.Definition.Passive);
        Assert.Same(points, hero.Points);
    }

    [Fact]
    public void ARecruit_StartsWithThreeSkillsOfTheFirstTiers_AndNoPassive()
    {
        var recruit = Sheet(level: 1, learned: new[] { "a1", "b1", "b2" }, equipped: new[] { "a1", "b1", "b2" });

        Assert.Equal(4, Heroes.Unit(recruit).Definition.Skills.Count);
        Assert.Null(Heroes.Unit(recruit).Definition.Passive);
        // Quatre cases pour trois points de compétence.
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(level: 1, learned: new[] { "a1", "a2", "b1", "b2" })));
    }

    [Fact]
    public void ATier_OpensWhenEnoughPointsAreSpentInTheLowerTiersOfItsBranch()
    {
        // Palier 2 : il faut 2 points dans la branche. Un seul ici (b1 est dans une autre branche).
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(learned: new[] { "a1", "b1", "a3" })));
        Heroes.Check(Sheet(learned: new[] { "a1", "a2", "a3" }));
        // Palier 3 : 4 points dans les paliers 1 et 2 de la branche.
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(learned: new[] { "a1", "a2", "a3", "a4" })));
        Heroes.Check(Sheet(learned: new[] { "a1", "a2", "a3", "weakPoint", "a4" }));
    }

    [Fact]
    public void ASkillToCome_CanBeLearnedToOpenATier_ButNotEquipped()
    {
        // c2 n'est pas codée : elle compte quand même pour ouvrir le palier 2 de la branche c.
        var sheet = Sheet(learned: new[] { "c1", "c2", "c3", "vigilance", "c4", "cMaster" }, equipped: new[] { "c3" }, passive: "vigilance");

        Heroes.Check(sheet);
        Assert.Throws<ArgumentException>(() => Heroes.Check(sheet with { Equipped = new[] { "c2" } }));
        Assert.Throws<ArgumentException>(() => Heroes.Check(sheet with { Passive = "cMaster" })); // passif à venir
    }

    [Fact]
    public void TheEquipment_ComesFromWhatIsLearned_OneVersionOfEachSkill()
    {
        var learned = new[] { "a1", "a2", "b1" };

        Heroes.Check(Sheet(learned: learned, equipped: new[] { "a1v", "a2" }));
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(learned: learned, equipped: new[] { "b2" })));        // pas achetée
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(learned: learned, equipped: new[] { "a1", "a1v" }))); // les deux versions
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(learned: learned, equipped: new[] { "a1", "a1" })));
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(learned: new[] { "a1", "a1v" })));                    // une variante ne s'achète pas
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(learned: learned, passive: "grip")));                 // passif pas acheté
    }

    [Fact]
    public void TheFourthSlot_OpensAtLevel10()
    {
        var learned = new[] { "a1", "a2", "b1", "b2" };

        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(level: 9, learned: learned, equipped: learned)));
        Heroes.Check(Sheet(level: 10, learned: learned, equipped: learned));
    }

    [Fact]
    public void LevelAndStatPoints_AreChecked()
    {
        Heroes.Check(Sheet(level: 5, points: new StatPoints { Hp = 2, Power = 1, Initiative = 1 })); // 4 points au niveau 5
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(level: 5, points: new StatPoints { Hp = 5 })));
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(level: 0)));
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet(level: 21)));
        Assert.Throws<ArgumentException>(() => Heroes.Check(Sheet() with { Archetype = "nobody" }));
    }

    [Fact]
    public void TheCatalog_RefusesAnIllegalHero_OrAMalformedTree()
    {
        Assert.Equal("h", Catalog(Sheet()).Sheets.Keys.Single());
        Assert.Throws<ArgumentException>(() => Catalog(Sheet(learned: new[] { "a4" })));
        Assert.Throws<ArgumentException>(() => Catalog(Sheet(), Sheet())); // deux héros du même nom
        Assert.Throws<ArgumentException>(() => Catalog().Unit("nobody"));
        Assert.Throws<ArgumentException>(() =>
            new HeroCatalog(Rules, Units, Skills, new[] { Tree with { Branches = Tree.Branches.Take(1).ToArray() } }, Array.Empty<HeroSheet>()));
        Assert.Throws<ArgumentException>(() =>
            new HeroCatalog(Rules, Units, Skills, new[] { Tree with { Archetype = "nobody" } }, Array.Empty<HeroSheet>()));
    }

    [Fact]
    public void AScenario_PlacesAHeroWithItsEquipmentAndPoints()
    {
        const string json = """
            {
              "id": "test", "name": "Test", "seed": 7,
              "map": [ "..." ],
              "units": [
                { "hero": "veteran", "team": 0, "x": 0, "y": 0 },
                { "unit": "hero", "team": 1, "x": 2, "y": 0 }
              ]
            }
            """;
        var heroes = Catalog(Sheet(id: "veteran", learned: new[] { "c1", "c2", "c3", "vigilance" }, equipped: new[] { "c1" },
            passive: "vigilance", points: new StatPoints { Hp = 2, Power = 1 }));
        var scenario = ScenarioDefinition.FromJson(json);

        var sim = new CombatSimulation(scenario.ToSetup(CombatTestKit.Rules, Units, heroes));

        var veteran = sim.FindUnit(new UnitId(1))!;
        Assert.Equal(new[] { "strike", "c1" }, veteran.Skills.Select(s => s.Id));
        Assert.Equal((120, 110), (veteran.MaxHp, veteran.PowerPercent)); // +10 % par point avec les règles de test
        Assert.Equal(2, veteran.OpportunityAttacksPerCycle);             // Vigilance : une attaque d'opportunité de plus
        // L'unité placée telle quelle : l'archétype nu, sans point ni passif.
        Assert.Equal((100, 100, 1), (sim.Units[1].MaxHp, sim.Units[1].PowerPercent, sim.Units[1].OpportunityAttacksPerCycle));

        Assert.Throws<ArgumentException>(() => scenario.ToSetup(CombatTestKit.Rules, Units));          // pas de héros dans ce contenu
        Assert.Throws<ArgumentException>(() => scenario.ToSetup(CombatTestKit.Rules, Units, Catalog())); // héros inconnu
    }

    [Theory]
    [InlineData("\"hero\": \"veteran\"", "\"hero\": \"veteran\", \"unit\": \"hero\"")] // les deux
    [InlineData("\"hero\": \"veteran\", ", "")]                                           // ni l'un ni l'autre
    public void AScenarioEntry_PlacesEitherAUnitOrAHero(string valid, string invalid)
    {
        const string json = """
            { "id": "test", "name": "Test", "seed": 7, "map": [ "..." ], "units": [ { "hero": "veteran", "team": 0, "x": 0, "y": 0 } ] }
            """;

        ScenarioDefinition.FromJson(json);
        Assert.Throws<ArgumentException>(() => ScenarioDefinition.FromJson(json.Replace(valid, invalid)));
    }

    [Fact]
    public void AScenario_RefusesAHeroWhosePointsPushItsInitiativeAboveTheGaugeCapacity()
    {
        var scenario = ScenarioDefinition.FromJson("""
            { "id": "test", "name": "Test", "seed": 7, "map": [ "..." ], "units": [ { "hero": "h", "team": 0, "x": 0, "y": 0 } ] }
            """);
        var fast = new Dictionary<string, UnitDefinition> { ["hero"] = Units["hero"] with { Initiative = 90 } };
        HeroCatalog With(int initiativePoints) =>
            new(Rules, fast, Skills, new[] { Tree }, new[] { Sheet(points: new StatPoints { Initiative = initiativePoints }) });

        scenario.ToSetup(CombatTestKit.Rules, fast, With(1)); // 99 : sous la capacité (100)
        Assert.Throws<ArgumentException>(() => scenario.ToSetup(CombatTestKit.Rules, fast, With(2))); // 108
    }
}
