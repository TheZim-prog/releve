namespace Game.Core.Combat;

/// <summary>
/// Le script de mission affiche un message (n° <paramref name="Number"/>, à partir de 1, dans l'ordre d'apparition). S'il
/// <paramref name="Freezes"/>, le combat reste figé jusqu'à <see cref="TimeResumed"/> (cf.
/// <see cref="CombatSimulation.FrozenMessage"/>).
/// </summary>
public sealed record ScriptMessageShown(int Tick, ScriptMessage Message) : CombatEvent(Tick);

/// <summary>Un joueur a fait continuer le message figé n° <paramref name="Message"/> : le temps reprend.</summary>
public sealed record TimeResumed(int Tick, int Message) : CombatEvent(Tick);

/// <summary>Un message du script : son numéro, qui parle (ou <c>null</c>), le texte, et s'il fige le combat.</summary>
public sealed record ScriptMessage(int Number, string? Speaker, string Text, bool Freezes);
