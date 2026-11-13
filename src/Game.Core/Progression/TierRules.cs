namespace Game.Core.Progression;

/// <summary>Forme d'un palier de branche, la même dans tous les arbres (docs/design.md 2.7).</summary>
public sealed record TierRules
{
    /// <summary>Points déjà dépensés dans la branche (dans les paliers précédents) pour accéder à ce palier.</summary>
    public required int PointsInBranch { get; init; }

    /// <summary>Nombre de compétences actives du palier.</summary>
    public required int Actives { get; init; }

    /// <summary>Nombre de passifs du palier.</summary>
    public required int Passives { get; init; }

    /// <summary>Vrai : chaque compétence active du palier a une variante ; faux : aucune n'en a.</summary>
    public required bool Variants { get; init; }

    /// <summary>Vrai : les compétences actives du palier sont des actions lourdes.</summary>
    public required bool RequiresHeavy { get; init; }

    public void Validate()
    {
        if (PointsInBranch < 0 || Actives < 0 || Passives < 0 || Actives + Passives == 0)
            throw new ArgumentException("A tree tier needs at least one skill or passive, and no negative value.");
    }
}
