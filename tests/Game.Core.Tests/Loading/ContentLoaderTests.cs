using System.Text.Json;

namespace Game.Core.Tests.Loading;

/// <summary>Chargement d'un dossier data/ : ce qui est facultatif, et des erreurs qui disent où chercher.</summary>
public sealed class ContentLoaderTests : IDisposable
{
    private static readonly string RealData = Path.Combine(AppContext.BaseDirectory, "data");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "releve-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Copie du vrai contenu, à abîmer.</summary>
    private string CopyOfTheRealData()
    {
        foreach (var file in Directory.GetFiles(RealData, "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_directory, Path.GetRelativePath(RealData, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return _directory;
    }

    private void Rewrite(string relativePath, string old, string replacement)
    {
        var path = Path.Combine(_directory, relativePath);
        var text = File.ReadAllText(path);
        Assert.Contains(old, text);
        File.WriteAllText(path, text.Replace(old, replacement));
    }

    [Fact]
    public void ACopyOfTheRealContent_HasTheSameVersion()
    {
        Assert.Equal(GameContent.Load(RealData).Version, GameContent.Load(CopyOfTheRealData()).Version);
    }

    [Fact]
    public void AContentWithoutProgression_LoadsWithoutHeroes()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "units"));
        Directory.CreateDirectory(Path.Combine(_directory, "scenarios"));
        File.Copy(Path.Combine(RealData, "combat-rules.json"), Path.Combine(_directory, "combat-rules.json"));
        File.WriteAllText(Path.Combine(_directory, "units", "units.json"), """
            [ {
              "id": "thug", "name": "Voyou", "maxHp": 50, "initiative": 90, "mobility": 3, "onDefeat": "removed",
              "basicAttack": "hit", "opportunityAttacksPerCycle": 1, "behavior": "basic",
              "skills": [ { "id": "hit", "weight": "light", "range": 1, "minDamage": 5, "maxDamage": 6, "cooldownMs": 0 } ]
            } ]
            """);
        File.WriteAllText(Path.Combine(_directory, "scenarios", "duel.json"), """
            { "id": "duel", "name": "Duel", "seed": 1, "map": [ "..." ],
              "units": [ { "unit": "thug", "team": 0, "x": 0, "y": 0 }, { "unit": "thug", "team": 1, "x": 2, "y": 0 } ] }
            """);

        var content = GameContent.Load(_directory);

        Assert.Null(content.Heroes);
        Assert.Empty(content.Skills);
        Assert.Equal(2, content.SetupFor("duel").Units.Count);
    }

    [Fact]
    public void ADataError_NamesTheFile_AndTheSkillOrUnit()
    {
        CopyOfTheRealData();
        Rewrite("skills/tech/saboteur.json", "\"range\": 4", "\"rnge\": 4");

        var error = Assert.Throws<JsonException>(() => GameContent.Load(_directory));

        Assert.Contains("skills/tech/saboteur.json", error.Message);
        Assert.Contains("'emp'", error.Message);
    }

    [Fact]
    public void ARefusedValue_NamesTheFile()
    {
        CopyOfTheRealData();
        Rewrite("heroes/prologue.json", "\"equipped\": [\"dome\", \"leap\", \"projection\", \"shockwave\"]",
            "\"equipped\": [\"dome\", \"leap\", \"projection\", \"fortress\"]"); // pas achetée, et pas encore codée
        Assert.Contains("prologue-colossus", Assert.Throws<ArgumentException>(() => GameContent.Load(_directory)).Message);

        Dispose();
        CopyOfTheRealData();
        Rewrite("scenarios/street.json", "\"hero\": \"prologue-tech\"", "\"hero\": \"prologue-nobody\"");
        Assert.Contains("scenarios/street.json", Assert.Throws<ArgumentException>(() => GameContent.Load(_directory)).Message);
    }

    [Fact]
    public void ASkillAddedToTheLibrary_MustLeaveThePlannedListOfItsTree()
    {
        CopyOfTheRealData();
        // « catapult » est codée (ici : un coup quelconque), mais l'arbre du colosse la dit encore « à venir ».
        Rewrite("skills/colossus/wrestler.json", "\n]", """
            ,
              { "id": "catapult", "name": "Catapulte", "weight": "light", "range": 1, "minDamage": 3, "maxDamage": 4, "cooldownMs": 0 }
            ]
            """);

        Assert.Contains("catapult", Assert.Throws<ArgumentException>(() => GameContent.Load(_directory)).Message);

        Rewrite("trees/colossus.json", "    \"catapult\": \"Catapulte\",\n", "");
        Assert.True(GameContent.Load(_directory).Skills.ContainsKey("catapult"));
    }
}
