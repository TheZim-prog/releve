using Game.Core.Grids;

namespace Game.Core.Scripts;

/// <summary>
/// Une action imposée à un héros par le script (tutoriel) : seule l'activation qui la respecte est acceptée. Le
/// déplacement peut être libre : sans <see cref="MoveTo"/>, le héros se place où il veut avant d'agir ; sans
/// <see cref="Skill"/>, il doit seulement se déplacer (ni compétence ni interaction). La visée de la compétence, elle,
/// est imposée : <see cref="Target"/> et/ou <see cref="Cell"/>, comme la compétence le demande.
/// </summary>
public sealed record ImposedAction
{
    /// <summary>Repère du héros (<c>tag</c> d'une unité du scénario).</summary>
    public required string Unit { get; init; }

    /// <summary>Compétence à utiliser (une compétence du héros), ou <c>null</c> pour un déplacement seul.</summary>
    public string? Skill { get; init; }

    /// <summary>Repère de la cible de la compétence (attaque ciblée).</summary>
    public string? Target { get; init; }

    /// <summary>Case visée par la compétence (compétence à zone).</summary>
    public Cell? Cell { get; init; }

    /// <summary>Case d'arrivée imposée, ou <c>null</c> : déplacement libre (ou aucun).</summary>
    public Cell? MoveTo { get; init; }
}
