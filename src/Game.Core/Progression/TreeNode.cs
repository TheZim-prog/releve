namespace Game.Core.Progression;

/// <summary>
/// Une case d'un arbre, à plat : ce qu'un point de compétence achète (une compétence active ou un passif), avec sa
/// place dans l'arbre.
/// </summary>
/// <param name="Id">Identifiant de la compétence active ou du passif.</param>
/// <param name="Tier">Rang du palier dans sa branche (0 pour le premier).</param>
/// <param name="Variant">Variante de la compétence active, ou <c>null</c>.</param>
public sealed record TreeNode(string Id, string BranchId, int Tier, bool IsPassive, string? Variant = null);
