using Game.Core.Grids;

namespace Game.Core.Scripts;

/// <summary>
/// Une action décrite par le script, pour une unité désignée par son repère : imposée à un héros (effet
/// <see cref="ScriptEffect.Impose"/>, seule l'activation qui la respecte est acceptée) ou ordonnée à une unité du jeu
/// (effet <see cref="ScriptEffect.Act"/>, elle la fait aussitôt). Le déplacement peut être libre : sans
/// <see cref="MoveTo"/>, l'unité se place où il faut avant d'agir (le héros, où il veut) ; sans <see cref="Skill"/>, elle
/// doit seulement se déplacer. La visée de la compétence, elle, est donnée : <see cref="Target"/> et/ou
/// <see cref="Cell"/>, comme la compétence le demande.
/// </summary>
public sealed record ScriptAction
{
    /// <summary>Repère de l'unité qui agit (<c>tag</c> d'une unité du scénario).</summary>
    public required string Unit { get; init; }

    /// <summary>Compétence à utiliser (une compétence de l'unité), ou <c>null</c> pour un déplacement seul.</summary>
    public string? Skill { get; init; }

    /// <summary>Repère de la cible de la compétence.</summary>
    public string? Target { get; init; }

    /// <summary>Case visée par la compétence (zone, ou destination d'une télékinésie).</summary>
    public Cell? Cell { get; init; }

    /// <summary>Case d'arrivée imposée, ou <c>null</c> : déplacement libre (ou aucun).</summary>
    public Cell? MoveTo { get; init; }
}
