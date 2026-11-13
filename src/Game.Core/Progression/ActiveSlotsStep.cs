namespace Game.Core.Progression;

/// <summary>Nombre d'emplacements de compétences actives à partir d'un niveau.</summary>
public sealed record ActiveSlotsStep
{
    public required int FromLevel { get; init; }
    public required int Count { get; init; }
}
