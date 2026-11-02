namespace Game.Core.Combat;

/// <summary>États (docs/design.md, section 3).</summary>
public enum StatusKind
{
    /// <summary>Ne peut pas agir, jauge gelée à sa valeur.</summary>
    Stunned,

    /// <summary>Jauge ralentie (pourcentage dans <see cref="CombatRules.OffBalanceFillPercent"/>).</summary>
    OffBalance,
}

/// <summary>État appliqué par une compétence à sa cible, pour une durée en millisecondes de combat.</summary>
public sealed record StatusEffect
{
    public required StatusKind Status { get; init; }
    public required int DurationMs { get; init; }

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Status))
            throw new ArgumentException($"Skill '{skillId}' has an unknown status.");
        if (DurationMs <= 0)
            throw new ArgumentException($"Skill '{skillId}': status {Status} needs a positive duration.");
    }
}
