using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Ordre envoyé par un joueur ou par l'IA. La simulation le valide avant de l'appliquer.</summary>
public abstract record CombatCommand(UnitId Unit);

/// <summary>
/// Activation d'une unité à jauge pleine : un déplacement et/ou une action, en une seule commande atomique.
/// Pour l'instant, seul le déplacement existe.
/// </summary>
/// <param name="MoveTo">Case d'arrivée du déplacement, ou <c>null</c> pour agir sans bouger.</param>
public sealed record ActivateCommand(UnitId Unit, Cell? MoveTo) : CombatCommand(Unit);
