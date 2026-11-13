namespace Game.Core.Progression;

/// <summary>
/// Arbre de compétences d'un archétype (data/trees) : ses branches, et le nom des compétences qu'il prévoit mais qui ne
/// sont pas encore dans la bibliothèque. Une compétence à venir peut être achetée (elle compte pour atteindre le palier
/// suivant), pas équipée.
/// </summary>
public sealed record SkillTree
{
    /// <summary>Identifiant du type d'unité de l'archétype (ex. « colossus »).</summary>
    public required string Archetype { get; init; }

    public required IReadOnlyList<TreeBranch> Branches { get; init; }

    /// <summary>Compétences et variantes à venir (absentes de la bibliothèque) : identifiant → nom affiché.</summary>
    public IReadOnlyDictionary<string, string> Planned { get; init; } = new Dictionary<string, string>();

    /// <summary>Lit un arbre, sans le vérifier : voir <see cref="SkillTreeValidator"/>.</summary>
    public static SkillTree FromJson(string json) => DataJson.Deserialize<SkillTree>(json);

    /// <summary>Toutes les cases de l'arbre, branche par branche puis palier par palier (liste recalculée à chaque appel).</summary>
    public IReadOnlyList<TreeNode> Nodes() => Branches
        .SelectMany(branch => branch.Tiers.SelectMany((tier, rank) =>
            tier.Actives.Select(a => new TreeNode(a.Skill, branch.Id, rank, IsPassive: false, a.Variant))
                .Concat(tier.Passives.Select(p => new TreeNode(p.Id, branch.Id, rank, IsPassive: true)))))
        .ToList();
}
