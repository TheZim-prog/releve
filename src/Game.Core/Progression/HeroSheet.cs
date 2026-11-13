using Game.Core.Combat;

namespace Game.Core.Progression;

/// <summary>
/// Fiche d'un héros : son archétype, son niveau et ce qu'il en a fait (docs/design.md 2.6 et 2.7). Les points de stats
/// et les compétences achetées sont définitifs ; l'équipement se choisit avant chaque mission.
/// </summary>
public sealed record HeroSheet
{
    public required string Id { get; init; }

    /// <summary>Identifiant du type d'unité de son archétype (ex. « colossus »).</summary>
    public required string Archetype { get; init; }

    public required int Level { get; init; }

    public StatPoints Points { get; init; } = StatPoints.None;

    /// <summary>Cases de l'arbre achetées (compétences actives et passifs), une par point de compétence.</summary>
    public IReadOnlyList<string> Learned { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Compétences actives équipées, dans l'ordre des boutons : pour chacune, la compétence achetée ou sa variante.
    /// L'attaque de base n'en fait pas partie : elle est toujours là.
    /// </summary>
    public IReadOnlyList<string> Equipped { get; init; } = Array.Empty<string>();

    /// <summary>Passif équipé parmi ceux achetés, ou <c>null</c>.</summary>
    public string? Passive { get; init; }

    public static IReadOnlyList<HeroSheet> ListFromJson(string json) =>
        DataJson.ValidateList(DataJson.Deserialize<List<HeroSheet?>>(json), _ => { }, h => h.Id, "hero");
}
