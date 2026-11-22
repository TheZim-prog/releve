using Game.Core.Combat;
using Game.Core.Progression;

namespace Game.Core.Campaign;

/// <summary>Une stat dans laquelle mettre un point (la mobilité ne s'achète pas, cf. docs/design.md 2.6).</summary>
public enum StatKind
{
    Hp,
    Power,
    Initiative,
}

public enum TrainingRefusal
{
    /// <summary>Ce héros n'est pas au joueur (ou n'existe pas).</summary>
    NotYourHero,

    /// <summary>Un héros mort ne s'entraîne plus.</summary>
    HeroDead,

    /// <summary>Plus de point de ce genre à dépenser.</summary>
    NoPointToSpend,

    /// <summary>Les règles de l'arbre ou de l'équipement ne le permettent pas.</summary>
    NotAllowed,
}

/// <summary>Le résultat d'un entraînement : la campagne changée, ou la raison du refus.</summary>
public sealed record TrainingResult(CampaignState? State, TrainingRefusal? Refusal)
{
    public static TrainingResult Refused(TrainingRefusal reason) => new(null, reason);
}

/// <summary>Une compétence apprise à équiper, et ses versions disponibles (elle-même et sa variante, si elles sont codées).</summary>
public sealed record EquipOption(TreeNode Node, IReadOnlyList<string> Versions);

/// <summary>
/// L'entraînement à l'académie (docs/design.md 2.6, 2.7, 4.6) : dépenser les points gagnés en montant de niveau (un
/// point de stats dans les PV, la puissance ou l'initiative ; une case de l'arbre dans un palier ouvert), et changer
/// l'équipement (compétences actives ou leurs variantes, dans la limite des emplacements, et le passif). Points et cases
/// sont définitifs ; l'équipement se change librement. Tout est revérifié par les règles de progression.
/// </summary>
public static class Training
{
    public static TrainingResult SpendStatPoint(CampaignState state, HeroCatalog catalog, string owner, string heroId, StatKind stat)
    {
        if (Hero(state, owner, heroId) is not { } hero)
            return Refusal(state, owner, heroId);
        if (QuestRewards.UnspentStatPoints(hero.Sheet, catalog.Rules) <= 0)
            return TrainingResult.Refused(TrainingRefusal.NoPointToSpend);
        var points = hero.Sheet.Points;
        points = stat switch
        {
            StatKind.Hp => points with { Hp = points.Hp + 1 },
            StatKind.Power => points with { Power = points.Power + 1 },
            StatKind.Initiative => points with { Initiative = points.Initiative + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };
        return WithSheet(state, catalog, hero, hero.Sheet with { Points = points });
    }

    /// <summary>
    /// Apprend une case de l'arbre ; une compétence ou un passif disponible est aussitôt équipé s'il reste un emplacement
    /// (un passif, s'il n'en a pas).
    /// </summary>
    public static TrainingResult Learn(CampaignState state, HeroCatalog catalog, string owner, string heroId, string nodeId)
    {
        if (Hero(state, owner, heroId) is not { } hero)
            return Refusal(state, owner, heroId);
        var sheet = hero.Sheet;
        if (QuestRewards.UnspentSkillPoints(sheet, catalog.Rules) <= 0)
            return TrainingResult.Refused(TrainingRefusal.NoPointToSpend);
        if (Learnable(catalog, sheet).FirstOrDefault(n => n.Id == nodeId) is not { } node)
            return TrainingResult.Refused(TrainingRefusal.NotAllowed);
        sheet = sheet with { Learned = sheet.Learned.Append(node.Id).ToList() };
        if (!node.IsPassive && catalog.Skills.ContainsKey(node.Id) && sheet.Equipped.Count < catalog.Rules.ActiveSlotsAt(sheet.Level))
            sheet = sheet with { Equipped = sheet.Equipped.Append(node.Id).ToList() };
        if (node.IsPassive && sheet.Passive is null && Passives.TryParse(node.Id, out _))
            sheet = sheet with { Passive = node.Id };
        return WithSheet(state, catalog, hero, sheet);
    }

    /// <summary>Change l'équipement : ces compétences (ou variantes) apprises, dans l'ordre des boutons, et ce passif.</summary>
    public static TrainingResult Equip(CampaignState state, HeroCatalog catalog, string owner, string heroId,
        IReadOnlyList<string> equipped, string? passive)
    {
        if (Hero(state, owner, heroId) is not { } hero)
            return Refusal(state, owner, heroId);
        if (equipped is null || equipped.Any(e => e is null))
            return TrainingResult.Refused(TrainingRefusal.NotAllowed);
        return WithSheet(state, catalog, hero, hero.Sheet with { Equipped = equipped.ToList(), Passive = passive });
    }

    /// <summary>Les cases qu'il peut apprendre maintenant (points restants, paliers ouverts).</summary>
    public static IReadOnlyList<TreeNode> Learnable(HeroCatalog catalog, HeroSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sheet);
        return Academy.Learnable(catalog, sheet.Archetype, sheet.Learned, catalog.Rules.SkillPointsAt(sheet.Level));
    }

    /// <summary>Ses compétences actives apprises, chacune avec ses versions disponibles (aucune pour une compétence à venir).</summary>
    public static IReadOnlyList<EquipOption> Equippable(HeroCatalog catalog, HeroSheet sheet) =>
        Learned(catalog, sheet)
            .Where(n => !n.IsPassive)
            .Select(n => new EquipOption(n, new[] { n.Id, n.Variant }.OfType<string>().Where(catalog.Skills.ContainsKey).ToList()))
            .ToList();

    /// <summary>Ses passifs appris et disponibles (codés).</summary>
    public static IReadOnlyList<TreeNode> EquippablePassives(HeroCatalog catalog, HeroSheet sheet) =>
        Learned(catalog, sheet).Where(n => n.IsPassive && Passives.TryParse(n.Id, out _)).ToList();

    private static IEnumerable<TreeNode> Learned(HeroCatalog catalog, HeroSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sheet);
        var nodes = catalog.Trees.TryGetValue(sheet.Archetype, out var tree) ? tree.Nodes() : Array.Empty<TreeNode>();
        return sheet.Learned.Select(id => nodes.FirstOrDefault(n => n.Id == id)).OfType<TreeNode>();
    }

    /// <summary>Le héros vivant de ce joueur, ou <c>null</c>.</summary>
    private static CampaignHero? Hero(CampaignState state, string owner, string heroId) =>
        state.Heroes.FirstOrDefault(h => h.Id == heroId && h.Owner == owner && !h.IsDead);

    private static TrainingResult Refusal(CampaignState state, string owner, string heroId) =>
        TrainingResult.Refused(state.Heroes.Any(h => h.Id == heroId && h.Owner == owner)
            ? TrainingRefusal.HeroDead
            : TrainingRefusal.NotYourHero);

    /// <summary>La campagne avec cette nouvelle fiche, si les règles de progression l'acceptent.</summary>
    private static TrainingResult WithSheet(CampaignState state, HeroCatalog catalog, CampaignHero hero, HeroSheet sheet)
    {
        try
        {
            catalog.Check(sheet);
        }
        catch (ArgumentException)
        {
            return TrainingResult.Refused(TrainingRefusal.NotAllowed);
        }
        return new TrainingResult(state with { Heroes = state.Heroes.Select(h => h.Id == hero.Id ? h with { Sheet = sheet } : h).ToList() }, null);
    }
}
