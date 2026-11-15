using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Obstacle qu'une compétence dresse sur chaque case libre de sa zone (données), pour un temps : un bloc de glace
/// (obstacle bas, avec ses PV de couverture) ou un pilier (un mur, qui bloque aussi la vue). Il disparaît de lui-même ;
/// ce n'est pas du décor du lieu.
/// </summary>
public sealed record RaisedObstacleDefinition
{
    public required Terrain Terrain { get; init; }

    /// <summary>PV d'un obstacle bas (ce qu'il peut absorber comme couverture). Sans objet pour un mur.</summary>
    public int Hp { get; init; }

    /// <summary>Durée en millisecondes de combat avant qu'il ne fonde.</summary>
    public required int DurationMs { get; init; }

    public void Validate(string skillId)
    {
        if (Terrain is not (Terrain.LowObstacle or Terrain.Wall))
            throw new ArgumentException($"Skill '{skillId}' raises a low obstacle or a wall.");
        if (Terrain == Terrain.LowObstacle ? Hp < 1 : Hp != 0)
            throw new ArgumentException($"Skill '{skillId}': a raised low obstacle needs HP, a raised wall has none.");
        if (DurationMs < 1)
            throw new ArgumentException($"Skill '{skillId}': a raised obstacle needs a duration.");
    }
}
