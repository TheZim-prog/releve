using Game.Core.Grids;

namespace Game.Core.Objectives;

/// <summary>
/// Objet à neutraliser d'une course contre la montre (bombe, portail…), posé sur la case (<see cref="X"/>, <see cref="Y"/>) :
/// il faut <see cref="Interactions"/> interactions au contact, d'un ou plusieurs héros.
/// </summary>
public sealed record DeviceDefinition
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Interactions { get; init; }

    public Cell Cell => new(X, Y);

    public void Validate()
    {
        if (Interactions < 1)
            throw new ArgumentException($"The device on {Cell} needs at least one interaction.");
    }
}
