using Game.Core.Grids;

namespace Game.Core.Objectives;

/// <summary>
/// Objectif de mission, décrit par un scénario (docs/design.md 3.3). Les objectifs <b>principaux</b> décident de l'issue
/// du combat ; les <b>secondaires</b> n'alimentent que le bilan. Quels que soient les objectifs, la chute de tous les
/// héros est une défaite.
/// </summary>
public sealed record ObjectiveDefinition
{
    public required ObjectiveKind Kind { get; init; }

    /// <summary>Secondaire : compte pour le bilan (récompenses, réputation), sans décider de la victoire.</summary>
    public bool Secondary { get; init; }

    /// <summary>Sauvetage : nombre minimum de civils à évacuer (au moins 1). Les autres types n'en ont pas.</summary>
    public int? Minimum { get; init; }

    /// <summary>Poursuite : les cases par où le vilain poursuivi quitte le combat (au moins une). Les autres types n'en ont pas.</summary>
    public IReadOnlyList<Cell>? Exits { get; init; }

    /// <summary>Course contre la montre : les objets à neutraliser (au moins un). Les autres types n'en ont pas.</summary>
    public IReadOnlyList<DeviceDefinition>? Devices { get; init; }

    /// <summary>Course contre la montre : durée du compte à rebours, en ms. Les autres types n'en ont pas.</summary>
    public int? DurationMs { get; init; }

    /// <summary>Objectifs d'un scénario qui n'en décrit pas : éliminer tous les ennemis.</summary>
    public static IReadOnlyList<ObjectiveDefinition> Default { get; } =
        Array.AsReadOnly(new[] { new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate } });

    /// <summary>Chaque champ propre à un type est présent pour ce type, absent pour les autres, et sa valeur a un sens.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentException($"Unknown objective kind {Kind}.");
        ExpectField(Minimum is not null, ObjectiveKind.Rescue, "minimum");
        ExpectField(Exits is not null, ObjectiveKind.Pursuit, "exits");
        ExpectField(Devices is not null, ObjectiveKind.Countdown, "devices");
        ExpectField(DurationMs is not null, ObjectiveKind.Countdown, "durationMs");

        if (Minimum < 1)
            throw new ArgumentException("A rescue objective needs a minimum of evacuated civilians (at least 1).");
        if (Exits is { Count: 0 })
            throw new ArgumentException("A pursuit objective needs at least one exit cell.");
        if (Devices is { Count: 0 } || Devices?.Any(d => d is null) == true)
            throw new ArgumentException("A countdown objective needs at least one device, without null entries.");
        foreach (var device in Devices ?? Array.Empty<DeviceDefinition>())
            device.Validate();
        if (DurationMs < 1)
            throw new ArgumentException("A countdown needs a duration of at least 1 ms.");
    }

    /// <summary>Le champ <paramref name="field"/> va avec le type <paramref name="owner"/> : obligatoire pour lui, interdit aux autres.</summary>
    private void ExpectField(bool present, ObjectiveKind owner, string field)
    {
        if (present != (Kind == owner))
            throw new ArgumentException(present
                ? $"Only a {owner} objective takes '{field}' (not {Kind})."
                : $"A {owner} objective needs '{field}'.");
    }

    /// <summary>
    /// Une liste d'objectifs : sans entrée nulle, chacun valide, et au moins un principal s'il y en a. Une liste vide
    /// (tests, outils) donne un combat qui ne se termine jamais ; un scénario, lui, en exige (<see cref="ValidateMission"/>).
    /// </summary>
    public static void ValidateList(IReadOnlyList<ObjectiveDefinition> objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        foreach (var objective in objectives)
        {
            if (objective is null)
                throw new ArgumentException("The objective list contains a null entry.");
            objective.Validate();
        }
        if (objectives.Count > 0 && objectives.All(o => o.Secondary))
            throw new ArgumentException("A combat needs at least one main (not secondary) objective.");
    }

    /// <summary>Les objectifs d'une mission : comme <see cref="ValidateList"/>, mais au moins un.</summary>
    public static void ValidateMission(IReadOnlyList<ObjectiveDefinition> objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        if (objectives.Count == 0)
            throw new ArgumentException("A mission needs at least one objective (leave the list out to eliminate every enemy).");
        ValidateList(objectives);
    }
}
