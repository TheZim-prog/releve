using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// État initial d'un combat. Avec le journal des commandes, il suffit à rejouer le combat à l'identique.
/// La liste des unités est copiée : la modifier ensuite côté appelant ne change pas ce setup.
/// </summary>
/// <param name="Seed">Graine du générateur aléatoire du combat.</param>
public sealed record CombatSetup(CombatRules Rules, Grid Grid, IReadOnlyList<UnitSetup> Units, ulong Seed)
{
    private readonly IReadOnlyList<UnitSetup> _units = ReadOnlyCopy(Units);

    public IReadOnlyList<UnitSetup> Units
    {
        get => _units;
        init => _units = ReadOnlyCopy(value);
    }

    internal static IReadOnlyList<T> ReadOnlyCopy<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Array.AsReadOnly(items.ToArray());
    }
}
