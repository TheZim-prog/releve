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
        if (when.UnitReady is { } ready)
        {
            if (!tags.TryGetValue(ready, out var unit))
                throw new ArgumentException($"unitReady names an unknown unit tag '{ready}'.");
            if (unit.Definition.IsStructure)
                throw new ArgumentException($"unitReady names '{ready}', a structure, which never acts.");
        }
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
            if (effect.Lock is not null)
            {
                if (effect.Say is not null || effect.Freeze || effect.Impose is not null || effect.Act is not null || effect.Leave is not null)
                    throw new ArgumentException("\"lock\" stands alone in its effect.");
                continue;
            }
            if (effect.Leave is { } leaving)
            {
                if (effect.Freeze || effect.Impose is not null || effect.Act is not null)
                    throw new ArgumentException("\"leave\" goes alone in its effect, with at most a line to say.");
                ValidateLeaving(leaving, tags);
                continue;
            }
            if (effect.Impose is not null && effect.Act is not null)
                throw new ArgumentException("an effect either imposes actions or orders them, not both.");
            if (effect.Impose is null && effect.Act is null)
            {
                if (effect.Say is null)
                    throw new ArgumentException("an effect needs a text to say, or actions to impose or order.");
                continue;
            }
            if (effect.Freeze)
                throw new ArgumentException("\"freeze\" is for a message alone, not with actions.");
            if (effect.Impose is { } imposed)
                ValidateActions(imposed, tags, "imposed on", heroesOnly: true);
            if (effect.Act is { } ordered)
                ValidateActions(ordered, tags, "ordered to", heroesOnly: false);
        }
    }

    /// <summary>
    /// Au moins une action, au plus une par unité ; chacune pour une unité repérée (un héros, pour une action imposée),
    /// avec une compétence qu'elle a, visée comme elle le demande. <paramref name="verb"/> : « imposed on », « ordered
    /// to », pour les messages.
    /// </summary>
    private static void ValidateActions(IReadOnlyList<ScriptAction> actions, IReadOnlyDictionary<string, UnitSetup> tags, string verb,
        bool heroesOnly)
    {
        if (actions.Count == 0 || actions.Any(a => a is null))
            throw new ArgumentException($"actions {verb} units need at least one action, without null entries.");
        if (actions.GroupBy(a => a.Unit).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new ArgumentException($"two actions are {verb} '{twice.Key}' at once.");
        foreach (var action in actions)
        {
            if (action.Unit is null || !tags.TryGetValue(action.Unit, out var unit))
                throw new ArgumentException($"an action is {verb} an unknown unit tag '{action.Unit}'.");
            if (heroesOnly && !IsHero(unit))
                throw new ArgumentException($"an action is {verb} '{action.Unit}', which is not a hero.");
            if (unit.Definition.IsStructure && action.MoveTo is not null)
                throw new ArgumentException($"the action {verb} '{action.Unit}' moves a structure, which never moves.");
            if (action.Skill is null && action.MoveTo is null)
                throw new ArgumentException($"the action {verb} '{action.Unit}' needs a skill or a cell to move to.");
            if (action.Skill is null && (action.Target is not null || action.Cell is not null))
                throw new ArgumentException($"the action {verb} '{action.Unit}' has a target but no skill.");
            if (action.Target is { } target && !tags.ContainsKey(target))
                throw new ArgumentException($"the action {verb} '{action.Unit}' aims at an unknown unit tag '{target}'.");
            if (action.Skill is { } id)
            {
                var skill = unit.Definition.Skills.FirstOrDefault(s => s.Id == id)
                    ?? throw new ArgumentException($"'{action.Unit}' has no skill '{id}' to use.");
                ValidateAim(action, skill, verb);
            }
        }
    }

    /// <summary>Au moins une unité, chacune une fois, repérée et pas un héros (un héros ne quitte pas le combat sur ordre).</summary>
    private static void ValidateLeaving(IReadOnlyList<string> leaving, IReadOnlyDictionary<string, UnitSetup> tags)
    {
        if (leaving.Count == 0 || leaving.Any(t => t is null) || leaving.Distinct().Count() != leaving.Count)
            throw new ArgumentException("\"leave\" needs at least one unit tag, each once.");
        foreach (var tag in leaving)
        {
            if (!tags.TryGetValue(tag, out var unit))
                throw new ArgumentException($"\"leave\" names an unknown unit tag '{tag}'.");
            if (IsHero(unit))
                throw new ArgumentException($"\"leave\" names '{tag}', a hero: only the game's units leave on the script's word.");
            // Partie, elle ne compterait plus pour la protection : l'objectif serait faussé.
            if (unit.Definition.IsStructure || unit.IsProtected)
                throw new ArgumentException($"\"leave\" names '{tag}', a structure or a unit to protect, which cannot run away.");
        }
    }

    /// <summary>
    /// La visée d'une compétence du script n'est pas libre : une zone vise une case, une compétence sur soi rien, un
    /// sauvetage ou une télékinésie une unité et la case où elle va, les autres une unité seule. Le reste se vérifie au
    /// moment d'agir (une visée impossible y est abandonnée).
    /// </summary>
    private static void ValidateAim(ScriptAction action, SkillDefinition skill, string verb)
    {
        // Les compétences à visée particulière (chaîne, mouvement du lanceur, réanimation à distance, tourniquet) se
        // vérifient seulement au moment d'agir.
        if (skill.Chain is not null || skill.SelfMove is not null || skill.RevivesAlly || skill.ForcedMove?.Type == ForcedMoveType.Spin)
            return;
        var (fits, aims) = skill.Zone is not null ? (action.Cell is not null && action.Target is null, "a cell")
            : skill.Target == SkillTargeting.Self ? (action.Cell is null && action.Target is null, "nothing")
            : skill.IsRescue || skill.ForcedMove?.Type == ForcedMoveType.Place
                ? (action.Target is not null && action.Cell is not null, "a unit and a cell (where it goes)")
            : (action.Target is not null && action.Cell is null, "a unit (and no cell)");
        if (!fits)
            throw new ArgumentException($"the skill '{skill.Id}' {verb} '{action.Unit}' must aim at {aims}.");
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
