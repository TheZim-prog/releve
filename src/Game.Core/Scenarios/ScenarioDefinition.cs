using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Progression;

namespace Game.Core.Scenarios;

/// <summary>
/// Combat décrit en données (data/scenarios) : la carte (une ligne de texte par rangée, voir <see cref="Grid.FromRows"/>),
/// les unités placées et la graine. <see cref="ToSetup"/> en fait l'état initial d'un combat.
/// </summary>
public sealed record ScenarioDefinition
{
    public required string Id { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    public required ulong Seed { get; init; }
    public required IReadOnlyList<string> Map { get; init; }
    public required IReadOnlyList<ScenarioUnit> Units { get; init; }

    public static ScenarioDefinition FromJson(string json)
    {
        var scenario = DataJson.Deserialize<ScenarioDefinition>(json);
        scenario.Validate();
        return scenario;
    }

    /// <summary>Vérifie le scénario seul : carte lisible, unités sur des cases libres et distinctes.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("A scenario needs an id and a name.");
        if (Map is null || Units is null || Units.Any(u => u is null))
            throw new ArgumentException($"Scenario '{Id}' needs a map and a unit list without null entries.");
        var grid = Grid.FromRows(Map.ToArray());
        var taken = new HashSet<Cell>();
        foreach (var unit in Units)
        {
            var cell = new Cell(unit.X, unit.Y);
            if ((unit.Unit is null) == (unit.Hero is null) || string.IsNullOrWhiteSpace(unit.Unit ?? unit.Hero) || unit.Team < 0)
                throw new ArgumentException($"Scenario '{Id}': each entry places either a unit or a hero, in a team that is not negative.");
            if (!grid.IsWalkable(cell))
                throw new ArgumentException($"Scenario '{Id}': {unit.Label} stands on a blocked cell {cell}.");
            if (!taken.Add(cell))
                throw new ArgumentException($"Scenario '{Id}': two units on {cell}.");
        }
    }

    /// <summary>
    /// État initial du combat : la carte, et les unités (identifiants 1, 2, 3… dans l'ordre du scénario, jauges vides),
    /// tirées du catalogue <paramref name="units"/> ou, pour les héros, de <paramref name="heroes"/>. Refuse une unité
    /// ou un héros inconnu, ou dont les points de stats portent l'initiative au-delà de la capacité de la jauge.
    /// </summary>
    public CombatSetup ToSetup(CombatRules rules, IReadOnlyDictionary<string, UnitDefinition> units, HeroCatalog? heroes = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(units);
        // Les archétypes des héros et les unités placées telles quelles doivent venir du même catalogue.
        if (heroes is not null && !ReferenceEquals(heroes.Units, units))
            throw new ArgumentException($"Scenario '{Id}': the heroes were built from another unit catalog.");
        try
        {
            var placed = Units.Select((unit, index) => Place(unit, new UnitId(index + 1), units, heroes)).ToList();
            foreach (var unit in placed)
            {
                if (rules.InitiativeWith(unit.Definition.Initiative, unit.Points ?? StatPoints.None) > rules.GaugeCapacity)
                    throw new ArgumentException(
                        $"'{unit.Definition.Id}' has an initiative above the gauge capacity ({rules.GaugeCapacity}) with its stat points.");
            }
            return new CombatSetup(rules, Grid.FromRows(Map.ToArray()), placed, Seed);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"Scenario '{Id}': {e.Message}", e);
        }
    }

    private static UnitSetup Place(ScenarioUnit unit, UnitId id, IReadOnlyDictionary<string, UnitDefinition> units, HeroCatalog? heroes)
    {
        var cell = new Cell(unit.X, unit.Y);
        if (unit.Hero is { } heroId)
        {
            var hero = (heroes ?? throw new ArgumentException($"{unit.Label}: this content has no heroes.")).Unit(heroId);
            return hero.Definition.ToSetup(id, unit.Team, cell, points: hero.Points);
        }
        return units.TryGetValue(unit.Unit!, out var definition)
            ? definition.ToSetup(id, unit.Team, cell)
            : throw new ArgumentException($"unknown {unit.Label}.");
    }
}
