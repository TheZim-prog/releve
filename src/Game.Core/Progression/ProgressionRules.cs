namespace Game.Core.Progression;

/// <summary>
/// Règles de progression des héros, chargées depuis <c>data/progression.json</c> : niveaux, points de stats et de
/// compétences, emplacements, forme des arbres (docs/design.md 2.6 et 2.7). Ce que vaut un point de stat en combat est
/// dans <see cref="Combat.CombatRules"/>.
/// </summary>
public sealed record ProgressionRules
{
    public required int MaxLevel { get; init; }

    /// <summary>Points de stats gagnés à chaque niveau après le premier.</summary>
    public required int StatPointsPerLevel { get; init; }

    /// <summary>Points de compétence donnés à la création du héros (niveau 1).</summary>
    public required int StartingSkillPoints { get; init; }

    /// <summary>Un point de compétence tous les combien de niveaux (2 : aux niveaux pairs).</summary>
    public required int LevelsPerSkillPoint { get; init; }

    /// <summary>Emplacements de compétences actives selon le niveau, par niveau croissant (le premier dès le niveau 1).</summary>
    public required IReadOnlyList<ActiveSlotsStep> ActiveSlots { get; init; }

    public required int BranchesPerTree { get; init; }

    /// <summary>Paliers d'une branche, du premier au dernier.</summary>
    public required IReadOnlyList<TierRules> Tiers { get; init; }

    public static ProgressionRules FromJson(string json)
    {
        var rules = DataJson.Deserialize<ProgressionRules>(json);
        rules.Validate();
        return rules;
    }

    /// <summary>Points de stats d'un héros de ce niveau.</summary>
    public int StatPointsAt(int level) => (level - 1) * StatPointsPerLevel;

    /// <summary>Points de compétence d'un héros de ce niveau.</summary>
    public int SkillPointsAt(int level) => StartingSkillPoints + level / LevelsPerSkillPoint;

    /// <summary>Compétences actives qu'un héros de ce niveau peut équiper (l'emplacement de passif est à part).</summary>
    public int ActiveSlotsAt(int level) => ActiveSlots.Last(s => s.FromLevel <= level).Count;

    public void Validate()
    {
        if (MaxLevel < 1 || StatPointsPerLevel < 0 || StartingSkillPoints < 0 || LevelsPerSkillPoint < 1 || BranchesPerTree < 1)
            throw new ArgumentException("Progression rules need positive levels, branches and skill point pace.");
        if (ActiveSlots is null || ActiveSlots.Count == 0 || ActiveSlots.Any(s => s is null) || ActiveSlots[0].FromLevel != 1)
            throw new ArgumentException("Active slots must be given from level 1.");
        for (var i = 0; i < ActiveSlots.Count; i++)
        {
            if (ActiveSlots[i].Count < 1 || (i > 0 && ActiveSlots[i].FromLevel <= ActiveSlots[i - 1].FromLevel))
                throw new ArgumentException("Active slots need a positive count and increasing levels.");
        }
        if (Tiers is null || Tiers.Count == 0 || Tiers.Any(t => t is null))
            throw new ArgumentException("A tree needs at least one tier.");
        foreach (var tier in Tiers)
            tier.Validate();
        if (Tiers[0].PointsInBranch != 0)
            throw new ArgumentException("The first tier of a branch must be open from the start.");
    }
}
