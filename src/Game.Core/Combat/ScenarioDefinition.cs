using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Une unité placée par un scénario : son type (catalogue des unités), son équipe et sa case.</summary>
public sealed record ScenarioUnit
{
    /// <summary>Identifiant du type d'unité (héros, ennemi, civil).</summary>
    public required string Unit { get; init; }

    public required int Team { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>Points de stats de ce héros (facultatif : aucun par défaut).</summary>
    public StatPoints? Points { get; init; }
}

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
            if (string.IsNullOrWhiteSpace(unit.Unit) || unit.Team < 0)
                throw new ArgumentException($"Scenario '{Id}' has a unit without type or with a negative team.");
            if (!grid.IsWalkable(cell))
                throw new ArgumentException($"Scenario '{Id}': unit '{unit.Unit}' stands on a blocked cell {cell}.");
            if (!taken.Add(cell))
                throw new ArgumentException($"Scenario '{Id}': two units on {cell}.");
            try
            {
                unit.Points?.Validate();
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"Scenario '{Id}': unit '{unit.Unit}': {e.Message}", e);
            }
        }
    }

    /// <summary>
    /// État initial du combat : la carte, et les unités tirées du <paramref name="catalog"/> (identifiants 1, 2, 3…
    /// dans l'ordre du scénario, jauges vides). Refuse une unité inconnue, ou dont les points de stats portent
    /// l'initiative au-delà de la capacité de la jauge.
    /// </summary>
    public CombatSetup ToSetup(CombatRules rules, IReadOnlyDictionary<string, UnitDefinition> catalog)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(catalog);
        var units = Units
            .Select((u, i) => catalog.TryGetValue(u.Unit, out var definition)
                ? definition.ToSetup(new UnitId(i + 1), u.Team, new Cell(u.X, u.Y), points: u.Points)
                : throw new ArgumentException($"Scenario '{Id}': unknown unit '{u.Unit}'."))
            .ToList();
        foreach (var unit in units)
        {
            if (rules.InitiativeWith(unit.Definition.Initiative, unit.Points ?? StatPoints.None) > rules.GaugeCapacity)
                throw new ArgumentException(
                    $"Scenario '{Id}': unit '{unit.Definition.Id}' has an initiative above the gauge capacity ({rules.GaugeCapacity}) with its stat points.");
        }
        return new CombatSetup(rules, Grid.FromRows(Map.ToArray()), units, Seed);
    }
}
