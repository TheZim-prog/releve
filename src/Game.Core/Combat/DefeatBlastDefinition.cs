namespace Game.Core.Combat;

/// <summary>
/// Explosion d'une unité quand elle est détruite (données, ex. Leurre piégé) : ses adversaires debout à son contact
/// prennent ces dégâts, multipliés par sa puissance (celle de son lanceur, pour un gadget).
/// </summary>
public sealed record DefeatBlastDefinition
{
    public required int MinDamage { get; init; }
    public required int MaxDamage { get; init; }

    public void Validate(string unitId)
    {
        if (MinDamage < 0 || MaxDamage < MinDamage || MaxDamage == 0)
            throw new ArgumentException($"Unit '{unitId}': a defeat blast needs 0 <= minDamage <= maxDamage, and some damage.");
    }
}
