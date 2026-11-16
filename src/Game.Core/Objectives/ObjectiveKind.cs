namespace Game.Core.Objectives;

/// <summary>Types d'objectifs de mission (docs/design.md 3.3).</summary>
public enum ObjectiveKind
{
    /// <summary>Élimination : plus aucun ennemi debout (vaincus, enfuis ou en fuite).</summary>
    Eliminate,

    /// <summary>
    /// Sauvetage : évacuer au moins <see cref="ObjectiveDefinition.Minimum"/> civils. Raté dès que ce n'est plus possible
    /// (trop de civils perdus).
    /// </summary>
    Rescue,

    /// <summary>
    /// Poursuite : le vilain poursuivi court dès le début vers les sorties (<see cref="ObjectiveDefinition.Exits"/>).
    /// Réussi s'il est vaincu, mis à terre ou rendu ; raté s'il atteint une sortie.
    /// </summary>
    Pursuit,
}
