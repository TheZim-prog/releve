using Game.Core.Combat;
using Game.Core.Objectives;

namespace Game.Core.World;

/// <summary>
/// Ce qu'ajoute à une mission chaque niveau de gravité, donc chaque crochet en route (docs/design.md 4.2) : des ennemis
/// arrivés en renfort, des civils déjà perdus, un compte à rebours plus avancé. Une règle par type d'objectif (data/world/
/// rules.json), qu'une quête peut remplacer par la sienne.
/// </summary>
public sealed record GravityStep
{
    public static readonly GravityStep None = new();

    /// <summary>Types d'unités ennemies arrivées en renfort, à chaque niveau.</summary>
    public IReadOnlyList<string> ExtraUnits { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Civils déjà perdus à chaque niveau : jamais sous le minimum d'un sauvetage (il reste jouable), et jamais un civil
    /// que le scénario désigne (repéré pour son script, ou à protéger).
    /// </summary>
    public int CiviliansLost { get; init; }

    /// <summary>Millisecondes de moins à chaque compte à rebours (pas une protection à tenir), à chaque niveau.</summary>
    public int CountdownLessMs { get; init; }

    /// <summary>Un compte à rebours ne descend jamais sous ce % de sa durée.</summary>
    public int CountdownFloorPercent { get; init; } = 25;

    public void Validate(IReadOnlyDictionary<string, UnitDefinition> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (ExtraUnits is null || CiviliansLost < 0 || CountdownLessMs < 0 || CountdownFloorPercent is < 1 or > 100)
            throw new ArgumentException("A gravity step cannot be negative, and keeps a countdown floor between 1 and 100 %.");
        if (ExtraUnits.FirstOrDefault(u => !units.ContainsKey(u)) is { } unknown)
            throw new ArgumentException($"A gravity step adds an unknown unit '{unknown}'.");
    }

    /// <summary>
    /// Le combat, aggravé de <paramref name="level"/> niveaux : les renforts arrivent dès le début, comme une vague, au
    /// plus près à pied du premier ennemi ; les civils perdus sont les derniers du scénario ; les comptes à rebours
    /// raccourcissent.
    /// </summary>
    public CombatSetup Apply(CombatSetup setup, int level, IReadOnlyDictionary<string, UnitDefinition> units)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(units);
        if (level <= 0 || this == None)
            return setup;
        var placed = setup.Units.ToList();
        LoseCivilians(placed, setup.Objectives, CiviliansLost * level);
        var objectives = setup.Objectives
            .Select(o => o is { Kind: ObjectiveKind.Countdown, DurationMs: { } ms }
                ? o with { DurationMs = Math.Max(ms * CountdownFloorPercent / 100, ms - CountdownLessMs * level) }
                : o)
            .ToList();
        return setup with { Units = placed, Objectives = objectives, Waves = setup.Waves.Concat(Reinforcements(placed, level, units)).ToList() };
    }

    private static void LoseCivilians(List<UnitSetup> placed, IReadOnlyList<ObjectiveDefinition> objectives, int lost)
    {
        var keep = objectives.Where(o => o.Kind == ObjectiveKind.Rescue).Select(o => o.Minimum ?? 0).DefaultIfEmpty(0).Max();
        var civilians = placed.Where(u => u.Definition.IsCivilian).ToList();
        var removable = civilians.Where(u => u.Tag is null && !u.IsProtected).Reverse();
        foreach (var civilian in removable.Take(Math.Max(0, Math.Min(lost, civilians.Count - keep))))
            placed.Remove(civilian);
    }

    /// <summary>
    /// Les renforts : une vague dès le début du combat (1 ms, le premier pas de la simulation), posée sur le premier ennemi (chacun arrive sur la case libre la
    /// plus proche à pied, jamais derrière un mur). Aucun sans ennemi pour les placer.
    /// </summary>
    /// <summary>Le plus tôt qu'une vague puisse arriver (le validateur de mission refuse 0).</summary>
    private const int FirstMoment = 1;

    private IEnumerable<WaveSetup> Reinforcements(IReadOnlyList<UnitSetup> placed, int level, IReadOnlyDictionary<string, UnitDefinition> units)
    {
        if (ExtraUnits.Count == 0 || placed.FirstOrDefault(u => u.Team == 1) is not { } anchor)
            yield break;
        var arriving = Enumerable.Repeat(ExtraUnits, level).SelectMany(u => u)
            .Select(unit => units[unit].ToSetup(default, 1, anchor.Position))
            .ToList();
        yield return new WaveSetup(FirstMoment, arriving);
    }
}
