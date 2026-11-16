namespace Game.Core.Combat;

/// <summary>
/// Compétence qui prend un obstacle bas du décor (voiture, muret, bloc de glace) : impossible sans obstacle à portée.
/// L'obstacle pris est choisi tout seul : celui à portée qui est le plus près de la case visée (à égalité, le plus près
/// du lanceur, puis l'ordre des cases).
/// </summary>
public sealed record SceneryDefinition
{
    public required SceneryUse Use { get; init; }

    /// <summary>
    /// Jusqu'où le lanceur peut prendre l'obstacle : 1 = à son contact (sans coupe d'angle, comme le corps à corps) ;
    /// au-delà, à cette distance et en vue (ex. le télépathe).
    /// </summary>
    public int Reach { get; init; } = 1;

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Use))
            throw new ArgumentException($"Skill '{skillId}' has an unknown scenery use.");
        if (Reach < 1)
            throw new ArgumentException($"Skill '{skillId}' needs a scenery reach of at least 1.");
    }
}
