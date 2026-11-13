using Game.Core.Combat;
using Game.Core.Loading;
using Game.Core.Progression;
using Game.Core.Scenarios;

namespace Game.Core;

/// <summary>
/// Contenu du jeu (dossier <c>data/</c>) : règles, compétences, types d'unités, arbres et héros, scénarios. Partagé par
/// le client et le serveur, qui doivent avoir exactement le même pour que leurs simulations restent identiques :
/// <see cref="Version"/> le vérifie.
/// </summary>
public sealed class GameContent
{
    public required CombatRules Rules { get; init; }

    /// <summary>Bibliothèque des compétences, par identifiant : chacune y est décrite une seule fois.</summary>
    public IReadOnlyDictionary<string, SkillDefinition> Skills { get; init; } = new Dictionary<string, SkillDefinition>();

    /// <summary>Tous les types d'unités (archétypes de héros, ennemis, gadgets, civils), par identifiant.</summary>
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }

    /// <summary>Arbres de compétences et héros prêts à jouer, ou <c>null</c> si ce contenu n'en a pas.</summary>
    public HeroCatalog? Heroes { get; init; }

    public required IReadOnlyDictionary<string, ScenarioDefinition> Scenarios { get; init; }

    /// <summary>Empreinte du contenu chargé : deux contenus de même version donnent les mêmes combats.</summary>
    public required string Version { get; init; }

    /// <summary>Charge et valide le contenu d'un dossier <c>data/</c>.</summary>
    public static GameContent Load(string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        return ContentLoader.Load(dataDirectory);
    }

    /// <summary>État initial du combat d'un scénario de ce contenu.</summary>
    public CombatSetup SetupFor(string scenarioId) =>
        Scenarios.TryGetValue(scenarioId, out var scenario)
            ? scenario.ToSetup(Rules, Units, Heroes)
            : throw new ArgumentException($"Unknown scenario '{scenarioId}'.", nameof(scenarioId));

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
}
