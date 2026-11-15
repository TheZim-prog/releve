using Game.Core.Grids;

namespace Game.Core.Combat;

// Événements des zones au sol et des obstacles dressés par une compétence.

/// <summary>
/// La compétence <paramref name="SkillId"/> de <paramref name="Owner"/> laisse une zone au sol sur ces cases, jusqu'au
/// tick <paramref name="EndTick"/> (<c>null</c> : un piège, jusqu'à son déclenchement).
/// </summary>
public sealed record GroundZoneCreated(int Tick, int Zone, UnitId Owner, string SkillId, IReadOnlyList<Cell> Cells, int? EndTick)
    : CombatEvent(Tick)
{
    public bool Equals(GroundZoneCreated? other) =>
        other is not null && base.Equals(other) && Zone == other.Zone && Owner == other.Owner && SkillId == other.SkillId
        && EndTick == other.EndTick && Cells.SequenceEqual(other.Cells);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Zone, Owner, SkillId, Cells.Count);
}

/// <summary>La zone au sol disparaît : durée écoulée, fumée dissipée, piège déclenché ou remplacé.</summary>
public sealed record GroundZoneEnded(int Tick, int Zone) : CombatEvent(Tick);

/// <summary>L'unité entre sur le piège de <paramref name="Owner"/> en <paramref name="Cell"/> et le déclenche.</summary>
public sealed record TrapTriggered(int Tick, int Zone, UnitId Unit, UnitId Owner, Cell Cell) : CombatEvent(Tick);

/// <summary>Un obstacle est dressé sur la case (un obstacle bas ou un mur), jusqu'au tick <paramref name="EndTick"/>.</summary>
public sealed record ObstacleRaised(int Tick, Cell Cell, Terrain Terrain, int EndTick) : CombatEvent(Tick);

/// <summary>L'obstacle dressé sur la case a fait son temps : c'est de nouveau du sol.</summary>
public sealed record ObstacleMelted(int Tick, Cell Cell) : CombatEvent(Tick);
