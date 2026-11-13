using Game.Core.Combat;
using Game.Core.Progression;
using Game.Core.Tests.Combat;

namespace Game.Core.Tests.Progression;

/// <summary>
/// Un petit contenu pour tester la progression, avec les règles réelles (3 branches de 3 paliers, 3 points à la
/// création puis 1 aux niveaux pairs, 3 emplacements puis 4 au niveau 10).
/// <para>
/// L'arbre de l'archétype « hero » a les branches a, b et c. Dans la branche a : a1 et a2 (palier 1), a3 et le passif
/// weakPoint (palier 2), a4 (lourde) et le passif aMaster (palier 3) ; variantes a1v, a2v, a3v. Idem pour b (passif
/// grip) et c (passif vigilance). Les passifs de palier 3 ne sont pas codés, ni la compétence c2 et sa variante c2v.
/// </para>
/// </summary>
internal static class ProgressionTestKit
{
    public static readonly ProgressionRules Rules = new()
    {
        MaxLevel = 20,
        StatPointsPerLevel = 1,
        StartingSkillPoints = 3,
        LevelsPerSkillPoint = 2,
        ActiveSlots = new[] { new ActiveSlotsStep { FromLevel = 1, Count = 3 }, new ActiveSlotsStep { FromLevel = 10, Count = 4 } },
        BranchesPerTree = 3,
        Tiers = new[]
        {
            new TierRules { PointsInBranch = 0, Actives = 2, Passives = 0, Variants = true, RequiresHeavy = false },
            new TierRules { PointsInBranch = 2, Actives = 1, Passives = 1, Variants = true, RequiresHeavy = false },
            new TierRules { PointsInBranch = 4, Actives = 1, Passives = 1, Variants = false, RequiresHeavy = true },
        },
    };

    public static readonly SkillTree Tree = new()
    {
        Archetype = "hero",
        Branches = new[] { Branch("a", "weakPoint"), Branch("b", "grip"), Branch("c", "vigilance") },
        Planned = new Dictionary<string, string> { ["c2"] = "C2", ["c2v"] = "C2 bis" },
    };

    /// <summary>Bibliothèque : toutes les compétences de l'arbre sauf celles à venir ; celles de palier 3 sont lourdes.</summary>
    public static readonly IReadOnlyDictionary<string, SkillDefinition> Skills = Tree.Nodes()
        .Where(n => !n.IsPassive)
        .SelectMany(n => new[] { n.Id, n.Variant })
        .OfType<string>()
        .Where(id => !Tree.Planned.ContainsKey(id))
        .ToDictionary(id => id, id => CombatTestKit.Strike with { Id = id, Weight = id.EndsWith('4') ? SkillWeight.Heavy : SkillWeight.Light });

    /// <summary>L'archétype « hero » : 100 PV, et l'attaque de base « strike ».</summary>
    public static readonly IReadOnlyDictionary<string, UnitDefinition> Units = new Dictionary<string, UnitDefinition>
    {
        ["hero"] = CombatTestKit.Unit(0, 0, 0, 0, hp: 100, skills: new[] { CombatTestKit.Strike }, basicAttack: "strike").Definition
            with { Id = "hero" },
    };

    public static HeroCatalog Catalog(params HeroSheet[] sheets) => new(Rules, Units, Skills, new[] { Tree }, sheets);

    public static HeroSheet Sheet(int level = 20, string[]? learned = null, string[]? equipped = null, string? passive = null,
        StatPoints? points = null, string id = "h") => new()
    {
        Id = id,
        Archetype = "hero",
        Level = level,
        Points = points ?? StatPoints.None,
        Learned = learned ?? Array.Empty<string>(),
        Equipped = equipped ?? Array.Empty<string>(),
        Passive = passive,
    };

    public static TreeBranch Branch(string id, string tier2Passive) => new()
    {
        Id = id,
        Name = id.ToUpperInvariant(),
        Tiers = new[]
        {
            new TreeTier { Actives = new[] { Active($"{id}1", $"{id}1v"), Active($"{id}2", $"{id}2v") } },
            new TreeTier
            {
                Actives = new[] { Active($"{id}3", $"{id}3v") },
                Passives = new[] { new TreePassive { Id = tier2Passive, Name = tier2Passive } },
            },
            new TreeTier
            {
                Actives = new[] { Active($"{id}4") },
                Passives = new[] { new TreePassive { Id = $"{id}Master", Name = $"Master {id}", Planned = true } },
            },
        },
    };

    public static TreeActive Active(string skill, string? variant = null) => new() { Skill = skill, Variant = variant };
}
