namespace Game.Core.Combat;

/// <summary>États (docs/design.md, section 3).</summary>
public enum StatusKind
{
    /// <summary>Ne peut pas agir, jauge gelée à sa valeur.</summary>
    Stunned,

    /// <summary>Jauge ralentie (pourcentage dans <see cref="CombatRules.OffBalanceFillPercent"/>).</summary>
    OffBalance,

    /// <summary>Jauge accélérée (Hypervitesse ; pourcentage dans <see cref="CombatRules.HastedFillPercent"/>).</summary>
    Hasted,

    /// <summary>Dégâts infligés augmentés (Forme d'énergie pure ; <see cref="CombatRules.EmpoweredDamagePercent"/>).</summary>
    Empowered,

    /// <summary>Dégâts subis augmentés, de toutes les sources (Point faible ; <see cref="CombatRules.ExposedDamageTakenPercent"/>).</summary>
    Exposed,

    /// <summary>Agit seule et attaque l'unité debout la plus proche, quel que soit son camp (Confusion).</summary>
    Confused,

    /// <summary>Passe dans le camp de celui qui l'a dominée, et est commandée comme lui (Domination).</summary>
    Dominated,
}

public static class StatusKindExtensions
{
    /// <summary>
    /// Un état nuisible (contrôle) se pose sur un ennemi ; un état bénéfique (renforcement), sur soi ou un allié.
    /// Tout nouvel état doit être classé ici (sinon exception).
    /// </summary>
    public static bool IsHarmful(this StatusKind status) => status switch
    {
        StatusKind.Stunned or StatusKind.OffBalance or StatusKind.Exposed or StatusKind.Confused or StatusKind.Dominated => true,
        StatusKind.Hasted or StatusKind.Empowered => false,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status not classified as harmful or beneficial."),
    };
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
