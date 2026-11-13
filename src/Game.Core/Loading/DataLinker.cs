using System.Text.Json;
using System.Text.Json.Nodes;

namespace Game.Core.Loading;

/// <summary>Une compétence ou une unité, références résolues : son identifiant, le fichier qui la décrit et son JSON complet.</summary>
internal sealed record LinkedItem(string Id, string Source, string Json);

/// <summary>
/// Résout les références entre fichiers de données : une compétence ou une unité est décrite une seule fois, puis
/// désignée ailleurs par son identifiant. Une unité liste ses compétences par identifiant (<c>"skills": ["punch"]</c>),
/// une compétence d'invocation désigne l'unité qu'elle pose (<c>"summon": "turret"</c>). Le résultat est le même JSON
/// que si tout était écrit sur place : c'est lui que lisent <see cref="Combat.SkillDefinition"/> et
/// <see cref="Combat.UnitDefinition"/>, qui font toutes les vérifications.
/// </summary>
internal sealed class DataLinker
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly Dictionary<string, (JsonObject Item, string Source)> _skills = new();
    private readonly Dictionary<string, (JsonObject Item, string Source)> _units = new();

    /// <summary>Ajoute les compétences d'un fichier (un tableau JSON) à la bibliothèque.</summary>
    public void AddSkills(string source, string json) => Add(_skills, "skill", source, json);

    /// <summary>Ajoute les unités d'un fichier (un tableau JSON) au catalogue.</summary>
    public void AddUnits(string source, string json) => Add(_units, "unit", source, json);

    /// <summary>Toutes les compétences, références résolues, dans l'ordre où elles ont été ajoutées.</summary>
    public IReadOnlyList<LinkedItem> Skills() =>
        _skills.Select(s => new LinkedItem(s.Key, s.Value.Source, Skill(s.Key, new List<string>()).ToJsonString())).ToList();

    /// <summary>Toutes les unités, références résolues, dans l'ordre où elles ont été ajoutées.</summary>
    public IReadOnlyList<LinkedItem> Units() =>
        _units.Select(u => new LinkedItem(u.Key, u.Value.Source, Unit(u.Key, new List<string>()).ToJsonString())).ToList();

    private static void Add(Dictionary<string, (JsonObject, string Source)> target, string kind, string source, string json)
    {
        if (JsonNode.Parse(json, documentOptions: ReadOptions) is not JsonArray entries)
            throw new JsonException($"{source}: a list of {kind}s is expected.");
        foreach (var entry in entries)
        {
            if (entry is not JsonObject item || StringIn(item["id"]) is not { Length: > 0 } id)
                throw new JsonException($"{source}: every {kind} needs an id.");
            if (target.TryGetValue(id, out var existing))
                throw new ArgumentException($"{source}: {kind} '{id}' is already described in {existing.Source}.");
            target.Add(id, (item, source));
        }
    }

    /// <summary>La compétence, avec l'unité qu'elle invoque écrite sur place.</summary>
    private JsonObject Skill(string id, List<string> path) => Resolve(_skills, "skill", id, path, LinkSkill);

    /// <summary>L'unité, avec ses compétences écrites sur place.</summary>
    private JsonObject Unit(string id, List<string> path) => Resolve(_units, "unit", id, path, LinkUnit);

    private static JsonObject Resolve(Dictionary<string, (JsonObject Item, string Source)> source, string kind, string id,
        List<string> path, Action<JsonObject, List<string>> link)
    {
        var step = $"{kind} '{id}'";
        if (!source.TryGetValue(id, out var found))
            throw new ArgumentException(path.Count == 0 ? $"Unknown {step}." : $"Unknown {step}, referenced by {path[^1]}.");
        if (path.Contains(step))
            throw new ArgumentException($"Circular reference: {string.Join(" -> ", path)} -> {step}.");
        var copy = (JsonObject)found.Item.DeepClone();
        path.Add(step);
        link(copy, path);
        path.RemoveAt(path.Count - 1);
        return copy;
    }

    private void LinkSkill(JsonObject skill, List<string> path)
    {
        if (StringIn(skill["summon"]) is { } unitId)
            skill["summon"] = Unit(unitId, path);
        else if (skill["summon"] is JsonObject inline)
            LinkUnit(RequireNew(inline, _units, "unit", path), path);
    }

    private void LinkUnit(JsonObject unit, List<string> path)
    {
        if (unit["skills"] is not JsonArray skills)
            return;
        for (var i = 0; i < skills.Count; i++)
        {
            if (StringIn(skills[i]) is { } skillId)
                skills[i] = Skill(skillId, path);
            else if (skills[i] is JsonObject inline)
                LinkSkill(RequireNew(inline, _skills, "skill", path), path);
        }
    }

    /// <summary>
    /// Une description écrite sur place ne peut pas reprendre un identifiant de la bibliothèque : il y aurait deux
    /// versions de la même chose, et régler l'une ne changerait pas l'autre.
    /// </summary>
    private static JsonObject RequireNew(JsonObject inline, Dictionary<string, (JsonObject, string Source)> library, string kind,
        List<string> path)
    {
        if (StringIn(inline["id"]) is { } id && library.TryGetValue(id, out var existing))
            throw new ArgumentException(
                $"{path[^1]} describes {kind} '{id}' in place, but it is already described in {existing.Source}: refer to it by its id.");
        return inline;
    }

    /// <summary>La chaîne portée par le nœud (un identifiant, ou une référence), ou <c>null</c> si ce n'en est pas une.</summary>
    private static string? StringIn(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
