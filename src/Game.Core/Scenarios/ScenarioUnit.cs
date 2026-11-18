using Game.Core.Combat;

namespace Game.Core.Scenarios;

/// <summary>
/// Une unité placée par un scénario, sur une case et dans une équipe : soit un type d'unité tel quel
/// (<see cref="Unit"/> : ennemi, civil…), soit un héros prêt à jouer (<see cref="Hero"/> : sa fiche donne son niveau,
/// ses points de stats et son équipement). Exactement l'un des deux.
/// </summary>
public sealed record ScenarioUnit
{
    /// <summary>Identifiant du type d'unité (data/units).</summary>
    public string? Unit { get; init; }

    /// <summary>Identifiant du héros (data/heroes).</summary>
    public string? Hero { get; init; }

    /// <summary>
    /// Chef de son camp (une unité ennemie, ni héros ni civil ; un seul par camp) : ce que font ses sbires quand il tombe
    /// (<c>"flee"</c> : ils fuient, <c>"surrender"</c> : ils se rendent).
    /// </summary>
    public MinionReaction? Leader { get; init; }

    /// <summary>Vilain poursuivi (une unité ennemie) : il fuit dès le début vers les sorties de l'objectif Poursuite.</summary>
    public bool Pursued { get; init; }

    /// <summary>
    /// Personne à protéger (objectif Protection) : un allié du camp des joueurs commandé par le jeu (une unité, pas un
    /// héros) ; s'il tombe, la protection est ratée.
    /// </summary>
    public bool Protected { get; init; }

    /// <summary>Repère de l'unité pour le script de mission (unique dans le scénario, vagues comprises).</summary>
    public string? Tag { get; init; }

    /// <summary>
    /// Comportement d'IA à la place de celui de son type, ou <c>null</c>. Un héros qui en a un n'est pas jouable : il est
    /// commandé par le jeu (<c>"scripted"</c> : il n'agit que sur ordre du script).
    /// </summary>
    public string? Behavior { get; init; }

    public required int Team { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>Ce qui est placé, pour les messages d'erreur.</summary>
    internal string Label => Hero is not null ? $"hero '{Hero}'" : $"unit '{Unit}'";
}
