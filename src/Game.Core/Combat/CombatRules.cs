using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Core.Combat;

/// <summary>
/// Paramètres d'équilibrage du combat, chargés depuis <c>data/combat-rules.json</c>.
/// Toutes les valeurs sont des entiers (simulation déterministe).
/// </summary>
public sealed record CombatRules
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Fréquence de la simulation. Sert à convertir les durées en secondes en ticks.</summary>
    public required int TicksPerSecond { get; init; }

    /// <summary>Valeur d'une jauge pleine.</summary>
    public required int GaugeCapacity { get; init; }

    /// <summary>Pourcentage de la capacité dont repart la jauge après un déplacement seul.</summary>
    public required int MoveOnlyRestartPercent { get; init; }

    [JsonIgnore]
    public int MoveOnlyRestartGauge => (int)((long)GaugeCapacity * MoveOnlyRestartPercent / 100);

    public static CombatRules FromJson(string json)
    {
        var rules = JsonSerializer.Deserialize<CombatRules>(json, JsonOptions)
            ?? throw new JsonException("Combat rules are null.");
        rules.Validate();
        return rules;
    }

    public void Validate()
    {
        if (TicksPerSecond <= 0)
            throw new ArgumentException($"{nameof(TicksPerSecond)} must be positive.");
        if (GaugeCapacity <= 0)
            throw new ArgumentException($"{nameof(GaugeCapacity)} must be positive.");
        if (MoveOnlyRestartPercent is < 0 or > 100)
            throw new ArgumentException($"{nameof(MoveOnlyRestartPercent)} must be between 0 and 100.");
    }
}
