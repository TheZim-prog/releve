using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Ordre envoyé par un joueur ou par l'IA. La simulation le valide avant de l'appliquer.</summary>
public abstract record CombatCommand(UnitId Unit);

/// <summary>
/// Utilisation d'une compétence : sur une unité (<paramref name="Target"/>) pour une attaque ciblée,
/// ou sur une case (<paramref name="Cell"/>) pour une compétence à zone.
/// </summary>
public sealed record SkillUse(string SkillId, UnitId? Target, Cell? Cell = null);

/// <summary>
/// Résultat d'une prévisualisation d'action (<see cref="CombatSimulation.PreviewAction"/>) : refusée
/// (<paramref name="Reason"/>), ou ce qu'elle viserait : une unité, des cases (zone, course), une case (arrivée,
/// dépôt, gadget).
/// </summary>
public sealed record ActionPreview(RejectionReason? Reason, UnitId? Target = null, IReadOnlyList<Cell>? Cells = null,
    Cell? Destination = null)
{
    public bool IsValid => Reason is null;
}

/// <summary>Ce que permet l'action « interagir » (commune à tous les héros, légère, au contact).</summary>
public enum InteractionKind
{
    /// <summary>Relever un allié à terre : il se remet debout avec une part de ses PV, jauge vide.</summary>
    Revive,
}

/// <summary>Interaction avec une unité au contact (<paramref name="Target"/>).</summary>
public sealed record InteractionUse(InteractionKind Kind, UnitId Target);

/// <summary>
/// Activation d'une unité à jauge pleine : un déplacement et/ou une action, en une seule commande atomique.
/// Le déplacement a lieu d'abord ; portée et cible de l'action se calculent depuis la case d'arrivée.
/// Si une partie est impossible, rien n'est appliqué.
/// </summary>
/// <param name="MoveTo">Case d'arrivée du déplacement, ou <c>null</c> pour agir sans bouger.</param>
/// <param name="Skill">Action (compétence), ou <c>null</c>.</param>
/// <param name="Interaction">Action « interagir », ou <c>null</c> ; une seule action par activation.</param>
public sealed record ActivateCommand(UnitId Unit, Cell? MoveTo, SkillUse? Skill = null, InteractionUse? Interaction = null)
    : CombatCommand(Unit);
