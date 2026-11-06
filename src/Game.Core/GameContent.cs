using System.Security.Cryptography;
using System.Text;
using Game.Core.Combat;

namespace Game.Core;

/// <summary>
/// Contenu du jeu (dossier <c>data/</c>) : règles, types d'unités, scénarios. Partagé par le client et le serveur, qui
/// doivent avoir exactement le même pour que leurs simulations restent identiques : <see cref="Version"/> le vérifie.
/// </summary>
public sealed class GameContent
{
    public required CombatRules Rules { get; init; }

    /// <summary>Tous les types d'unités (héros, ennemis, civils), par identifiant.</summary>
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }

    public required IReadOnlyDictionary<string, ScenarioDefinition> Scenarios { get; init; }

    /// <summary>Empreinte du contenu chargé : deux contenus de même version donnent les mêmes combats.</summary>
    public required string Version { get; init; }

    /// <summary>Charge et valide le contenu d'un dossier <c>data/</c>.</summary>
    public static GameContent Load(string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        // L'empreinte porte exactement sur les textes chargés (un fichier ignoré ne la change pas).
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string Read(string relativePath)
        {
            var text = File.ReadAllText(Path.Combine(dataDirectory, relativePath));
            // Fins de ligne normalisées : Git peut les convertir différemment selon la machine (Windows, Raspberry Pi).
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath.Replace('\\', '/') + "\n" + text.Replace("\r\n", "\n") + "\n"));
            return text;
        }

        var rules = CombatRules.FromJson(Read("combat-rules.json"));
        var units = JsonFiles(dataDirectory, "units")
            .SelectMany(file => UnitDefinition.ListFromJson(Read(file)))
            .ToDictionary(u => u.Id);
        foreach (var unit in units.Values)
            unit.ValidateAgainst(rules);
        var scenarios = JsonFiles(dataDirectory, "scenarios")
            .Select(file => ScenarioDefinition.FromJson(Read(file)))
            .ToDictionary(s => s.Id);
        return new GameContent
        {
            Rules = rules, Units = units, Scenarios = scenarios, Version = Convert.ToHexString(hash.GetHashAndReset())[..16],
        };
    }

    /// <summary>
    /// Le dossier <c>data/</c> le plus proche en remontant depuis <paramref name="startDirectory"/> (à côté d'un serveur
    /// publié, ou celui du dépôt quand on lance depuis les sources), ou <c>null</c>.
    /// </summary>
    public static string? FindDataDirectory(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "combat-rules.json")))
                return candidate;
        }
        return null;
    }

    /// <summary>Fichiers JSON d'un sous-dossier, en chemins relatifs, dans un ordre stable.</summary>
    private static IEnumerable<string> JsonFiles(string dataDirectory, string folder) =>
        Directory.GetFiles(Path.Combine(dataDirectory, folder), "*.json")
            .Select(f => Path.Combine(folder, Path.GetFileName(f)))
            .Order(StringComparer.Ordinal);
}
