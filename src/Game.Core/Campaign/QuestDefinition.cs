using Game.Core.Scenarios;

namespace Game.Core.Campaign;

/// <summary>
/// La difficulté affichée d'une quête (docs/design.md 4.2b et 4.3) : des libellés, pas de pourcentage.
/// </summary>
public enum QuestDifficulty
{
    VeryEasy,
    Easy,
    Hard,
    VeryHard,
}

/// <summary>
/// Une quête du panneau du QG (data/quests) : un scénario où se placent les héros des joueurs, sa difficulté affichée,
/// et ses récompenses (docs/design.md 4.6) : l'XP que reçoit chaque héros participant, et les matériaux versés au pot
/// commun.
/// </summary>
public sealed record QuestDefinition
{
    public required string Id { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    /// <summary>Ce que le panneau en dit.</summary>
    public string Description { get; init; } = "";

    /// <summary>Le scénario joué : il doit avoir des cases pour les héros des joueurs.</summary>
    public required string Scenario { get; init; }

    public required QuestDifficulty Difficulty { get; init; }

    /// <summary>XP reçue par chaque héros participant, en entier, qu'ils soient un ou cinq.</summary>
    public required int Xp { get; init; }

    /// <summary>Matériaux versés au pot commun, par identifiant (une seule sorte pour commencer : « materials »).</summary>
    public IReadOnlyDictionary<string, int> Materials { get; init; } = new Dictionary<string, int>();

    /// <summary>Le lieu de la carte du monde où elle se joue (un lieu de mission), ou <c>null</c> sans carte.</summary>
    public string? Site { get; init; }

    /// <summary>Ce qu'ajoute chaque niveau de gravité à cette quête, à la place de la règle de son objectif, ou <c>null</c>.</summary>
    public World.GravityStep? Gravity { get; init; }

    public static IReadOnlyList<QuestDefinition> ListFromJson(string json) =>
        DataJson.ValidateList(DataJson.Deserialize<List<QuestDefinition?>>(json), q => q.Validate(), q => q.Id, "quest");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Scenario))
            throw new ArgumentException("A quest needs an id, a name and a scenario.");
        if (!Enum.IsDefined(Difficulty))
            throw new ArgumentException($"Quest '{Id}' has an unknown difficulty.");
        if (Xp < 0 || Materials is null || Materials.Values.Any(m => m < 0))
            throw new ArgumentException($"Quest '{Id}' cannot give negative rewards.");
    }

    /// <summary>Son scénario existe et accueille des héros de joueurs.</summary>
    public void ValidateAgainst(IReadOnlyDictionary<string, ScenarioDefinition> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        if (!scenarios.TryGetValue(Scenario, out var scenario))
            throw new ArgumentException($"Quest '{Id}': unknown scenario '{Scenario}'.");
        if (scenario.HeroSlots is not { Count: > 0 })
            throw new ArgumentException($"Quest '{Id}': scenario '{Scenario}' has no hero slots for the players' heroes.");
    }
}
