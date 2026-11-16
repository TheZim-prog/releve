using Game.Core.Grids;

namespace Game.Core.Combat;

// Événements des réactions : gardes, esquives, interceptions, ripostes, surveillances.

/// <summary>L'unité se met en garde (Parade, Déviation, Maître d'armes), jusqu'au tick <paramref name="EndTick"/> ou, sans fin, jusqu'à sa prochaine action.</summary>
public sealed record GuardRaised(int Tick, UnitId Unit, GuardAgainst Against, int? EndTick) : CombatEvent(Tick);

/// <summary>La garde de l'unité tombe (elle a bloqué, son temps est écoulé, ou l'unité agit de nouveau).</summary>
public sealed record GuardEnded(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>L'unité pare l'attaque de <paramref name="Attacker"/> : elle n'a aucun effet sur elle.</summary>
public sealed record AttackParried(int Tick, UnitId Unit, UnitId Attacker) : CombatEvent(Tick);

/// <summary>L'unité esquive l'attaque de <paramref name="Attacker"/> (Réflexes) : elle n'a aucun effet sur elle.</summary>
public sealed record AttackDodged(int Tick, UnitId Unit, UnitId Attacker) : CombatEvent(Tick);

/// <summary><paramref name="Guard"/> prend à la place de <paramref name="Ally"/> l'attaque de <paramref name="Attacker"/> (Garde du corps).</summary>
public sealed record AttackIntercepted(int Tick, UnitId Guard, UnitId Ally, UnitId Attacker) : CombatEvent(Tick);

/// <summary>L'unité riposte contre <paramref name="Target"/> avec son attaque de base (les dégâts suivent).</summary>
public sealed record CounterAttack(int Tick, UnitId Unit, UnitId Target) : CombatEvent(Tick);

/// <summary>
/// <paramref name="Owner"/> surveille ces cases (Tir de couverture) ou l'allié <paramref name="Ally"/> (Protection
/// rapprochée), avec sa compétence <paramref name="SkillId"/>.
/// </summary>
public sealed record WatchSet(int Tick, UnitId Owner, string SkillId, IReadOnlyList<Cell>? Cells, UnitId? Ally) : CombatEvent(Tick)
{
    public bool Equals(WatchSet? other) =>
        other is not null && base.Equals(other) && Owner == other.Owner && SkillId == other.SkillId && Ally == other.Ally
        && (Cells ?? Array.Empty<Cell>()).SequenceEqual(other.Cells ?? Array.Empty<Cell>());

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Owner, SkillId, Ally);
}

/// <summary>La surveillance de <paramref name="Owner"/> prend fin (elle a tiré, ou il agit de nouveau, ou il est tombé).</summary>
public sealed record WatchEnded(int Tick, UnitId Owner) : CombatEvent(Tick);

/// <summary>La surveillance de <paramref name="Owner"/> tire sur <paramref name="Target"/> (les dégâts suivent).</summary>
public sealed record WatchFired(int Tick, UnitId Owner, UnitId Target) : CombatEvent(Tick);
