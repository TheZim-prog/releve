namespace Game.Core.World;

/// <summary>Ce qu'un test regarde chez les héros déployés (la mobilité ne s'achète pas, mais compte ici).</summary>
public enum TestStat
{
    Hp,
    Power,
    Initiative,
    Mobility,
}

/// <summary>
/// Un test (docs/design.md 4.2b) : le meilleur héros déployé dans <see cref="Stat"/> le tente. Sa chance de réussite, en
/// % : <see cref="BaseChance"/>, plus <see cref="PercentPerUnit"/> par unité de sa stat au-dessus de
/// <see cref="Threshold"/> (moins en dessous), plus le bonus de son archétype ; ramenée entre les bornes des règles. Le
/// joueur n'en voit que le libellé (très facile → très difficile).
/// </summary>
public sealed record EventTest
{
    public required TestStat Stat { get; init; }

    /// <summary>La valeur de la stat (PV max, puissance en %, initiative, mobilité) qui donne la chance de base.</summary>
    public required int Threshold { get; init; }

    public required int BaseChance { get; init; }

    public required int PercentPerUnit { get; init; }

    /// <summary>Bonus de chance (en points de %) par archétype, ex. { "colossus": 25 }.</summary>
    public IReadOnlyDictionary<string, int> ArchetypeBonus { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Les effets d'un choix ou de l'issue d'un test : matériaux au pot (ou en moins), réputation, demi-chutes (au héros qui
/// a tenté le test ; sans test, à chaque héros déployé), et un bonus ou malus pour la quête en cours.
/// </summary>
public sealed record EventEffects
{
    public static readonly EventEffects None = new();

    public IReadOnlyDictionary<string, int> Materials { get; init; } = new Dictionary<string, int>();
    public int Reputation { get; init; }
    public int HalfFalls { get; init; }
    public QuestModifiers Quest { get; init; } = QuestModifiers.None;
}

/// <summary>
/// Une option d'un événement : son libellé, et ses effets ; avec un test, <see cref="Success"/> s'il est réussi,
/// <see cref="Failure"/> sinon. Une option peut être réservée à certains archétypes (au moins un héros déployé).
/// </summary>
public sealed record EventOption
{
    public required string Label { get; init; }
    public EventTest? Test { get; init; }
    public EventEffects Success { get; init; } = EventEffects.None;
    public EventEffects Failure { get; init; } = EventEffects.None;
    public IReadOnlyList<string> RequiresArchetype { get; init; } = Array.Empty<string>();
}

/// <summary>
/// L'événement d'un point d'intérêt (docs/design.md 4) : une scène, ses options. Passer son chemin est toujours possible
/// et ne coûte rien (ce n'est pas une option écrite ici).
/// </summary>
public sealed record WorldEvent
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required IReadOnlyList<EventOption> Options { get; init; }

    public static IReadOnlyList<WorldEvent> ListFromJson(string json) =>
        DataJson.ValidateList(DataJson.Deserialize<List<WorldEvent?>>(json), e => e.Validate(), e => e.Id, "event");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Text))
            throw new ArgumentException("An event needs an id, a title and a text.");
        if (Options is not { Count: > 0 } || Options.Any(o => o is null || string.IsNullOrWhiteSpace(o.Label)))
            throw new ArgumentException($"Event '{Id}' needs options, each with a label.");
        foreach (var option in Options)
        {
            if (option.Success is null || option.Failure is null || option.RequiresArchetype is null
                || new[] { option.Success, option.Failure }.Any(e => e.Materials is null || e.Quest is null || e.HalfFalls < 0))
                throw new ArgumentException($"Event '{Id}': option '{option.Label}' has missing (null) or negative-fall effects.");
            option.Success.Quest.Validate();
            option.Failure.Quest.Validate();
            if (option.Test is { } test && (!Enum.IsDefined(test.Stat) || test.PercentPerUnit < 0 || test.ArchetypeBonus is null))
                throw new ArgumentException($"Event '{Id}': option '{option.Label}' has an invalid test.");
            if (option.Test is null && option.Failure != EventEffects.None)
                throw new ArgumentException($"Event '{Id}': option '{option.Label}' has failure effects but no test.");
        }
    }
}
