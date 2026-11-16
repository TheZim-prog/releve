namespace Game.Core.Combat;

/// <summary>Contre quelles attaques ciblées une garde protège.</summary>
public enum GuardAgainst
{
    /// <summary>Les attaques au contact (ex. Parade).</summary>
    Melee,

    /// <summary>Les attaques à distance (ex. Déviation).</summary>
    Ranged,
}

/// <summary>
/// Garde que la compétence donne à son lanceur (données) : elle bloque une attaque ciblée qui le vise (pas une zone).
/// Sans durée, elle bloque la prochaine et tombe alors, ou à la prochaine action du lanceur ; avec une durée (ex. Maître
/// d'armes), elle tient tout ce temps.
/// </summary>
public sealed record GuardDefinition
{
    public required GuardAgainst Against { get; init; }

    /// <summary>Durée en millisecondes de combat ; 0 = jusqu'à la prochaine attaque bloquée ou la prochaine action.</summary>
    public int DurationMs { get; init; }

    /// <summary>Elle bloque toutes les attaques de son genre tant qu'elle dure, au lieu de tomber à la première.</summary>
    public bool BlocksAll { get; init; }

    /// <summary>Le lanceur riposte (attaque de base) à chaque attaque qu'elle bloque.</summary>
    public bool Ripostes { get; init; }

    public void Validate(string skillId)
    {
        if (!Enum.IsDefined(Against))
            throw new ArgumentException($"Skill '{skillId}' has an unknown guard kind.");
        if (DurationMs < 0 || (BlocksAll && DurationMs == 0))
            throw new ArgumentException($"Skill '{skillId}': a guard that blocks everything needs a duration (and no guard lasts less than 0).");
        if (Ripostes && Against != GuardAgainst.Melee)
            throw new ArgumentException($"Skill '{skillId}': only a guard against melee attacks can riposte.");
    }
}
