using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>
/// Le script de mission affiche un message (n° <paramref name="Number"/>, à partir de 1, dans l'ordre d'apparition). S'il
/// <paramref name="Freezes"/>, le combat reste figé jusqu'à <see cref="TimeResumed"/> (cf.
/// <see cref="CombatSimulation.FrozenMessage"/>).
/// </summary>
public sealed record ScriptMessageShown(int Tick, ScriptMessage Message) : CombatEvent(Tick);

/// <summary>Un joueur a fait continuer le message figé n° <paramref name="Message"/> : le temps reprend.</summary>
public sealed record TimeResumed(int Tick, int Message) : CombatEvent(Tick);

/// <summary>
/// Un message du script : son numéro, qui parle (ou <c>null</c>), le texte, et s'il fige le combat jusqu'à « Continuer »
/// (la consigne d'actions imposées ne l'attend pas : elle dure tant qu'elles ne sont pas faites).
/// </summary>
public sealed record ScriptMessage(int Number, string? Speaker, string Text, bool Freezes);

/// <summary>Le script impose une action à ce héros (cf. <see cref="CombatSimulation.ImposedActions"/>) ; sa jauge est pleine.</summary>
public sealed record ActionImposed(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>
/// L'action imposée à ce héros est faite (<paramref name="Done"/>), ou abandonnée parce qu'elle est devenue impossible
/// (cible tombée, héros étourdi…).
/// </summary>
public sealed record ImposedActionEnded(int Tick, UnitId Unit, bool Done) : CombatEvent(Tick);

/// <summary>
/// L'unité de repère <paramref name="Tag"/> n'a pas pu faire l'action que le script lui ordonnait : absente
/// (<paramref name="Unit"/> <c>null</c> : pas encore arrivée), ou action impossible (hors de combat, étourdie, en fuite,
/// sans ligne de vue…), ou annulée en chemin. Faite, elle produit les événements de toute action.
/// </summary>
public sealed record ScriptedActionFailed(int Tick, string Tag, UnitId? Unit) : CombatEvent(Tick);

/// <summary>
/// Une action imposée, ses repères résolus : la compétence (ou <c>null</c> : déplacement seul) sur une unité ou une case,
/// et la case d'arrivée imposée (ou <c>null</c> : libre).
/// </summary>
public sealed record ResolvedAction(UnitId Unit, string? Skill, UnitId? Target, Cell? Cell, Cell? MoveTo)
{
    /// <summary>L'activation respecte-t-elle ce qui est imposé ?</summary>
    public bool Allows(ActivateCommand command) =>
        command.Unit == Unit
        && command.Interaction is null
        && (MoveTo is null || command.MoveTo == MoveTo)
        && (Skill is null
            ? command.Skill is null
            : command.Skill is { } use && use.SkillId == Skill
                && (Target is null || use.Target == Target) && (Cell is null || use.Cell == Cell));
}
