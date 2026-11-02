using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Core;

/// <summary>Options communes de lecture des fichiers de <c>data/</c>.</summary>
internal static class DataJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"{typeof(T).Name} is null.");

    /// <summary>
    /// Valide une liste de données : aucune entrée nulle, chaque entrée valide, identifiants uniques.
    /// Renvoie la liste, typée sans les <c>null</c>.
    /// </summary>
    public static IReadOnlyList<T> ValidateList<T>(IEnumerable<T?> items, Action<T> validate, Func<T, string> idOf, string kind)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = new List<T>();
        var ids = new HashSet<string>();
        foreach (var item in items)
        {
            if (item is null)
                throw new ArgumentException($"The {kind} list contains a null entry.");
            validate(item);
            if (!ids.Add(idOf(item)))
                throw new ArgumentException($"Duplicate {kind} id '{idOf(item)}'.");
            list.Add(item);
        }
        return list;
    }
}
