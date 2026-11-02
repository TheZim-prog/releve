using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Ordre envoyé par un joueur ou par l'IA. La simulation le valide avant de l'appliquer.</summary>
public abstract record CombatCommand(UnitId Unit);

/// <summary>Utilisation d'une compétence sur une unité.</summary>
public sealed record SkillUse(string SkillId, UnitId Target);

/// <summary>
/// Activation d'une unité à jauge pleine : un déplacement et/ou une action, en une seule commande atomique.
/// Le déplacement a lieu d'abord ; portée et cible de l'action se calculent depuis la case d'arrivée.
/// Si une partie est impossible, rien n'est appliqué.
/// </summary>
/// <param name="MoveTo">Case d'arrivée du déplacement, ou <c>null</c> pour agir sans bouger.</param>
/// <param name="Skill">Action, ou <c>null</c> pour un déplacement seul.</param>
public sealed record ActivateCommand(UnitId Unit, Cell? MoveTo, SkillUse? Skill = null) : CombatCommand(Unit);
