using Game.Core.Combat;
using Game.Core.Progression;
using static Game.Core.Tests.Progression.ProgressionTestKit;

namespace Game.Core.Tests.Progression;

public class SkillTreeTests
{
    private static void Validate(SkillTree tree, IReadOnlyDictionary<string, SkillDefinition>? skills = null) =>
        SkillTreeValidator.Validate(tree, Rules, skills ?? Skills);

    /// <summary>L'arbre de test, avec la branche a remplacée.</summary>
    private static SkillTree WithBranchA(TreeBranch branch) =>
        Tree with { Branches = new[] { branch, Tree.Branches[1], Tree.Branches[2] } };

    private static TreeBranch WithTier(int rank, TreeTier tier)
    {
        var tiers = Tree.Branches[0].Tiers.ToArray();
        tiers[rank] = tier;
        return Tree.Branches[0] with { Tiers = tiers };
    }

    [Fact]
    public void AWellFormedTree_IsAccepted_AndListsItsNodes()
    {
        Validate(Tree);

        var nodes = Tree.Nodes();
        Assert.Equal(18, nodes.Count); // 3 branches × (4 compétences actives + 2 passifs)
        Assert.Equal(new TreeNode("a1", "a", 0, IsPassive: false, "a1v"), nodes[0]);
        Assert.Contains(new TreeNode("weakPoint", "a", 1, IsPassive: true), nodes);
        Assert.Contains(new TreeNode("c4", "c", 2, IsPassive: false), nodes);
    }

    [Fact]
    public void FromJson_ReadsATree()
    {
        var tree = SkillTree.FromJson("""
            {
              "archetype": "hero",
              "branches": [
                { "id": "a", "name": "A", "tiers": [
                  { "actives": [ { "skill": "a1", "variant": "a1v" } ] },
                  { "actives": [ { "skill": "a3" } ], "passives": [ { "id": "contagion", "name": "Contagion", "planned": true } ] }
                ] }
              ],
              "planned": { "a3": "À venir" }
            }
            """);

        Assert.Equal("hero", tree.Archetype);
        Assert.Equal(new[] { "a1", "a3", "contagion" }, tree.Nodes().Select(n => n.Id));
        Assert.Equal("À venir", tree.Planned["a3"]);
        Assert.True(tree.Branches[0].Tiers[1].Passives[0].Planned);
    }

    [Fact]
    public void TheShape_IsTheSameForEveryTree()
    {
        // Deux branches au lieu de trois.
        Assert.Throws<ArgumentException>(() => Validate(Tree with { Branches = Tree.Branches.Take(2).ToArray() }));
        // Une seule compétence au palier 1.
        Assert.Throws<ArgumentException>(() => Validate(WithBranchA(WithTier(0, new TreeTier { Actives = new[] { Active("a1", "a1v") } }))));
        // Pas de passif au palier 2.
        Assert.Throws<ArgumentException>(() => Validate(WithBranchA(WithTier(1, new TreeTier { Actives = new[] { Active("a3", "a3v") } }))));
        // Deux branches du même nom.
        Assert.Throws<ArgumentException>(() => Validate(Tree with { Branches = new[] { Tree.Branches[0], Tree.Branches[0], Tree.Branches[2] } }));
    }

    [Fact]
    public void Tier1And2Skills_NeedAVariant_TheTier3SkillHasNone()
    {
        var withoutVariant = new TreeTier { Actives = new[] { Active("a1"), Active("a2", "a2v") } };
        var heavyWithVariant = Tree.Branches[0].Tiers[2] with { Actives = new[] { Active("a4", "a3v") } };

        Assert.Throws<ArgumentException>(() => Validate(WithBranchA(WithTier(0, withoutVariant))));
        Assert.Throws<ArgumentException>(() => Validate(WithBranchA(WithTier(2, heavyWithVariant))));
    }

    [Fact]
    public void TheTier3Skill_IsAHeavyAction()
    {
        var skills = new Dictionary<string, SkillDefinition>(Skills) { ["a4"] = Skills["a4"] with { Weight = SkillWeight.Light } };

        Assert.Throws<ArgumentException>(() => Validate(Tree, skills));
    }

    [Fact]
    public void ASkill_IsEitherInTheLibraryOrPlanned_NeverBoth()
    {
        var withoutC1 = Skills.Where(s => s.Key != "c1").ToDictionary();

        // Inconnue : ni dans la bibliothèque, ni à venir (une faute de frappe, par exemple).
        Assert.Throws<ArgumentException>(() => Validate(Tree, withoutC1));
        Validate(Tree with { Planned = new Dictionary<string, string>(Tree.Planned) { ["c1"] = "C1" } }, withoutC1);
        // Codée entre-temps, mais encore dans la liste des compétences à venir.
        Assert.Throws<ArgumentException>(() => Validate(Tree with { Planned = new Dictionary<string, string>(Tree.Planned) { ["a1"] = "A1" } }));
        // À venir, mais absente de l'arbre.
        Assert.Throws<ArgumentException>(() => Validate(Tree with { Planned = new Dictionary<string, string>(Tree.Planned) { ["zz"] = "?" } }));
    }

    [Fact]
    public void APassive_IsPlannedExactlyWhenItIsNotCoded()
    {
        TreeTier Tier2(TreePassive passive) => Tree.Branches[0].Tiers[1] with { Passives = new[] { passive } };

        // Codé, mais marqué à venir.
        Assert.Throws<ArgumentException>(() =>
            Validate(WithBranchA(WithTier(1, Tier2(new TreePassive { Id = "weakPoint", Name = "Point faible", Planned = true })))));
        // Pas codé, et pas marqué à venir.
        Assert.Throws<ArgumentException>(() =>
            Validate(WithBranchA(WithTier(1, Tier2(new TreePassive { Id = "contagion", Name = "Contagion" })))));
        Validate(WithBranchA(WithTier(1, Tier2(new TreePassive { Id = "contagion", Name = "Contagion", Planned = true }))));
    }

    [Fact]
    public void AnId_AppearsOnlyOnceInATree()
    {
        // La variante de a2 est aussi la compétence a1.
        var twice = new TreeTier { Actives = new[] { Active("a1", "a1v"), Active("a2", "a1") } };

        Assert.Throws<ArgumentException>(() => Validate(WithBranchA(WithTier(0, twice))));
    }
}
