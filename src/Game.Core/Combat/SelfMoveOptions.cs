namespace Game.Core.Combat;

/// <summary>Réglages d'un mouvement spécial (données) ; chacun ne vaut que pour certains types.</summary>
public sealed record SelfMoveOptions
{
    public static readonly SelfMoveOptions None = new();

    /// <summary>Ruée : après le coup, le lanceur revient sur sa case de départ (ex. Aller-retour) ; sans repousser sa cible.</summary>
    public bool ReturnsToStart { get; init; }

    /// <summary>Ruée : dégâts en plus, en % par case parcourue pendant la ruée (ex. Coup d'élan). 0 = aucun.</summary>
    public int DamagePercentPerCell { get; init; }

    /// <summary>Saut : la zone frappe autour de la case de départ au lieu de l'atterrissage (ex. Propulsion).</summary>
    public bool ZoneAtStart { get; init; }

    /// <summary>Percée : seuls les ennemis traversés sont touchés (ex. Slalom).</summary>
    public bool EnemiesOnly { get; init; }

    /// <summary>Repli : nombre de pas, au plus, entre la case du coup et la case choisie.</summary>
    public int RetreatDistance { get; init; }

    public void Validate(string skillId, SelfMoveType type)
    {
        if ((ReturnsToStart || DamagePercentPerCell != 0) && type != SelfMoveType.Dash)
            throw new ArgumentException($"Skill '{skillId}': returning to the start or damage per cell belongs to a dash.");
        if (DamagePercentPerCell is < 0 or > 1000)
            throw new ArgumentException($"Skill '{skillId}' needs a damage per cell between 0 and 1000 %.");
        if (ZoneAtStart && type != SelfMoveType.Leap)
            throw new ArgumentException($"Skill '{skillId}': only a leap has a zone, at its start or its landing.");
        if (EnemiesOnly && type != SelfMoveType.Pierce)
            throw new ArgumentException($"Skill '{skillId}': only a pierce chooses whom it hits on its way.");
        if ((type == SelfMoveType.Retreat) != (RetreatDistance > 0) || RetreatDistance < 0)
            throw new ArgumentException($"Skill '{skillId}': a retreat, and only a retreat, needs a retreat distance of at least 1.");
    }
}
