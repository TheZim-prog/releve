namespace Game.Core.Combat;

/// <summary>
/// Points de stats d'un héros (docs/design.md 2.6) : un par niveau, mis dans les PV, la puissance ou l'initiative.
/// Chaque point ajoute un pourcentage de la stat de base de son type d'unité (<see cref="CombatRules"/>). La mobilité
/// ne s'achète pas.
/// </summary>
public sealed record StatPoints
{
    public static readonly StatPoints None = new();

    /// <summary>Borne de chaque stat : très au-dessus d'un héros de niveau 20 (19 points), elle garde les calculs loin de tout débordement.</summary>
    public const int Max = 1000;

    public int Hp { get; init; }

    /// <summary>Puissance : multiplie les dégâts et les soins de toutes ses compétences.</summary>
    public int Power { get; init; }

    public int Initiative { get; init; }

    public void Validate()
    {
        if (Hp is < 0 or > Max || Power is < 0 or > Max || Initiative is < 0 or > Max)
            throw new ArgumentException($"Stat points must be between 0 and {Max}.");
    }
}
