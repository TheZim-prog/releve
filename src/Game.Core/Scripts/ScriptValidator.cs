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
                ValidateEffects(trigger.Do, tags);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"Script trigger '{trigger.Id}': {e.Message}", e);
            }
        }
        RefuseLoops(script);
    }

    /// <summary>Les unités du combat qui ont un repère (au début et dans les vagues), chaque repère unique.</summary>
    private static Dictionary<string, UnitSetup> Tags(CombatSetup setup)
    {
        var tags = new Dictionary<string, UnitSetup>();
        foreach (var unit in setup.Units.Concat(setup.Waves.SelectMany(w => w.Units)))
        {
            if (unit.Tag is not { } tag)
                continue;
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("A unit tag cannot be blank.");
            if (!tags.TryAdd(tag, unit))
                throw new ArgumentException($"Two units share the tag '{tag}'.");
        }
        return tags;
    }

    private static void ValidateConditions(ScriptCondition? when, IReadOnlySet<string> ids, IReadOnlyDictionary<string, UnitSetup> tags)
    {
        if (when is null)
            return;
        if (when.AtMs is < 0)
            throw new ArgumentException("atMs cannot be negative.");
        if (when.After is { } after && !ids.Contains(after))
            throw new ArgumentException($"it follows an unknown trigger '{after}'.");
        if (when.UnitDown is { } tag && !tags.ContainsKey(tag))
            throw new ArgumentException($"unitDown names an unknown unit tag '{tag}'.");
    }

    private static void ValidateEffects(IReadOnlyList<ScriptEffect>? effects, IReadOnlyDictionary<string, UnitSetup> tags)
    {
        if (effects is not { Count: > 0 } || effects.Any(e => e is null))
            throw new ArgumentException("it needs effects, without null entries.");
        foreach (var effect in effects)
        {
            if (effect.Say is { } say && string.IsNullOrWhiteSpace(say))
                throw new ArgumentException("a text to say cannot be blank.");
            if (effect.Speaker is { } speaker && (string.IsNullOrWhiteSpace(speaker) || effect.Say is null))
                throw new ArgumentException("a speaker needs a text to say, and cannot be blank.");
            if (effect.Impose is null)
            {
                if (effect.Say is null)
                    throw new ArgumentException("an effect needs a text to say, or actions to impose.");
                continue;
            }
            if (effect.Freeze)
                throw new ArgumentException("imposed actions already freeze the combat: no \"freeze\" with them.");
            ValidateImposed(effect.Impose, tags);
        }
    }

    /// <summary>
    /// Au moins une action, au plus une par héros ; chacune pour un héros, avec une compétence qu'il a, visée comme elle
    /// le demande.
    /// </summary>
    private static void ValidateImposed(IReadOnlyList<ImposedAction> actions, IReadOnlyDictionary<string, UnitSetup> tags)
    {
        if (actions.Count == 0 || actions.Any(a => a is null))
            throw new ArgumentException("imposed actions need at least one action, without null entries.");
        if (actions.GroupBy(a => a.Unit).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new ArgumentException($"two actions are imposed on '{twice.Key}' at once.");
        foreach (var action in actions)
        {
            if (action.Unit is null || !tags.TryGetValue(action.Unit, out var unit))
                throw new ArgumentException($"an action is imposed on an unknown unit tag '{action.Unit}'.");
            if (!IsHero(unit))
                throw new ArgumentException($"an action is imposed on '{action.Unit}', which is not a hero.");
            if (action.Skill is null && action.MoveTo is null)
                throw new ArgumentException($"the action imposed on '{action.Unit}' needs a skill or a cell to move to.");
            if (action.Skill is null && (action.Target is not null || action.Cell is not null))
                throw new ArgumentException($"the action imposed on '{action.Unit}' has a target but no skill.");
            if (action.Target is { } target && !tags.ContainsKey(target))
                throw new ArgumentException($"the action imposed on '{action.Unit}' aims at an unknown unit tag '{target}'.");
            if (action.Skill is { } id)
            {
                var skill = unit.Definition.Skills.FirstOrDefault(s => s.Id == id)
                    ?? throw new ArgumentException($"'{action.Unit}' has no skill '{id}' to use.");
                ValidateAim(action, skill);
            }
        }
    }

    /// <summary>
    /// La visée d'une compétence imposée n'est pas libre : une zone vise une case, une compétence sur soi rien, les autres
    /// une unité (plus une case pour celles qui la déplacent quelque part, comme la télékinésie). Le reste se vérifie au
    /// moment d'imposer (une visée impossible y est abandonnée).
    /// </summary>
    private static void ValidateAim(ImposedAction action, SkillDefinition skill)
    {
        var (fits, aims) = skill.Zone is not null ? (action.Cell is not null && action.Target is null, "a cell")
            : skill.Target == SkillTargeting.Self ? (action.Cell is null && action.Target is null, "nothing")
            : (action.Target is not null, "a unit");
        if (!fits)
            throw new ArgumentException($"the skill '{skill.Id}' imposed on '{action.Unit}' must aim at {aims}.");
    }

    /// <summary>Un héros : du camp des joueurs, ni civil, ni personne à protéger, ni structure.</summary>
    private static bool IsHero(UnitSetup unit) =>
        unit.Team == CombatSimulation.PlayerTeam && !unit.Definition.IsCivilian && !unit.IsProtected && !unit.Definition.IsStructure;

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
