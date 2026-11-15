namespace Game.Core.Combat;

public enum ForcedMoveType
{
    /// <summary>
    /// Projection : la cible est poussée en ligne droite (direction lanceur → cible, ou centre de la zone → cible) sur
    /// <see cref="ForcedMoveDefinition.Distance"/> cases ; elle s'arrête avant un mur, le bord ou une unité, et la
    /// collision fait des dégâts.
    /// </summary>
    Push,

    /// <summary>
    /// Télékinésie, lancer : la cible est posée sur une case libre choisie, à <see cref="ForcedMoveDefinition.Distance"/>
    /// cases au plus d'elle et visible depuis elle.
    /// </summary>
    Place,

    /// <summary>
    /// Renversement : comme une projection, mais la cible (au contact) est jetée derrière le lanceur ; la distance se
    /// compte depuis la case du lanceur.
    /// </summary>
    Throw,

    /// <summary>Permutation : le lanceur et sa cible échangent leurs cases.</summary>
    Swap,
}

/// <summary>Déplacement forcé appliqué à la cible d'une compétence (données). Pas d'attaque d'opportunité.</summary>
public sealed record ForcedMoveDefinition
{
    public required ForcedMoveType Type { get; init; }

    /// <summary>Distance en cases (au moins 1), sauf pour une permutation, qui n'en a pas.</summary>
    public int Distance { get; init; }

    /// <summary>Dégâts de collision d'une projection (0 = aucun), pour la cible et l'unité percutée.</summary>
    public int CollisionMinDamage { get; init; }

    public int CollisionMaxDamage { get; init; }

    /// <summary>
    /// Dégâts à l'atterrissage d'une unité posée (0 = aucun), pour les adversaires du lanceur au contact de la case
    /// d'arrivée (ex. Boulet).
    /// </summary>
    public int ImpactMinDamage { get; init; }

    public int ImpactMaxDamage { get; init; }

    /// <summary>Projection ou renversement : la cible part en ligne droite et peut percuter quelque chose.</summary>
    public bool IsLinear => Type is ForcedMoveType.Push or ForcedMoveType.Throw;

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Type))
            throw new ArgumentException($"Skill '{skillId}' has an unknown forced move type.");
        if (Type == ForcedMoveType.Swap ? Distance != 0 : Distance < 1)
            throw new ArgumentException($"Skill '{skillId}' needs a forced move distance of at least 1 (none for a swap).");
        if (CollisionMinDamage < 0 || CollisionMaxDamage < CollisionMinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= collisionMinDamage <= collisionMaxDamage.");
        if (!IsLinear && CollisionMaxDamage > 0)
            throw new ArgumentException($"Skill '{skillId}': only a push or a throw deals collision damage.");
        if (ImpactMinDamage < 0 || ImpactMaxDamage < ImpactMinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= impactMinDamage <= impactMaxDamage.");
        if (Type != ForcedMoveType.Place && ImpactMaxDamage > 0)
            throw new ArgumentException($"Skill '{skillId}': only a placement deals impact damage.");
    }
}
