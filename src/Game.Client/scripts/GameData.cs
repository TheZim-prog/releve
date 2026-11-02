using Game.Core.Combat;
using Godot;

namespace Game.Client;

/// <summary>
/// Contenu du jeu lu dans le dossier <c>data/</c> du dépôt (prototype : on le cherche en remontant depuis le projet
/// Godot ; l'export embarquera les données plus tard).
/// </summary>
public sealed class GameData
{
    public required CombatRules Rules { get; init; }

    /// <summary>Tous les types d'unités (héros, ennemis, civils), par identifiant.</summary>
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }

    public static GameData Load()
    {
        var root = FindDataDirectory();
        var rules = CombatRules.FromJson(File.ReadAllText(Path.Combine(root, "combat-rules.json")));
        var units = Directory.GetFiles(Path.Combine(root, "units"), "*.json")
            .Order(StringComparer.Ordinal)
            .SelectMany(file => UnitDefinition.ListFromJson(File.ReadAllText(file)))
            .ToDictionary(u => u.Id);
        foreach (var unit in units.Values)
            unit.ValidateAgainst(rules);
        return new GameData { Rules = rules, Units = units };
    }

    public static ScenarioDefinition LoadScenario(string id) =>
        ScenarioDefinition.FromJson(File.ReadAllText(Path.Combine(FindDataDirectory(), "scenarios", id + ".json")));

    private static string FindDataDirectory()
    {
        var directory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        for (; directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "combat-rules.json")))
                return candidate;
        }
        throw new DirectoryNotFoundException("The data/ folder was not found above the Godot project.");
    }
}
