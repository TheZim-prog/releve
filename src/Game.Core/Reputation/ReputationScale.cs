namespace Game.Core.Reputation;

/// <summary>
/// L'échelle de la réputation du groupe (docs/design.md 3.4 et 4.4) : une valeur bornée, lue en paliers nommés, qui
/// cumule les bilans des missions. En données (<c>reputation.json</c>, « scale »).
/// </summary>
public sealed record ReputationScale
{
    public required int Min { get; init; }

    public required int Max { get; init; }

    /// <summary>La réputation d'une nouvelle campagne.</summary>
    public required int Start { get; init; }

    /// <summary>Les paliers, du plus bas au plus haut : chacun commence à sa valeur <see cref="ReputationTier.From"/>.</summary>
    public required IReadOnlyList<ReputationTier> Tiers { get; init; }

    /// <summary>La valeur ramenée dans les bornes.</summary>
    public int Clamp(int value) => Math.Clamp(value, Min, Max);

    /// <summary>Le palier où tombe cette valeur (le dernier dont le seuil est atteint).</summary>
    public ReputationTier TierOf(int value) => Tiers.Last(t => t.From <= Clamp(value));

    public void Validate()
    {
        if (Min >= Max || Start < Min || Start > Max)
            throw new ArgumentException("The reputation scale needs min < max, and its start between them.");
        if (Tiers is not { Count: > 0 } || Tiers.Any(t => t is null || string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Name)))
            throw new ArgumentException("The reputation scale needs tiers, each with an id and a name.");
        if (Tiers[0].From != Min)
            throw new ArgumentException("The first reputation tier must start at the scale's min.");
        for (var i = 1; i < Tiers.Count; i++)
        {
            if (Tiers[i].From <= Tiers[i - 1].From || Tiers[i].From > Max)
                throw new ArgumentException("Reputation tiers must start in increasing order, within the scale.");
        }
        if (Tiers.Select(t => t.Id).Distinct().Count() != Tiers.Count)
            throw new ArgumentException("Reputation tier ids must be unique.");
    }
}

/// <summary>Un palier de réputation : son identifiant (anglais), son nom affiché, et la valeur où il commence.</summary>
public sealed record ReputationTier(string Id, string Name, int From);
