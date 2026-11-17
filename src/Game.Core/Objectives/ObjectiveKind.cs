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

    /// <summary>
    /// Course contre la montre : neutraliser tous les objets (<see cref="ObjectiveDefinition.Devices"/>) avant la fin du
    /// compte à rebours (<see cref="ObjectiveDefinition.DurationMs"/>) ; raté s'il arrive au bout.
    /// </summary>
    Countdown,

    /// <summary>
    /// Protection : les personnes protégées doivent rester debout jusqu'à la fin (<see cref="ObjectiveDefinition.Until"/>) :
    /// toutes les vagues vaincues, ou un temps tenu. Raté dès que l'une d'elles tombe.
    /// </summary>
    Protect,
}

/// <summary>Quand une protection est réussie.</summary>
public enum ProtectUntil
{
    /// <summary>Toutes les vagues sont arrivées et plus aucun ennemi n'est en lice.</summary>
    Waves,

    /// <summary>Le temps donné (<see cref="ObjectiveDefinition.DurationMs"/>) est tenu, quels que soient les ennemis restants.</summary>
    Time,
}
