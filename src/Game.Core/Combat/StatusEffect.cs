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

    /// <summary>
    /// Ne peut plus se déplacer d'elle-même (ni déplacement, ni mouvement spécial, ni sauvetage), mais peut encore
    /// agir sur place ; on peut toujours la déplacer de force.
    /// </summary>
    Immobilized,

    /// <summary>Ne peut plus utiliser que son attaque de base ; sa charge en cours est interrompue.</summary>
    Disarmed,

    /// <summary>Insensible aux états néfastes : aucun ne prend sur elle (Sérénité) ; ceux qu'elle avait déjà restent.</summary>
    Serene,
}

public static class StatusKindExtensions
{
    /// <summary>
    /// Un état nuisible (contrôle) se pose sur un ennemi ; un état bénéfique (renforcement), sur soi ou un allié.
    /// Tout nouvel état doit être classé ici (sinon exception).
    /// </summary>
    public static bool IsHarmful(this StatusKind status) => status switch
    {
        StatusKind.Stunned or StatusKind.OffBalance or StatusKind.Exposed or StatusKind.Confused or StatusKind.Dominated
            or StatusKind.Immobilized or StatusKind.Disarmed => true,
        StatusKind.Hasted or StatusKind.Empowered or StatusKind.Serene => false,
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

    /// <summary>
    /// États qu'une compétence pose à une sorte de cible en particulier (<paramref name="targets"/> : « robot »,
    /// « boss ») : non nuls, valides, néfastes (ils visent un adversaire), chacun une seule fois.
    /// </summary>
    internal static void ValidateAgainst(IReadOnlyList<StatusEffect>? effects, string skillId, string targets)
    {
        if (effects is null || effects.Any(e => e is null))
            throw new ArgumentException($"Skill '{skillId}' has a null {targets} effect.");
        foreach (var effect in effects)
            effect.Validate(skillId);
        if (effects.Any(e => !e.Status.IsHarmful()))
            throw new ArgumentException($"Skill '{skillId}': a {targets} effect is a harmful status.");
        if (effects.Select(e => e.Status).Distinct().Count() != effects.Count)
            throw new ArgumentException($"Skill '{skillId}' applies the same status twice to a {targets}.");
    }
}
