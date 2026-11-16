namespace Game.Core.Combat;

/// <summary>
/// Leurre (données d'une unité invoquée) : quand il apparaît, les ennemis commandés par l'IA à <see cref="Range"/> cases
/// au plus le visent pour leurs <see cref="Actions"/> prochaines activations (une provocation).
/// </summary>
public sealed record LureDefinition
{
    public required int Range { get; init; }
    public required int Actions { get; init; }

    public void Validate(string unitId)
    {
        if (Range < 1 || Actions is < 1 or > 10)
            throw new ArgumentException($"Unit '{unitId}': a lure needs a range of at least 1 and 1 to 10 actions.");
    }
}
