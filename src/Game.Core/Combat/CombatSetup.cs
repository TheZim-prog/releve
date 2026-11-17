using Game.Core.Grids;
using Game.Core.Objectives;

namespace Game.Core.Combat;

/// <summary>
/// État initial d'un combat. Avec le journal des commandes, il suffit à rejouer le combat à l'identique.
/// Les listes (unités, objectifs) sont copiées : les modifier ensuite côté appelant ne change pas ce setup.
/// </summary>
/// <param name="Seed">Graine du générateur aléatoire du combat.</param>
public sealed record CombatSetup(CombatRules Rules, Grid Grid, IReadOnlyList<UnitSetup> Units, ulong Seed)
{
    private readonly IReadOnlyList<UnitSetup> _units = ReadOnlyCopy(Units);
    private readonly IReadOnlyList<ObjectiveDefinition> _objectives = ObjectiveDefinition.Default;

    public IReadOnlyList<UnitSetup> Units
    {
        get => _units;
        init => _units = ReadOnlyCopy(value);
    }

    /// <summary>
    /// Objectifs du combat (par défaut : éliminer tous les ennemis). Sans aucun objectif (tests, outils), le combat ne se
    /// termine jamais.
    /// </summary>
    public IReadOnlyList<ObjectiveDefinition> Objectives
    {
        get => _objectives;
        init => _objectives = ReadOnlyCopy(value);
    }

    private readonly IReadOnlyList<WaveSetup> _waves = Array.Empty<WaveSetup>();

    /// <summary>Renforts qui arrivent en cours de combat (aucun par défaut).</summary>
    public IReadOnlyList<WaveSetup> Waves
    {
        get => _waves;
        init => _waves = ReadOnlyCopy(value);
    }

    /// <summary>Objectifs valides et réalisables avec cette carte et ces unités (cf. <see cref="MissionValidator"/>).</summary>
    public void ValidateObjectives() => MissionValidator.Validate(this);

    internal static IReadOnlyList<T> ReadOnlyCopy<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Array.AsReadOnly(items.ToArray());
    }
}
