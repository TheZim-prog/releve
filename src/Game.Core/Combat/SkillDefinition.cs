namespace Game.Core.Combat;

/// <summary>Poids d'une action : détermine d'où repart la jauge (voir <see cref="CombatRules"/>).</summary>
public enum SkillWeight
{
    Light,
    Heavy,
}

/// <summary>
/// Compétence (attaque de base comprise), décrite en données.
/// Pour l'instant : une attaque ciblée sur une unité ennemie, au corps à corps.
/// </summary>
public sealed record SkillDefinition
{
    public required string Id { get; init; }
    public required SkillWeight Weight { get; init; }

    /// <summary>Portée en cases (diagonale = 1). Seule la portée 1 est gérée tant que la ligne de vue n'existe pas.</summary>
    public required int Range { get; init; }

    public required int MinDamage { get; init; }
    public required int MaxDamage { get; init; }

    /// <summary>Temps de recharge, en millisecondes de combat (0 = aucun).</summary>
    public required int CooldownMs { get; init; }

    public static IReadOnlyList<SkillDefinition> ListFromJson(string json)
    {
        var skills = DataJson.Deserialize<List<SkillDefinition?>>(json);
        ValidateAll(skills);
        return skills!;
    }

    /// <summary>Valide chaque compétence de la liste et l'unicité des identifiants.</summary>
    public static void ValidateAll(IEnumerable<SkillDefinition?> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var ids = new HashSet<string>();
        foreach (var skill in skills)
        {
            if (skill is null)
                throw new ArgumentException("Skill list contains a null entry.");
            skill.Validate();
            if (!ids.Add(skill.Id))
                throw new ArgumentException($"Duplicate skill id '{skill.Id}'.");
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Skill id is required.");
        if (!Enum.IsDefined(Weight))
            throw new ArgumentException($"Skill '{Id}' has an unknown weight.");
        if (Range != 1)
            throw new ArgumentException($"Skill '{Id}': only range 1 is supported until line of sight exists.");
        if (MinDamage < 0 || MaxDamage < MinDamage)
            throw new ArgumentException($"Skill '{Id}' needs 0 <= minDamage <= maxDamage.");
        if (CooldownMs < 0)
            throw new ArgumentException($"Skill '{Id}' has a negative cooldown.");
    }
}
