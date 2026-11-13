using Game.Core.Combat;

namespace Game.Core.Progression;

/// <summary>
/// Vérifie qu'un arbre a la forme commune à tous (<see cref="ProgressionRules"/>) et que chacune de ses compétences est
/// soit dans la bibliothèque, soit déclarée « à venir », jamais les deux : une faute de frappe ne passe pas, et la
/// liste des compétences à venir reste à jour quand on en code une.
/// </summary>
internal static class SkillTreeValidator
{
    public static void Validate(SkillTree tree, ProgressionRules rules, IReadOnlyDictionary<string, SkillDefinition> skills)
    {
        if (string.IsNullOrWhiteSpace(tree.Archetype))
            throw new ArgumentException("A skill tree needs an archetype.");
        try
        {
            CheckShape(tree, rules, skills);
            CheckIds(tree, skills);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"Skill tree '{tree.Archetype}': {e.Message}", e);
        }
    }

    private static void CheckShape(SkillTree tree, ProgressionRules rules, IReadOnlyDictionary<string, SkillDefinition> skills)
    {
        if (tree.Branches is null || tree.Branches.Count != rules.BranchesPerTree || tree.Branches.Any(b => b is null))
            throw new ArgumentException($"{rules.BranchesPerTree} branches expected.");
        if (tree.Planned is null)
            throw new ArgumentException("The planned skill list is null.");
        foreach (var branch in tree.Branches)
        {
            if (string.IsNullOrWhiteSpace(branch.Id) || string.IsNullOrWhiteSpace(branch.Name))
                throw new ArgumentException("A branch needs an id and a name.");
            if (branch.Tiers is null || branch.Tiers.Count != rules.Tiers.Count || branch.Tiers.Any(t => t is null))
                throw new ArgumentException($"Branch '{branch.Id}': {rules.Tiers.Count} tiers expected.");
            for (var rank = 0; rank < rules.Tiers.Count; rank++)
                CheckTier(branch.Tiers[rank], rules.Tiers[rank], $"Branch '{branch.Id}', tier {rank + 1}", skills);
        }
        if (tree.Branches.Select(b => b.Id).Distinct().Count() != tree.Branches.Count)
            throw new ArgumentException("Two branches share the same id.");
    }

    private static void CheckTier(TreeTier tier, TierRules rules, string where, IReadOnlyDictionary<string, SkillDefinition> skills)
    {
        if (tier.Actives is null || tier.Actives.Count != rules.Actives || tier.Actives.Any(a => a is null))
            throw new ArgumentException($"{where}: {rules.Actives} active skill(s) expected.");
        if (tier.Passives is null || tier.Passives.Count != rules.Passives || tier.Passives.Any(p => p is null))
            throw new ArgumentException($"{where}: {rules.Passives} passive(s) expected.");
        foreach (var active in tier.Actives)
        {
            if (string.IsNullOrWhiteSpace(active.Skill))
                throw new ArgumentException($"{where}: a skill has no id.");
            if (rules.Variants != (active.Variant is not null) || active.Variant is { Length: 0 })
                throw new ArgumentException(
                    $"{where}: skill '{active.Skill}' {(rules.Variants ? "needs a variant" : "cannot have a variant")}.");
            if (rules.RequiresHeavy && skills.TryGetValue(active.Skill, out var skill) && skill.Weight != SkillWeight.Heavy)
                throw new ArgumentException($"{where}: skill '{active.Skill}' must be a heavy action.");
        }
        foreach (var passive in tier.Passives)
        {
            if (string.IsNullOrWhiteSpace(passive.Id) || string.IsNullOrWhiteSpace(passive.Name))
                throw new ArgumentException($"{where}: a passive needs an id and a name.");
            if (passive.Planned == Passives.TryParse(passive.Id, out _))
                throw new ArgumentException(
                    $"{where}: passive '{passive.Id}' is {(passive.Planned ? "marked planned but already exists" : "unknown (mark it planned)")}.");
        }
    }

    /// <summary>Identifiants uniques dans l'arbre ; chaque compétence dans la bibliothèque ou « à venir », pas les deux.</summary>
    private static void CheckIds(SkillTree tree, IReadOnlyDictionary<string, SkillDefinition> skills)
    {
        var nodes = tree.Nodes();
        var skillIds = nodes.Where(n => !n.IsPassive).SelectMany(n => new[] { n.Id, n.Variant }).OfType<string>().ToList();
        var all = skillIds.Concat(nodes.Where(n => n.IsPassive).Select(n => n.Id)).ToList();
        if (all.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new ArgumentException($"'{twice.Key}' appears twice.");

        foreach (var id in skillIds)
        {
            var known = skills.ContainsKey(id);
            if (known == tree.Planned.ContainsKey(id))
                throw new ArgumentException(known
                    ? $"Skill '{id}' exists: remove it from the planned list."
                    : $"Unknown skill '{id}' (add it to the skill library, or to the planned list).");
        }
        if (tree.Planned.FirstOrDefault(p => !skillIds.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value)) is { Key: not null } stray)
            throw new ArgumentException($"Planned skill '{stray.Key}' is not in the tree, or has no name.");
    }
}
