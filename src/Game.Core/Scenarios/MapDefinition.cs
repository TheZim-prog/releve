using Game.Core.Grids;

namespace Game.Core.Scenarios;

/// <summary>
/// Une carte partagée entre plusieurs scénarios (data/maps) : une ligne de texte par rangée (voir
/// <see cref="Grid.FromRows"/>). Un scénario la désigne par son identifiant (<see cref="ScenarioDefinition.MapId"/>).
/// </summary>
public sealed record MapDefinition
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Rows { get; init; }

    public static MapDefinition FromJson(string json)
    {
        var map = DataJson.Deserialize<MapDefinition>(json);
        if (string.IsNullOrWhiteSpace(map.Id) || map.Rows is null)
            throw new ArgumentException("A map needs an id and rows.");
        Grid.FromRows(map.Rows.ToArray()); // lisible
        return map;
    }
}
