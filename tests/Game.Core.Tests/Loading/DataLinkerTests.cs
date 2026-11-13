using System.Text.Json;
using Game.Core.Combat;
using Game.Core.Loading;

namespace Game.Core.Tests.Loading;

/// <summary>Références entre fichiers de données : une compétence ou une unité décrite une fois, désignée par son identifiant.</summary>
public class DataLinkerTests
{
    private const string Skills = """
        [
          // Les commentaires et la virgule finale sont permis, comme dans tout data/.
          { "id": "shot", "name": "Tir", "weight": "light", "range": 5, "minDamage": 5, "maxDamage": 8, "cooldownMs": 0 },
          { "id": "place-turret", "weight": "light", "range": 3, "minDamage": 0, "maxDamage": 0, "cooldownMs": 0, "summon": "turret" },
        ]
        """;

    private const string Units = """
        [
          {
            "id": "turret", "name": "Tourelle", "maxHp": 30, "initiative": 50, "mobility": 0, "onDefeat": "removed",
            "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "basic", "skills": ["shot"]
          },
          {
            "id": "tech", "name": "Tech", "maxHp": 80, "initiative": 10, "mobility": 4, "onDefeat": "downed",
            "basicAttack": "shot", "opportunityAttacksPerCycle": 1, "behavior": null,
            "skills": [
              "shot",
              "place-turret",
              { "id": "jab", "weight": "light", "range": 1, "minDamage": 2, "maxDamage": 3, "cooldownMs": 0 }
            ]
          }
        ]
        """;

    private static DataLinker Linker(string skills, string units)
    {
        var linker = new DataLinker();
        linker.AddSkills("skills.json", skills);
        linker.AddUnits("units.json", units);
        return linker;
    }

    private static Dictionary<string, UnitDefinition> UnitsOf(DataLinker linker) =>
        linker.Units().ToDictionary(item => item.Id, item => UnitDefinition.ListFromJson($"[{item.Json}]").Single());

    private static Dictionary<string, SkillDefinition> SkillsOf(DataLinker linker) =>
        linker.Skills().ToDictionary(item => item.Id, item => SkillDefinition.ListFromJson($"[{item.Json}]").Single());

    [Fact]
    public void References_AreReplacedByWhatTheyDesignate()
    {
        var linker = Linker(Skills, Units);

        var units = UnitsOf(linker);
        var skills = SkillsOf(linker);

        // Le tech : deux compétences de la bibliothèque, et une écrite sur place.
        Assert.Equal(new[] { "shot", "place-turret", "jab" }, units["tech"].Skills.Select(s => s.Id));
        Assert.Equal(skills["shot"], units["tech"].Skills[0]);
        // La compétence d'invocation porte l'unité entière, avec ses propres compétences.
        var summoned = units["tech"].Skills[1].Summon!;
        Assert.Equal(("turret", "shot"), (summoned.Id, summoned.Skills.Single().Id));
        Assert.Equal("turret", skills["place-turret"].Summon!.Id);
        // Chaque entrée garde le nom de son fichier, pour les messages d'erreur.
        Assert.All(linker.Units().Concat(linker.Skills()), item => Assert.EndsWith(".json", item.Source));
    }

    [Fact]
    public void AUnitWrittenInPlace_CanStillReferToTheLibrary()
    {
        const string drone = """
            {
              "id": "drone", "name": "Drone", "maxHp": 10, "initiative": 50, "mobility": 0, "onDefeat": "removed",
              "basicAttack": null, "opportunityAttacksPerCycle": 0, "behavior": "basic", "skills": ["shot"]
            }
            """;
        var linker = Linker(Skills.Replace("\"summon\": \"turret\"", $"\"summon\": {drone}"), Units);

        Assert.Equal("shot", SkillsOf(linker)["place-turret"].Summon!.Skills.Single().Id);
    }

    [Fact]
    public void WhatIsInTheLibrary_CannotBeDescribedAgainInPlace()
    {
        // Le tech réécrit « shot » sur place au lieu de le désigner : il y aurait deux versions de la compétence.
        var linker = Linker(Skills, Units.Replace("\"id\": \"jab\"", "\"id\": \"shot\""));

        Assert.Contains("skills.json", Assert.Throws<ArgumentException>(() => linker.Units()).Message);
    }

    [Fact]
    public void AnUnknownReference_IsRefused_WithWhoDesignatesIt()
    {
        var unknownSkill = Linker(Skills, Units.Replace("\"skills\": [\"shot\"]", "\"skills\": [\"laser\"]"));
        var unknownUnit = Linker(Skills.Replace("\"summon\": \"turret\"", "\"summon\": \"drone\""), Units);

        Assert.Contains("unit 'turret'", Assert.Throws<ArgumentException>(() => unknownSkill.Units()).Message);
        Assert.Contains("skill 'place-turret'", Assert.Throws<ArgumentException>(() => unknownUnit.Skills()).Message);
    }

    [Fact]
    public void ACircularReference_IsRefused()
    {
        // La tourelle sait poser une tourelle, qui sait poser une tourelle…
        var linker = Linker(Skills, Units.Replace("\"skills\": [\"shot\"]", "\"skills\": [\"place-turret\"]"));

        Assert.Contains("Circular", Assert.Throws<ArgumentException>(() => linker.Units()).Message);
    }

    [Fact]
    public void AnId_IsDescribedOnlyOnce_AcrossAllFiles()
    {
        var linker = Linker(Skills, Units);

        Assert.Contains("skills.json", Assert.Throws<ArgumentException>(() => linker.AddSkills("more.json", """[ { "id": "shot" } ]""")).Message);
        Assert.Throws<ArgumentException>(() => linker.AddUnits("more.json", """[ { "id": "tech" } ]"""));
    }

    [Theory]
    [InlineData("""{ "id": "shot" }""")]   // pas une liste
    [InlineData("""[ { "name": "?" } ]""")] // pas d'identifiant
    [InlineData("""[ "shot" ]""")]
    public void AFile_IsAListOfEntriesWithAnId(string json)
    {
        Assert.Throws<JsonException>(() => new DataLinker().AddSkills("skills.json", json));
    }
}
