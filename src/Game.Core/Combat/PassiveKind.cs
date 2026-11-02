namespace Game.Core.Combat;

/// <summary>
/// Passifs équipés (docs/design.md, section 2.3 et kits de la section 3). Ceux qui ne sont qu'une valeur
/// (plus d'attaques d'opportunité pour le colosse) passent par les stats ; ceux-ci changent une règle.
/// </summary>
public enum PassiveKind
{
    /// <summary>L'unité ne subit pas d'attaques d'opportunité (speedster).</summary>
    IgnoresOpportunityAttacks,
}
