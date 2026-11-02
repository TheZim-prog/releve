using Game.Core.Grids;

namespace Game.Core.Combat;

public readonly record struct UnitId(int Value)
{
    public override string ToString() => $"#{Value}";
}

/// <summary>Description d'une unité au début du combat.</summary>
/// <param name="Team">Les unités d'une même équipe sont alliées, les autres ennemies.</param>
/// <param name="Initiative">Points de jauge gagnés par tick.</param>
/// <param name="Mobility">Nombre maximal de cases par déplacement.</param>
/// <param name="InitialGauge">Jauge au début du combat, entre 0 et la capacité.</param>
public sealed record UnitSetup(UnitId Id, int Team, Cell Position, int Initiative, int Mobility, int InitialGauge = 0);

/// <summary>État courant d'une unité. Seule la <see cref="CombatSimulation"/> le modifie.</summary>
public sealed class Unit
{
    private readonly int _gaugeCapacity;

    internal Unit(UnitSetup setup, int gaugeCapacity)
    {
        Id = setup.Id;
        Team = setup.Team;
        Position = setup.Position;
        Initiative = setup.Initiative;
        Mobility = setup.Mobility;
        Gauge = setup.InitialGauge;
        _gaugeCapacity = gaugeCapacity;
    }

    public UnitId Id { get; }
    public int Team { get; }
    public Cell Position { get; internal set; }
    public int Initiative { get; }
    public int Mobility { get; }
    public int Gauge { get; internal set; }

    /// <summary>Jauge pleine : l'unité peut agir, et le reste tant qu'elle n'a pas agi.</summary>
    public bool IsReady => Gauge >= _gaugeCapacity;
}
