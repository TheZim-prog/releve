namespace Game.Core.Combat;

/// <summary>Ce que font les sbires d'un chef quand il tombe (docs/design.md 3.5), selon le groupe d'ennemis.</summary>
public enum MinionReaction
{
    /// <summary>Ils prennent la fuite vers le bord de carte, et ne comptent plus pour le combat.</summary>
    Flee,

    /// <summary>Ils se rendent : ils quittent aussitôt le combat.</summary>
    Surrender,
}
