namespace Game.Core.Combat;

/// <summary>Mouvement spécial du lanceur pendant sa compétence (sans attaque d'opportunité).</summary>
public enum SelfMoveType
{
    /// <summary>
    /// Percée : vise une case alignée ; course en ligne droite sur <see cref="SkillDefinition.Range"/> cases à travers
    /// les unités (pas les murs ni les obstacles bas), arrêt sur la dernière case libre, et frappe tout ce qui est traversé.
    /// </summary>
    Pierce,

    /// <summary>Saut : vise une case libre à portée, par-dessus unités et obstacles bas (pas les murs) ; la zone frappe autour de l'atterrissage.</summary>
    Leap,

    /// <summary>Charge : vise un ennemi aligné à portée ; fonce et s'arrête contre la première unité de la ligne, qui prend le coup.</summary>
    Charge,

    /// <summary>
    /// Ruée : vise un ennemi ; le lanceur court (à pied, <see cref="SkillDefinition.Range"/> pas au plus) jusqu'à la
    /// case libre la plus proche au contact de sa cible, puis la frappe.
    /// </summary>
    Dash,

    /// <summary>
    /// Bond : vise un ennemi ; le lanceur saute (comme un Saut : à portée, par-dessus unités et obstacles bas) sur la
    /// case libre la plus proche au contact de sa cible, et ne frappe qu'elle.
    /// </summary>
    Pounce,

    /// <summary>
    /// Repli : vise un ennemi au contact, puis une case ; le lanceur frappe, puis rejoint la case à pied
    /// (<see cref="SelfMoveOptions.RetreatDistance"/> pas au plus).
    /// </summary>
    Retreat,
}
