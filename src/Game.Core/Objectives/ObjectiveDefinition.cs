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

    /// <summary>Objectifs d'un scénario qui n'en décrit pas : éliminer tous les ennemis.</summary>
    public static IReadOnlyList<ObjectiveDefinition> Default { get; } =
        Array.AsReadOnly(new[] { new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate } });

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentException($"Unknown objective kind {Kind}.");
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
