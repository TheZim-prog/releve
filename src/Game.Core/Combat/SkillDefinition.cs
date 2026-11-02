namespace Game.Core.Combat;

/// <summary>Poids d'une action : détermine d'où repart la jauge (voir <see cref="CombatRules"/>).</summary>
public enum SkillWeight
{
    Light,
    Heavy,
}

/// <summary>
/// Compétence (attaque de base comprise), décrite en données.
/// Pour l'instant : une attaque ciblée sur une unité ennemie, au contact ou à distance (ligne de vue).
/// </summary>
public sealed record SkillDefinition
{
    public required string Id { get; init; }
    public required SkillWeight Weight { get; init; }

    /// <summary>Portée en cases (diagonale = 1). Au-delà de 1, il faut une ligne de vue.</summary>
    public required int Range { get; init; }

    public required int MinDamage { get; init; }
    public required int MaxDamage { get; init; }

    /// <summary>Temps de recharge, en millisecondes de combat (0 = aucun).</summary>
    public required int CooldownMs { get; init; }

    private readonly IReadOnlyList<StatusEffect> _effects = Array.Empty<StatusEffect>();

    /// <summary>États appliqués à la cible si elle est encore debout après les dégâts (facultatif). Liste copiée.</summary>
    public IReadOnlyList<StatusEffect> Effects
    {
        get => _effects;
        init => _effects = value is null ? null! : CombatSetup.ReadOnlyCopy(value);
    }

    /// <summary>Une compétence à 0 dégât maximum n'en inflige pas (contrôle pur) : aucun tirage, aucun événement.</summary>
    public bool DealsDamage => MaxDamage > 0;

    /// <summary>
    /// Durée de charge en millisecondes de combat (0 = instantanée). Pendant la charge, le lanceur est immobilisé.
    /// </summary>
    public int ChargeMs { get; init; }

    /// <summary>Zone d'effet (facultative). Avec une zone, la compétence vise une case ; sinon, une unité ennemie.</summary>
    public ZoneDefinition? Zone { get; init; }

    public bool IsCharged => ChargeMs > 0;

    public static IReadOnlyList<SkillDefinition> ListFromJson(string json) =>
        ValidateAll(DataJson.Deserialize<List<SkillDefinition?>>(json));

    /// <summary>Valide chaque compétence de la liste et l'unicité des identifiants.</summary>
    public static IReadOnlyList<SkillDefinition> ValidateAll(IEnumerable<SkillDefinition?> skills) =>
        DataJson.ValidateList(skills, s => s.Validate(), s => s.Id, "skill");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Skill id is required.");
        if (!Enum.IsDefined(Weight))
            throw new ArgumentException($"Skill '{Id}' has an unknown weight.");
        // Un carré peut être centré sur le lanceur (portée 0) ; une ligne part vers une case voisine au moins ;
        // une attaque ciblée vise une autre unité.
        if (Range < (Zone?.Shape == ZoneShape.Square ? 0 : 1))
            throw new ArgumentException($"Skill '{Id}' has an invalid range {Range}.");
        if (ChargeMs < 0)
            throw new ArgumentException($"Skill '{Id}' has a negative charge time.");
        Zone?.Validate(Id);
        if (MinDamage < 0 || MaxDamage < MinDamage)
            throw new ArgumentException($"Skill '{Id}' needs 0 <= minDamage <= maxDamage.");
        if (CooldownMs < 0)
            throw new ArgumentException($"Skill '{Id}' has a negative cooldown.");
        if (Effects is null || Effects.Any(e => e is null))
            throw new ArgumentException($"Skill '{Id}' has a null effect.");
        foreach (var effect in Effects)
            effect.Validate(Id);
        if (Effects.Select(e => e.Status).Distinct().Count() != Effects.Count)
            throw new ArgumentException($"Skill '{Id}' applies the same status twice.");
        if (!DealsDamage && Effects.Count == 0)
            throw new ArgumentException($"Skill '{Id}' deals no damage and applies no status.");
    }
}
