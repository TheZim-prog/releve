namespace Game.Core.Combat;

/// <summary>Réactions limitées à une par cycle (rechargées quand l'unité rejoue), déjà utilisées.</summary>
[Flags]
public enum CycleReactions
{
    None = 0,

    /// <summary>Garde du corps : une attaque prise à la place d'un allié.</summary>
    Bodyguard = 1,

    /// <summary>Réflexes : une attaque ciblée esquivée.</summary>
    Reflexes = 2,

    /// <summary>Riposte : une contre-attaque.</summary>
    Riposte = 4,
}
