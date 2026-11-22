using Game.Core.Progression;

namespace Game.Core.Campaign;

/// <summary>
/// Ce qu'une quête terminée rapporte (docs/design.md 4.6 et 4.9) : à chaque héros qui y est parti, toute l'XP de la
/// quête (une part seulement si elle est perdue), avec les niveaux qu'elle fait gagner ; au pot commun, ses matériaux,
/// si elle est gagnée. Les points de stats et de compétence des niveaux gagnés attendent d'être dépensés à l'académie.
/// </summary>
public static class QuestRewards
{
    /// <summary>L'XP que reçoit chaque héros de la quête.</summary>
    public static int XpFor(QuestDefinition quest, bool victory, ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(rules);
        return victory ? quest.Xp : quest.Xp * rules.DefeatXpPercent / 100;
    }

    /// <summary>La campagne après la quête : XP et niveaux de ses héros (vivants), matériaux au pot si elle est gagnée.</summary>
    public static CampaignState Grant(CampaignState state, QuestDefinition quest, IReadOnlyCollection<string> heroIds, bool victory,
        ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(heroIds);
        var xp = XpFor(quest, victory, rules);
        var heroes = state.Heroes
            .Select(h => heroIds.Contains(h.Id) && !h.IsDead ? WithXp(h, h.Xp + xp, rules) : h)
            .ToList();
        var materials = new Dictionary<string, int>(state.Materials);
        if (victory)
        {
            foreach (var (material, amount) in quest.Materials)
                materials[material] = materials.GetValueOrDefault(material) + amount;
        }
        return state with { Heroes = heroes, Materials = materials };
    }

    /// <summary>Le héros avec cette XP totale, et le niveau qu'elle donne (un niveau ne se perd jamais).</summary>
    private static CampaignHero WithXp(CampaignHero hero, int xp, ProgressionRules rules) =>
        hero with { Xp = xp, Sheet = hero.Sheet with { Level = Math.Max(hero.Sheet.Level, rules.LevelFor(xp)) } };

    /// <summary>Points de stats gagnés en montant de niveau et pas encore dépensés.</summary>
    public static int UnspentStatPoints(HeroSheet sheet, ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rules);
        return rules.StatPointsAt(sheet.Level) - (sheet.Points.Hp + sheet.Points.Power + sheet.Points.Initiative);
    }

    /// <summary>Points de compétence pas encore dépensés dans l'arbre.</summary>
    public static int UnspentSkillPoints(HeroSheet sheet, ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rules);
        return rules.SkillPointsAt(sheet.Level) - sheet.Learned.Count;
    }
}
