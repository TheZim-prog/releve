namespace Game.Core.Grids;

/// <summary>Ce qui occupe une case, du point de vue de l'unité qui se déplace.</summary>
public enum Occupant
{
    None = 0,

    /// <summary>Traversable, mais on ne peut pas s'y arrêter.</summary>
    Ally,

    /// <summary>Infranchissable, et bloque les diagonales qui frôlent sa case, comme un mur.</summary>
    Enemy,
}
