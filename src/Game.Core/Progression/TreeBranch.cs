namespace Game.Core.Progression;

// Forme d'un arbre telle qu'elle est écrite dans data/trees : une branche, ses paliers, et ce que chaque palier propose.

/// <summary>Une branche d'un arbre : une façon de jouer l'archétype (ex. Rempart pour le colosse).</summary>
public sealed record TreeBranch
{
    public required string Id { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    /// <summary>Paliers, du premier au dernier.</summary>
    public required IReadOnlyList<TreeTier> Tiers { get; init; }
}

/// <summary>Un palier d'une branche : ses compétences actives et ses passifs.</summary>
public sealed record TreeTier
{
    public required IReadOnlyList<TreeActive> Actives { get; init; }

    public IReadOnlyList<TreePassive> Passives { get; init; } = Array.Empty<TreePassive>();
}

/// <summary>
/// Une compétence active à acheter. Sa <see cref="Variant"/> (une autre version, qu'on équipe à sa place) vient avec
/// elle, sans coûter de point.
/// </summary>
public sealed record TreeActive
{
    /// <summary>Identifiant de la compétence (bibliothèque <c>data/skills</c>, ou liste des compétences à venir de l'arbre).</summary>
    public required string Skill { get; init; }

    public string? Variant { get; init; }
}

/// <summary>Un passif à acheter. Les passifs n'ont pas de bibliothèque : leur nom est écrit ici.</summary>
public sealed record TreePassive
{
    /// <summary>Identifiant : celui d'un <see cref="Combat.PassiveKind"/> en camelCase quand le passif est codé.</summary>
    public required string Id { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    /// <summary>Vrai tant que le passif n'est pas codé : on peut l'acheter, pas l'équiper.</summary>
    public bool Planned { get; init; }
}
