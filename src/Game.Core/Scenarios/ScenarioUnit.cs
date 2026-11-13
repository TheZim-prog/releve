namespace Game.Core.Scenarios;

/// <summary>
/// Une unité placée par un scénario, sur une case et dans une équipe : soit un type d'unité tel quel
/// (<see cref="Unit"/> : ennemi, civil…), soit un héros prêt à jouer (<see cref="Hero"/> : sa fiche donne son niveau,
/// ses points de stats et son équipement). Exactement l'un des deux.
/// </summary>
public sealed record ScenarioUnit
{
    /// <summary>Identifiant du type d'unité (data/units).</summary>
    public string? Unit { get; init; }

    /// <summary>Identifiant du héros (data/heroes).</summary>
    public string? Hero { get; init; }

    public required int Team { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>Ce qui est placé, pour les messages d'erreur.</summary>
    internal string Label => Hero is not null ? $"hero '{Hero}'" : $"unit '{Unit}'";
}
