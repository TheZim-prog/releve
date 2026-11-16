namespace Game.Core.Combat;

/// <summary>
/// Surveillance (données) : au lieu d'agir tout de suite, la compétence surveille ses cases (ex. Tir de couverture) ou un
/// allié (ex. Protection rapprochée). Le premier ennemi qui bouge ou agit dans la zone, ou qui attaque l'allié, prend un
/// tir, s'il est à <see cref="ShotRange"/> cases au plus du lanceur et en vue. Elle tombe quand elle a tiré, ou à la
/// prochaine action du lanceur.
/// </summary>
public sealed record WatchDefinition
{
    /// <summary>Portée du tir de réaction, depuis le lanceur.</summary>
    public required int ShotRange { get; init; }

    public required int MinDamage { get; init; }
    public required int MaxDamage { get; init; }

    private readonly IReadOnlyList<StatusEffect> _effects = Array.Empty<StatusEffect>();

    /// <summary>États (nuisibles) posés par le tir. Liste copiée.</summary>
    public IReadOnlyList<StatusEffect> Effects
    {
        get => _effects;
        init => _effects = value is null ? null! : CombatSetup.ReadOnlyCopy(value);
    }

    /// <summary>Le tir de réaction, comme une attaque ciblée ordinaire (même identifiant que la compétence qui surveille).</summary>
    public SkillDefinition ShotOf(SkillDefinition watching) => new()
    {
        Id = watching.Id, Name = watching.Name, Weight = watching.Weight, Range = ShotRange,
        MinDamage = MinDamage, MaxDamage = MaxDamage, CooldownMs = 0, Effects = Effects,
    };

    public void Validate(string skillId)
    {
        if (ShotRange < 1)
            throw new ArgumentException($"Skill '{skillId}': a watch shot needs a range of at least 1.");
        if (Effects is null || Effects.Any(e => e is null || !e.Status.IsHarmful()))
            throw new ArgumentException($"Skill '{skillId}': a watch shot only applies harmful statuses.");
        if (MinDamage < 0 || MaxDamage < MinDamage || (MaxDamage == 0 && Effects.Count == 0))
            throw new ArgumentException($"Skill '{skillId}': a watch shot needs 0 <= minDamage <= maxDamage, and damage or a status.");
        foreach (var effect in Effects)
            effect.Validate(skillId);
    }
}
