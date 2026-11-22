namespace Game.Core.Scripts;

/// <summary>Les conditions d'un déclencheur ; toutes celles données doivent être réunies.</summary>
public sealed record ScriptCondition
{
    /// <summary>Temps de combat écoulé, en ms (le temps figé ne compte pas).</summary>
    public int? AtMs { get; init; }

    /// <summary>Identifiant d'un autre déclencheur, qui doit être terminé.</summary>
    public string? After { get; init; }

    /// <summary>Repère (<c>tag</c>) d'une unité du scénario, qui doit être hors de combat (à terre ou retirée).</summary>
    public string? UnitDown { get; init; }

    /// <summary>
    /// Repère (<c>tag</c>) d'une unité du scénario, qui doit être prête à agir (debout, jauge pleine, libre) ; une unité
    /// hors de combat (à terre ou retirée) ne le sera jamais : la condition est alors remplie, pour ne pas bloquer le script.
    /// </summary>
    public string? UnitReady { get; init; }
}
