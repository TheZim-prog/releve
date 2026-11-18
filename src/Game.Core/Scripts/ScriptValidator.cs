using Game.Core.Combat;

namespace Game.Core.Scripts;

/// <summary>
/// Vérifie le script d'un combat avant de le jouer : repères d'unités uniques, déclencheurs bien formés, et chaque
/// déclencheur peut partir (il ne suit pas un déclencheur inconnu, ni une boucle qui ne finirait jamais).
/// </summary>
public static class ScriptValidator
{
    public static void Validate(CombatSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var tags = Tags(setup);
        var script = setup.Script;
        if (script.Any(t => t is null))
            throw new ArgumentException("The script contains a null trigger.");
        var ids = new HashSet<string>();
        foreach (var trigger in script)
        {
            if (string.IsNullOrWhiteSpace(trigger.Id))
                throw new ArgumentException("A script trigger needs an id.");
            if (!ids.Add(trigger.Id))
                throw new ArgumentException($"Duplicate script trigger id '{trigger.Id}'.");
        }
        foreach (var trigger in script)
        {
            try
            {
                ValidateConditions(trigger.When, ids, tags);
                ValidateEffects(trigger.Do);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"Script trigger '{trigger.Id}': {e.Message}", e);
            }
        }
        RefuseLoops(script);
    }

    /// <summary>Repères des unités du combat (au début et dans les vagues), chacun unique.</summary>
    private static HashSet<string> Tags(CombatSetup setup)
    {
        var tags = new HashSet<string>();
        foreach (var tag in setup.Units.Concat(setup.Waves.SelectMany(w => w.Units)).Select(u => u.Tag))
        {
            if (tag is null)
                continue;
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("A unit tag cannot be blank.");
            if (!tags.Add(tag))
                throw new ArgumentException($"Two units share the tag '{tag}'.");
        }
        return tags;
    }

    private static void ValidateConditions(ScriptCondition? when, IReadOnlySet<string> ids, IReadOnlySet<string> tags)
    {
        if (when is null)
            return;
        if (when.AtMs is < 0)
            throw new ArgumentException("atMs cannot be negative.");
        if (when.After is { } after && !ids.Contains(after))
            throw new ArgumentException($"it follows an unknown trigger '{after}'.");
        if (when.UnitDown is { } tag && !tags.Contains(tag))
            throw new ArgumentException($"unitDown names an unknown unit tag '{tag}'.");
    }

    private static void ValidateEffects(IReadOnlyList<ScriptEffect>? effects)
    {
        if (effects is not { Count: > 0 } || effects.Any(e => e is null))
            throw new ArgumentException("it needs effects, without null entries.");
        foreach (var effect in effects)
        {
            if (string.IsNullOrWhiteSpace(effect.Say))
                throw new ArgumentException("an effect needs a text to say.");
            if (effect.Speaker is { } speaker && string.IsNullOrWhiteSpace(speaker))
                throw new ArgumentException("a speaker cannot be blank.");
        }
    }

    /// <summary>Un déclencheur qui se suit lui-même, directement ou par une chaîne de <c>after</c>, ne partirait jamais.</summary>
    private static void RefuseLoops(IReadOnlyList<ScriptTrigger> script)
    {
        var after = script.ToDictionary(t => t.Id, t => t.When?.After);
        foreach (var trigger in script)
        {
            var seen = new HashSet<string> { trigger.Id };
            for (var next = after[trigger.Id]; next is not null; next = after[next])
            {
                if (!seen.Add(next))
                    throw new ArgumentException($"Script trigger '{trigger.Id}' waits on a loop of triggers: it would never start.");
            }
        }
    }
}
