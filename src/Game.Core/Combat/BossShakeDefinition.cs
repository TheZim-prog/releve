namespace Game.Core.Combat;

/// <summary>
/// Ce que fait une domination à un boss, qu'elle ne peut pas dominer (données ; ex. Domination : elle échoue mais le
/// secoue) : des dégâts et des états néfastes, à la place de ses propres effets.
/// </summary>
public sealed record BossShakeDefinition
{
    public required int MinDamage { get; init; }
    public required int MaxDamage { get; init; }

    public bool DealsDamage => MaxDamage > 0;

    private readonly IReadOnlyList<StatusEffect> _effects = Array.Empty<StatusEffect>();

    /// <summary>États posés sur le boss (facultatif) ; jamais une domination. Liste copiée.</summary>
    public IReadOnlyList<StatusEffect> Effects
    {
        get => _effects;
        init => _effects = value is null ? null! : CombatSetup.ReadOnlyCopy(value);
    }

    public void Validate(string skillId)
    {
        if (MinDamage < 0 || MaxDamage < MinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= boss minDamage <= boss maxDamage.");
        StatusEffect.ValidateAgainst(Effects, skillId, "boss");
        if (Effects.Any(e => e.Status == StatusKind.Dominated))
            throw new ArgumentException($"Skill '{skillId}': a boss is never dominated.");
        if (!DealsDamage && Effects.Count == 0)
            throw new ArgumentException($"Skill '{skillId}': its boss effect does nothing (no damage, no status).");
    }
}
