namespace Game.Core.Combat;

public enum ForcedMoveType
{
    /// <summary>
    /// Projection : la cible est poussée en ligne droite (direction lanceur → cible) sur <see cref="ForcedMoveDefinition.Distance"/>
    /// cases ; elle s'arrête avant un mur, le bord ou une unité, et la collision fait des dégâts.
    /// </summary>
    Push,

    /// <summary>
    /// Télékinésie : la cible est posée sur une case libre choisie, à <see cref="ForcedMoveDefinition.Distance"/> cases au plus
    /// d'elle et visible depuis elle.
    /// </summary>
    Place,
}

/// <summary>Déplacement forcé appliqué à la cible d'une compétence (données). Pas d'attaque d'opportunité.</summary>
public sealed record ForcedMoveDefinition
{
    public required ForcedMoveType Type { get; init; }
    public required int Distance { get; init; }

    /// <summary>Dégâts de collision d'une projection (0 = aucun), pour la cible et l'unité percutée.</summary>
    public int CollisionMinDamage { get; init; }

    public int CollisionMaxDamage { get; init; }

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Type))
            throw new ArgumentException($"Skill '{skillId}' has an unknown forced move type.");
        if (Distance < 1)
            throw new ArgumentException($"Skill '{skillId}' needs a forced move distance of at least 1.");
        if (CollisionMinDamage < 0 || CollisionMaxDamage < CollisionMinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= collisionMinDamage <= collisionMaxDamage.");
        if (Type == ForcedMoveType.Place && CollisionMaxDamage > 0)
            throw new ArgumentException($"Skill '{skillId}': only a push deals collision damage.");
    }
}
