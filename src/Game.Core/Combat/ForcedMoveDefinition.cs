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

    /// <summary>
    /// Tourniquet : comme un renversement, mais dans la direction choisie (une case visée, la direction la plus proche
    /// parmi les 8 depuis le lanceur) ; la distance se compte depuis la case du lanceur.
    /// </summary>
    Spin,

    /// <summary>
    /// Vortex : chaque unité touchée par la zone est attirée vers son centre, pas à pas (la direction la plus proche parmi
    /// les 8, recalculée à chaque pas), sur <see cref="ForcedMoveDefinition.Distance"/> cases au plus ; elle s'arrête sur
    /// le centre, ou avant une unité, un mur ou un coin bouché, sans collision.
    /// </summary>
    Pull,
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

    /// <summary>Projection, renversement ou tourniquet : la cible part en ligne droite et peut percuter quelque chose.</summary>
    public bool IsLinear => Type is ForcedMoveType.Push or ForcedMoveType.Throw or ForcedMoveType.Spin;

    /// <summary>
    /// Ce déplacement peut aller avec une zone : une projection ou un tourniquet, autour du lanceur
    /// (<see cref="ZoneAroundCaster"/>) ; un vortex, où qu'elle soit.
    /// </summary>
    public bool GoesWithZone => Type is ForcedMoveType.Push or ForcedMoveType.Spin or ForcedMoveType.Pull;

    /// <summary>Avec une zone, elle est centrée sur le lanceur (portée 0) : une projection, un tourniquet.</summary>
    public bool ZoneAroundCaster => Type is ForcedMoveType.Push or ForcedMoveType.Spin;

    /// <summary>Ce déplacement n'existe qu'avec une zone : un tourniquet (qui frappe autour), un vortex (vers son centre).</summary>
    public bool NeedsZone => Type is ForcedMoveType.Spin or ForcedMoveType.Pull;

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Type))
            throw new ArgumentException($"Skill '{skillId}' has an unknown forced move type.");
        if (Type == ForcedMoveType.Swap ? Distance != 0 : Distance < 1)
            throw new ArgumentException($"Skill '{skillId}' needs a forced move distance of at least 1 (none for a swap).");
        if (CollisionMinDamage < 0 || CollisionMaxDamage < CollisionMinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= collisionMinDamage <= collisionMaxDamage.");
        if (!IsLinear && CollisionMaxDamage > 0)
            throw new ArgumentException($"Skill '{skillId}': only a push, a throw or a spin deals collision damage.");
        if (ImpactMinDamage < 0 || ImpactMaxDamage < ImpactMinDamage)
            throw new ArgumentException($"Skill '{skillId}' needs 0 <= impactMinDamage <= impactMaxDamage.");
        if (Type != ForcedMoveType.Place && ImpactMaxDamage > 0)
            throw new ArgumentException($"Skill '{skillId}': only a placement deals impact damage.");
    }
}
