using System.Text.Json;
using Game.Core.Combat;
using Game.Core.Grids;

namespace Game.Core.Tests.Combat;

public class UnitDefinitionTests
{
    private const string Brawler = """
        {
          "id": "brawler", "name": "Cogneur",
          "maxHp": 60, "initiative": 9, "mobility": 4,
          "onDefeat": "removed", "basicAttack": "punch", "opportunityAttacksPerCycle": 1,
          "behavior": "basic",
          "skills": [
            { "id": "punch", "weight": "light", "range": 1, "minDamage": 8, "maxDamage": 12, "cooldownMs": 0 }
          ]
        }
        """;

    private const string Hero = """
        {
          "id": "fighter", "name": "Combattant",
          "maxHp": 110, "initiative": 10, "mobility": 4,
          "onDefeat": "downed", "basicAttack": "combo", "opportunityAttacksPerCycle": 1,
          "behavior": null,
          "skills": [
            { "id": "combo", "weight": "light", "range": 1, "minDamage": 10, "maxDamage": 14, "cooldownMs": 0 },
            { "id": "stun", "weight": "light", "range": 1, "minDamage": 4, "maxDamage": 6, "cooldownMs": 8000,
              "effects": [ { "status": "stunned", "durationMs": 1500 } ] }
          ]
        }
        """;

    [Fact]
    public void ListFromJson_ReadsUnits()
    {
        var units = UnitDefinition.ListFromJson($"[{Brawler}, {Hero}]");

        Assert.Equal(new[] { "brawler", "fighter" }, units.Select(u => u.Id));
        var brawler = units[0];
        Assert.Equal("Cogneur", brawler.Name);
        Assert.Equal(Behaviors.Basic, brawler.Behavior);
        Assert.Equal(DefeatOutcome.Removed, brawler.OnDefeat);
        Assert.Null(units[1].Behavior);
        Assert.Equal(2, units[1].Skills.Count);
    }

    [Fact]
    public void ToSetup_PlacesAnInstanceForACombat()
    {
        var fighter = UnitDefinition.ListFromJson($"[{Hero}]").Single();

        var setup = fighter.ToSetup(new UnitId(3), team: 0, new Cell(2, 1), initialGauge: 40);

        Assert.Equal(new UnitSetup(new UnitId(3), 0, new Cell(2, 1), fighter, 40), setup);
    }

    [Fact]
    public void InstanceInCombat_ReadsItsStatsFromTheDefinition()
    {
        var fighter = UnitDefinition.ListFromJson($"[{Hero}]").Single();
        var sim = new CombatSimulation(new CombatSetup(CombatTestKit.Rules with { GaugeCapacity = 1000 }, new Grid(3, 1),
            new[] { fighter.ToSetup(new UnitId(3), team: 0, new Cell(0, 0)) }, Seed: 1));

        var unit = sim.Units.Single();

        Assert.Same(fighter, unit.Definition);
        Assert.Equal((110, 110, 10, 4), (unit.MaxHp, unit.Hp, unit.Initiative, unit.Mobility));
        Assert.Equal("combo", unit.BasicAttack!.Id);
    }

    [Fact]
    public void ValidateAgainst_RejectsAnInitiativeAboveTheGaugeCapacity()
    {
        var brawler = UnitDefinition.ListFromJson($"[{Brawler}]").Single();

        brawler.ValidateAgainst(CombatTestKit.Rules with { GaugeCapacity = 9 });
        Assert.Throws<ArgumentException>(() => brawler.ValidateAgainst(CombatTestKit.Rules with { GaugeCapacity = 8 }));
    }

    [Theory]
    [InlineData("\"behavior\": \"basic\",", "")]                  // comportement à écrire, même null
    [InlineData("\"basicAttack\": \"punch\",", "")]               // attaque de base à écrire, même null
    [InlineData("\"onDefeat\": \"removed\"", "\"onDefeat\": \"fled\"")]
    [InlineData("\"maxHp\": 60", "\"maxHp\": 60, \"armor\": 3")]  // champ inconnu
    public void ListFromJson_RejectsMalformedUnits(string valid, string invalid)
    {
        Assert.Throws<JsonException>(() => UnitDefinition.ListFromJson($"[{Brawler.Replace(valid, invalid)}]"));
    }

    [Theory]
    [InlineData("\"maxHp\": 60", "\"maxHp\": 0")]
    [InlineData("\"mobility\": 4", "\"mobility\": -1")]
    [InlineData("\"initiative\": 9", "\"initiative\": -1")]
    [InlineData("\"opportunityAttacksPerCycle\": 1", "\"opportunityAttacksPerCycle\": -1")]
    [InlineData("\"cooldownMs\": 0 }", "\"cooldownMs\": 0, \"chargeMs\": 500 }")]  // attaque de base chargée
    [InlineData("\"skills\": [", "\"skills\": [ null,")]
    [InlineData("\"basicAttack\": \"punch\"", "\"basicAttack\": \"kick\"")]
    [InlineData("\"behavior\": \"basic\"", "\"behavior\": \"berserk\"")]
    [InlineData("\"minDamage\": 8", "\"minDamage\": 20")]
    public void ListFromJson_RejectsInvalidValues_NamingTheUnit(string valid, string invalid)
    {
        var error = Assert.Throws<ArgumentException>(() => UnitDefinition.ListFromJson($"[{Brawler.Replace(valid, invalid)}]"));
        Assert.Contains("brawler", error.Message);
    }

    [Theory]
    [InlineData("\"name\": \"Cogneur\"", "\"name\": \"\"")]
    [InlineData("\"id\": \"brawler\"", "\"id\": \"  \"")]
    public void ListFromJson_RejectsMissingIdentity(string valid, string invalid)
    {
        Assert.Throws<ArgumentException>(() => UnitDefinition.ListFromJson($"[{Brawler.Replace(valid, invalid)}]"));
    }

    [Fact]
    public void Validate_RejectsAMissingSkillList_NamingTheUnit()
    {
        var brawler = UnitDefinition.ListFromJson($"[{Brawler}]").Single() with { Skills = null! };

        var error = Assert.Throws<ArgumentException>(() => brawler.Validate());
        Assert.Contains("brawler", error.Message);
    }

    [Fact]
    public void ListFromJson_RejectsDuplicateIds()
    {
        Assert.Throws<ArgumentException>(() => UnitDefinition.ListFromJson($"[{Brawler}, {Brawler}]"));
    }
}
