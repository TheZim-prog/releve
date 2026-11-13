using Game.Core.Combat;

namespace Game.Core.Progression;

/// <summary>
/// Vérifie qu'une fiche de héros respecte les règles de progression : pas plus de points que son niveau n'en donne,
/// des cases achetées dans l'ordre des paliers, et un équipement pris parmi ce qu'il a acheté et qui est codé.
/// C'est ce que vérifiera aussi l'écran de création.
/// </summary>
internal static class HeroSheetValidator
{
    public static void Validate(HeroSheet sheet, SkillTree tree, ProgressionRules rules, IReadOnlyDictionary<string, SkillDefinition> skills)
    {
        if (string.IsNullOrWhiteSpace(sheet.Id))
            throw new ArgumentException("A hero needs an id.");
        try
        {
            CheckLevel(sheet, rules);
            var learned = CheckLearned(sheet, tree, rules);
            CheckEquipped(sheet, learned, rules, skills);
            CheckPassive(sheet, learned);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"Hero '{sheet.Id}': {e.Message}", e);
        }
    }

    private static void CheckLevel(HeroSheet sheet, ProgressionRules rules)
    {
        if (sheet.Level < 1 || sheet.Level > rules.MaxLevel)
            throw new ArgumentException($"level {sheet.Level} is outside [1, {rules.MaxLevel}].");
        if (sheet.Points is null || sheet.Learned is null || sheet.Equipped is null)
            throw new ArgumentException("points, learned and equipped cannot be null.");
        sheet.Points.Validate();
        var spent = sheet.Points.Hp + sheet.Points.Power + sheet.Points.Initiative;
        if (spent > rules.StatPointsAt(sheet.Level))
            throw new ArgumentException($"{spent} stat points spent, {rules.StatPointsAt(sheet.Level)} available at level {sheet.Level}.");
    }

    /// <summary>Les cases achetées : dans l'arbre, sans doublon, payées, et chaque palier ouvert par les précédents.</summary>
    private static List<TreeNode> CheckLearned(HeroSheet sheet, SkillTree tree, ProgressionRules rules)
    {
        if (sheet.Learned.Distinct().Count() != sheet.Learned.Count)
            throw new ArgumentException("a skill is learned twice.");
        if (sheet.Learned.Count > rules.SkillPointsAt(sheet.Level))
            throw new ArgumentException(
                $"{sheet.Learned.Count} skills learned, {rules.SkillPointsAt(sheet.Level)} skill points available at level {sheet.Level}.");

        var nodes = tree.Nodes();
        var learned = sheet.Learned
            .Select(id => nodes.FirstOrDefault(n => n.Id == id)
                ?? throw new ArgumentException($"'{id}' is not in the {tree.Archetype} tree (a variant comes with its skill: learn the skill)."))
            .ToList();
        foreach (var node in learned)
        {
            var needed = rules.Tiers[node.Tier].PointsInBranch;
            var below = learned.Count(n => n.BranchId == node.BranchId && n.Tier < node.Tier);
            if (below < needed)
                throw new ArgumentException(
                    $"'{node.Id}' needs {needed} points in the lower tiers of branch '{node.BranchId}' ({below} spent).");
        }
        return learned;
    }

    /// <summary>L'équipement : des compétences achetées (ou leur variante), codées, une seule version de chacune.</summary>
    private static void CheckEquipped(HeroSheet sheet, List<TreeNode> learned, ProgressionRules rules,
        IReadOnlyDictionary<string, SkillDefinition> skills)
    {
        if (sheet.Equipped.Count > rules.ActiveSlotsAt(sheet.Level))
            throw new ArgumentException(
                $"{sheet.Equipped.Count} skills equipped, {rules.ActiveSlotsAt(sheet.Level)} slots at level {sheet.Level}.");
        var used = new HashSet<TreeNode>();
        foreach (var id in sheet.Equipped)
        {
            var node = learned.FirstOrDefault(n => !n.IsPassive && (n.Id == id || n.Variant == id))
                ?? throw new ArgumentException($"'{id}' is equipped but not learned.");
            if (!used.Add(node))
                throw new ArgumentException($"'{node.Id}' is equipped twice (a skill and its variant share one slot).");
            if (!skills.ContainsKey(id))
                throw new ArgumentException($"'{id}' is not available yet and cannot be equipped.");
        }
    }

    private static void CheckPassive(HeroSheet sheet, List<TreeNode> learned)
    {
        if (sheet.Passive is not { } passive)
            return;
        if (!learned.Any(n => n.IsPassive && n.Id == passive))
            throw new ArgumentException($"passive '{passive}' is equipped but not learned.");
        if (!Passives.TryParse(passive, out _))
            throw new ArgumentException($"passive '{passive}' is not available yet and cannot be equipped.");
    }
}
