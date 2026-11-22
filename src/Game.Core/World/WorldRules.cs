using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Objectives;

namespace Game.Core.World;

/// <summary>Un libellé de difficulté de test et la chance (en %) à partir de laquelle il s'applique.</summary>
public sealed record DifficultyLabel(QuestDifficulty Difficulty, int From);

/// <summary>
/// Les règles de la carte du monde (data/world/rules.json) : les bornes d'une chance de test, les libellés de difficulté
/// qui la montrent (docs/design.md 4.2b : des plages fixes, cohérentes partout), et la gravité par défaut de chaque type
/// d'objectif (docs/design.md 4.2).
/// </summary>
public sealed record WorldRules
{
    public required int MinChance { get; init; }
    public required int MaxChance { get; init; }

    /// <summary>Du plus facile au plus difficile : chacun commence à sa chance <see cref="DifficultyLabel.From"/>.</summary>
    public required IReadOnlyList<DifficultyLabel> Difficulty { get; init; }

    /// <summary>Ce qu'ajoute un niveau de gravité, selon l'objectif principal de la mission.</summary>
    public required IReadOnlyDictionary<ObjectiveKind, GravityStep> Gravity { get; init; }

    public static WorldRules FromJson(string json)
    {
        var rules = DataJson.Deserialize<WorldRules>(json);
        rules.Validate();
        return rules;
    }

    /// <summary>La chance ramenée dans ses bornes.</summary>
    public int Clamp(int chance) => Math.Clamp(chance, MinChance, MaxChance);

    /// <summary>Le libellé d'une chance : le premier (le plus facile) dont le seuil est atteint.</summary>
    public QuestDifficulty LabelOf(int chance) => Difficulty.First(d => chance >= d.From).Difficulty;

    /// <summary>La gravité par défaut d'une mission : celle de son premier objectif principal.</summary>
    public GravityStep GravityFor(IReadOnlyList<ObjectiveDefinition> objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        var main = objectives.FirstOrDefault(o => !o.Secondary)?.Kind ?? ObjectiveKind.Eliminate;
        return Gravity.GetValueOrDefault(main, GravityStep.None);
    }

    public void Validate()
    {
        if (MinChance < 0 || MaxChance > 100 || MinChance > MaxChance)
            throw new ArgumentException("World rules need 0 <= minChance <= maxChance <= 100.");
        if (Difficulty is not { Count: > 0 } || Difficulty.Any(d => d is null) || Difficulty[^1].From > MinChance)
            throw new ArgumentException("Difficulty labels must cover every chance (the last one from the minimum chance or lower).");
        for (var i = 1; i < Difficulty.Count; i++)
        {
            if (Difficulty[i].From >= Difficulty[i - 1].From)
                throw new ArgumentException("Difficulty labels go from the easiest (highest chance) to the hardest.");
        }
        if (Gravity is null || Gravity.Values.Any(g => g is null))
            throw new ArgumentException("World rules need a gravity step per objective kind (or none).");
    }

    /// <summary>Les unités que la gravité ajoute existent.</summary>
    public void ValidateAgainst(IReadOnlyDictionary<string, UnitDefinition> units)
    {
        foreach (var step in Gravity.Values)
            step.Validate(units);
    }
}
