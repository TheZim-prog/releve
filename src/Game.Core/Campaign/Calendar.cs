using Game.Core.Progression;

namespace Game.Core.Campaign;

/// <summary>
/// Le temps de la campagne (docs/design.md 4.7) : chaque joueur a sa propre date, en heures depuis le début
/// (<see cref="CampaignPlayer.Hour"/>). Une quête la fait avancer pour ceux qui y partent : le groupe part à la date la
/// plus avancée de ses membres (les autres « sautent » jusqu'à elle), puis chacun avance de sa durée. Pendant que son
/// joueur avance, un héros qui reste au QG se repose : il récupère une chute tous les
/// <see cref="ProgressionRules.DaysToRecoverFall"/> jours (cf. 1.4).
/// </summary>
public static class Calendar
{
    public const int HoursPerDay = 24;

    /// <summary>La plus petite durée d'une quête, et son pas.</summary>
    public const int HalfDay = HoursPerDay / 2;

    /// <summary>Le jour d'une date (0 : le premier jour).</summary>
    public static int DayOf(int hour) => hour / HoursPerDay;

    /// <summary>
    /// Le panneau tel que ce joueur le voit : les quêtes parues à sa date, pas encore jouées ni prises par un groupe
    /// (<paramref name="taken"/>), dans l'ordre de leur parution puis de leur nom.
    /// </summary>
    public static IReadOnlyList<QuestDefinition> Board(CampaignState state, IEnumerable<QuestDefinition> quests, string uid,
        IReadOnlyCollection<string>? taken = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(quests);
        var day = DayOf(state.PlayerOf(uid)?.Hour ?? 0);
        return quests
            .Where(q => q.PublishedDay <= day && !state.DoneQuests.Contains(q.Id) && taken?.Contains(q.Id) != true)
            .OrderBy(q => q.PublishedDay).ThenBy(q => q.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Le groupe de ces joueurs part pour cette quête : chacun saute à la date la plus avancée d'entre eux, et jamais avant
    /// le jour où elle paraît (ses héros se reposent en attendant).
    /// </summary>
    public static CampaignState Depart(CampaignState state, IReadOnlyCollection<string> uids, QuestDefinition quest, ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(uids);
        ArgumentNullException.ThrowIfNull(quest);
        var date = state.Players.Where(p => uids.Contains(p.Uid)).Select(p => p.Hour).Append(quest.PublishedDay * HoursPerDay).Max();
        return uids.Aggregate(state, (s, uid) => PassTime(s, uid, date - (s.PlayerOf(uid)?.Hour ?? date), Array.Empty<string>(), rules));
    }

    /// <summary>
    /// Le groupe rentre d'une quête de <paramref name="hours"/> heures : chacun avance d'autant ; ses héros partis
    /// (<paramref name="away"/>) ne se reposent pas, les autres si.
    /// </summary>
    public static CampaignState Return(CampaignState state, IReadOnlyCollection<string> uids, int hours,
        IReadOnlyCollection<string> away, ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(uids);
        return uids.Aggregate(state, (s, uid) => PassTime(s, uid, hours, away, rules));
    }

    /// <summary>
    /// Ce joueur avance de <paramref name="hours"/> heures ; ses héros vivants qui ne sont pas partis
    /// (<paramref name="away"/>) se reposent. Un repos sans chute à récupérer ne s'accumule pas.
    /// </summary>
    public static CampaignState PassTime(CampaignState state, string uid, int hours, IReadOnlyCollection<string> away,
        ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(away);
        ArgumentNullException.ThrowIfNull(rules);
        if (hours < 0)
            throw new ArgumentOutOfRangeException(nameof(hours), "Time only moves forward.");
        if (hours == 0 || state.PlayerOf(uid) is null)
            return state;
        return state with
        {
            Players = state.Players.Select(p => p.Uid == uid ? p with { Hour = p.Hour + hours } : p).ToList(),
            Heroes = state.Heroes.Select(h => h.Owner == uid && !h.IsDead && !away.Contains(h.Id) ? Rested(h, hours, rules) : h).ToList(),
        };
    }

    private static CampaignHero Rested(CampaignHero hero, int hours, ProgressionRules rules)
    {
        var perFall = rules.DaysToRecoverFall * HoursPerDay;
        var rested = hero.HoursRested + hours;
        var halves = hero.HalfFalls;
        while (halves > 0 && rested >= perFall)
        {
            halves = Math.Max(0, halves - Injuries.HalvesPerFall);
            rested -= perFall;
        }
        return hero with { HalfFalls = halves, HoursRested = halves == 0 ? 0 : rested };
    }
}
