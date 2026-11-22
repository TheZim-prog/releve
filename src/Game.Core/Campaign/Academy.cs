using Game.Core.Combat;
using Game.Core.Progression;

namespace Game.Core.Campaign;

/// <summary>
/// Ce qu'un joueur demande à l'académie : un héros de cet archétype, sous ce nom, avec ces cases de son arbre (ses
/// points de compétence de départ, cf. docs/design.md 2.7).
/// </summary>
public sealed record Recruit(string Name, string Archetype, IReadOnlyList<string> Learned);

public enum RecruitRefusal
{
    /// <summary>Le joueur n'est pas inscrit dans la campagne.</summary>
    NotEnrolled,

    /// <summary>Le joueur a déjà son duo au complet (héros vivants).</summary>
    DuoFull,

    /// <summary>Nom vide ou trop long.</summary>
    InvalidName,

    /// <summary>Aucun arbre de compétences pour cet archétype.</summary>
    UnknownArchetype,

    /// <summary>Les cases choisies ne respectent pas l'arbre (paliers, nombre de points de départ).</summary>
    InvalidSkills,
}

/// <summary>
/// L'académie (docs/design.md 4.3) : un joueur y crée ses héros, jusqu'à son duo au complet. Un héros recruté est de
/// niveau 1, sans point de stats ; il dépense tous ses points de compétence de départ dans son arbre (paliers ouverts),
/// et part équipé de ce qu'il a appris et qui est disponible (compétences codées, passif codé).
/// </summary>
public static class Academy
{
    /// <summary>Comme un nom de joueur.</summary>
    public const int MaxNameLength = 20;

    /// <summary>Pourquoi ce recrutement est impossible, ou <c>null</c> s'il est possible.</summary>
    public static RecruitRefusal? Check(CampaignState state, HeroCatalog catalog, string owner, Recruit recruit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(recruit);
        if (state.PlayerOf(owner) is null)
            return RecruitRefusal.NotEnrolled;
        if (state.Heroes.Count(h => h.Owner == owner && !h.IsDead) >= catalog.Rules.HeroesPerPlayer)
            return RecruitRefusal.DuoFull;
        if (string.IsNullOrWhiteSpace(recruit.Name) || recruit.Name.Trim().Length > MaxNameLength || recruit.Name.Any(char.IsControl))
            return RecruitRefusal.InvalidName;
        if (recruit.Archetype is null || !catalog.Trees.ContainsKey(recruit.Archetype))
            return RecruitRefusal.UnknownArchetype;
        if (recruit.Learned is null || recruit.Learned.Count != catalog.Rules.SkillPointsAt(1))
            return RecruitRefusal.InvalidSkills;
        try
        {
            catalog.Check(SheetOf("check", recruit, catalog));
        }
        catch (ArgumentException)
        {
            return RecruitRefusal.InvalidSkills;
        }
        return null;
    }

    /// <summary>Le héros recruté rejoint la campagne ; lève une exception si le recrutement est impossible.</summary>
    public static CampaignState Enlist(CampaignState state, HeroCatalog catalog, string owner, Recruit recruit)
    {
        if (Check(state, catalog, owner, recruit) is { } refusal)
            throw new InvalidOperationException($"Cannot recruit: {refusal}.");
        // Le numéro suivant le plus grand déjà pris : unique même si un héros venait à quitter la sauvegarde.
        var id = HeroIdPrefix + (state.Heroes.Select(h => NumberOf(h.Id)).DefaultIfEmpty(0).Max() + 1);
        var hero = new CampaignHero { Id = id, Name = recruit.Name.Trim(), Owner = owner, Sheet = SheetOf(id, recruit, catalog) };
        return state with { Heroes = state.Heroes.Append(hero).ToList() };
    }

    private const string HeroIdPrefix = "hero-";

    /// <summary>Le numéro d'un identifiant « hero-N », ou 0.</summary>
    private static int NumberOf(string id) =>
        id.StartsWith(HeroIdPrefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(HeroIdPrefix.Length), out var n) ? n : 0;

    /// <summary>
    /// Pour l'écran de création : les cases de l'arbre qu'on peut encore ajouter à ce choix (pas déjà prises, un point de
    /// départ restant, palier ouvert par les cases déjà choisies dans la branche).
    /// </summary>
    public static IReadOnlyList<TreeNode> Learnable(HeroCatalog catalog, string archetype, IReadOnlyCollection<string> chosen)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(chosen);
        if (!catalog.Trees.TryGetValue(archetype, out var tree) || chosen.Count >= catalog.Rules.SkillPointsAt(1))
            return Array.Empty<TreeNode>();
        var nodes = tree.Nodes();
        var picked = nodes.Where(n => chosen.Contains(n.Id)).ToList();
        return nodes.Where(n => !chosen.Contains(n.Id) && IsOpen(n, picked, catalog.Rules)).ToList();
    }

    /// <summary>
    /// Pour l'écran de création : le choix sans les cases dont le palier n'est plus ouvert (après en avoir retiré une
    /// en dessous), dans l'ordre du choix.
    /// </summary>
    public static IReadOnlyList<string> Prune(HeroCatalog catalog, string archetype, IReadOnlyList<string> chosen)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(chosen);
        if (!catalog.Trees.TryGetValue(archetype, out var tree))
            return Array.Empty<string>();
        var nodes = tree.Nodes();
        var kept = nodes.Where(n => chosen.Contains(n.Id)).ToList();
        while (kept.FirstOrDefault(n => !IsOpen(n, kept, catalog.Rules)) is { } closed)
            kept.Remove(closed);
        return chosen.Where(id => kept.Any(n => n.Id == id)).ToList();
    }

    /// <summary>Le palier de la case est ouvert par les cases choisies plus bas dans sa branche.</summary>
    private static bool IsOpen(TreeNode node, IReadOnlyCollection<TreeNode> picked, ProgressionRules rules) =>
        picked.Count(n => n.BranchId == node.BranchId && n.Tier < node.Tier) >= rules.Tiers[node.Tier].PointsInBranch;

    /// <summary>
    /// La fiche d'un héros recruté : niveau 1, ses cases, équipé des compétences apprises disponibles (dans la limite de
    /// ses emplacements) et du premier passif appris disponible.
    /// </summary>
    private static HeroSheet SheetOf(string id, Recruit recruit, HeroCatalog catalog)
    {
        var nodes = catalog.Trees[recruit.Archetype].Nodes();
        var learned = recruit.Learned.Select(l => nodes.FirstOrDefault(n => n.Id == l)).OfType<TreeNode>().ToList();
        return new HeroSheet
        {
            Id = id,
            Archetype = recruit.Archetype,
            Level = 1,
            Learned = recruit.Learned.ToList(),
            Equipped = learned.Where(n => !n.IsPassive && catalog.Skills.ContainsKey(n.Id))
                .Select(n => n.Id).Take(catalog.Rules.ActiveSlotsAt(1)).ToList(),
            Passive = learned.FirstOrDefault(n => n.IsPassive && Passives.TryParse(n.Id, out _))?.Id,
        };
    }
}
