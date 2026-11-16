namespace Game.Core.Combat;

/// <summary>Le chef <paramref name="Leader"/> est tombé (vaincu, à terre ou enfui) : ses sbires réagissent (<paramref name="Minions"/>).</summary>
public sealed record LeaderFell(int Tick, UnitId Leader, MinionReaction Minions) : CombatEvent(Tick);

/// <summary>Le sbire se rend, son chef tombé : il quitte le combat (retiré, mais pas vaincu).</summary>
public sealed record UnitSurrendered(int Tick, UnitId Unit) : CombatEvent(Tick);
