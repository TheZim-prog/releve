namespace Game.Core.Scenarios;

/// <summary>
/// Une vague de renforts : des unités (pas des héros) qui arrivent sur la carte <see cref="AtMs"/> ms après le début du
/// combat, chacune sur sa case ou, si elle est prise, sur la case libre la plus proche.
/// </summary>
public sealed record ScenarioWave
{
    public required int AtMs { get; init; }
    public required IReadOnlyList<ScenarioUnit> Units { get; init; }

    /// <summary>
    /// La forme de la vague : au moins une unité, chacune un type d'unité (pas un héros) dans un camp positif. Le reste
    /// (heure, cases, rôles) se vérifie avec les objectifs, sur l'état initial du combat.
    /// </summary>
    internal void Validate(string scenarioId)
    {
        if (Units is not { Count: > 0 } || Units.Any(u => u is null))
            throw new ArgumentException($"Scenario '{scenarioId}': a wave needs units, without null entries.");
        if (Units.Any(u => u.Hero is not null || string.IsNullOrWhiteSpace(u.Unit) || u.Team < 0))
            throw new ArgumentException($"Scenario '{scenarioId}': a wave brings units (not heroes), in a team that is not negative.");
    }
}
