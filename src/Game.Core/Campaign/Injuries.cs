using Game.Core.Progression;

namespace Game.Core.Campaign;

/// <summary>
/// Les chutes des héros et leur mort différée (docs/design.md 1.4). Compté en demi-chutes : chaque fois qu'un héros
/// tombe à terre en quête, une chute (2), une demie (1) si un coéquipier le secourt avant la fin (relevé, ou exfiltré
/// par le Sauvetage éclair). À <see cref="ProgressionRules.FallsBeforeDeath"/> chutes, il meurt pour de bon. Le repos
/// les efface lentement, au fil des jours (cf. <see cref="Calendar"/>).
/// </summary>
public static class Injuries
{
    /// <summary>Une chute en demi-chutes (une chute secourue n'en vaut qu'une : la moitié, cf. docs/design.md 1.4).</summary>
    public const int HalvesPerFall = 2;

    /// <summary>Les demi-chutes qui tuent un héros.</summary>
    public static int HalfFallsToDeath(ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return HalvesPerFall * rules.FallsBeforeDeath;
    }

    /// <summary>Les demi-chutes d'une quête : ses chutes, chacune pour moitié si elle a été secourue.</summary>
    public static int HalfFallsOf(int falls, int rescuedFalls)
    {
        if (falls < 0 || rescuedFalls < 0 || rescuedFalls > falls)
            throw new ArgumentException("A hero cannot have more rescued falls than falls, nor negative ones.");
        return HalvesPerFall * (falls - rescuedFalls) + rescuedFalls;
    }

    /// <summary>
    /// La campagne après une quête : ses héros (<paramref name="halfFallsByHero"/>, même sans chute) prennent leurs
    /// chutes, et meurent s'ils en ont trop ; partis, leur repos repart de zéro.
    /// </summary>
    public static CampaignState Apply(CampaignState state, IReadOnlyDictionary<string, int> halfFallsByHero, ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(halfFallsByHero);
        ArgumentNullException.ThrowIfNull(rules);
        var heroes = state.Heroes.Select(hero =>
            !hero.IsDead && halfFallsByHero.TryGetValue(hero.Id, out var halves) ? Fallen(hero, halves, rules) with { HoursRested = 0 } : hero);
        return state with { Heroes = heroes.ToList() };
    }

    /// <summary>Des héros blessés en route (un événement) : leurs demi-chutes s'ajoutent, et peuvent les tuer.</summary>
    public static CampaignState Hurt(CampaignState state, IReadOnlyCollection<string> heroIds, int halves, ProgressionRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(heroIds);
        ArgumentNullException.ThrowIfNull(rules);
        if (halves <= 0)
            return state;
        return state with
        {
            Heroes = state.Heroes.Select(h => heroIds.Contains(h.Id) && !h.IsDead ? Fallen(h, halves, rules) : h).ToList(),
        };
    }

    private static CampaignHero Fallen(CampaignHero hero, int halves, ProgressionRules rules)
    {
        var total = hero.HalfFalls + halves;
        return hero with { HalfFalls = total, IsDead = total >= HalfFallsToDeath(rules) };
    }
}
