using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Ce qui s'est passé dans la simulation. Le client ne fait que rejouer les événements.</summary>
public abstract record CombatEvent(int Tick);

/// <summary>
/// L'unité peut agir : sa jauge vient de se remplir, ou son étourdissement vient de finir jauge pleine.
/// Elle cesse d'être prête quand elle agit (<see cref="UnitMoved"/>, <see cref="SkillUsed"/>), est étourdie
/// (<see cref="StatusApplied"/>) ou tombe : ces événements suffisent, il n'y a pas d'événement « plus prête ».
/// </summary>
public sealed record UnitReady(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>L'unité s'est déplacée le long de <paramref name="Path"/> (départ exclu, arrivée incluse).</summary>
public sealed record UnitMoved(int Tick, UnitId Unit, Cell From, IReadOnlyList<Cell> Path) : CombatEvent(Tick)
{
    public Cell To => Path[^1];

    // Égalité par valeur sur le chemin (par défaut, un record compare la référence de la liste).
    public bool Equals(UnitMoved? other) =>
        other is not null && base.Equals(other) && Unit == other.Unit && From == other.From && Path.SequenceEqual(other.Path);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Unit, From, Path.Count);
}

/// <summary>L'unité utilise une compétence sur une cible (les effets suivent dans d'autres événements).</summary>
public sealed record SkillUsed(int Tick, UnitId Unit, string SkillId, UnitId Target) : CombatEvent(Tick);

/// <summary>L'unité perd <paramref name="Amount"/> PV (dégâts tirés) ; il lui en reste <paramref name="Hp"/>.</summary>
public sealed record UnitDamaged(int Tick, UnitId Unit, int Amount, int Hp) : CombatEvent(Tick);

/// <summary>
/// L'unité reçoit un état, jusqu'au tick <paramref name="EndTick"/> (exclu). Émis aussi quand l'état était déjà là :
/// <paramref name="EndTick"/> donne alors la durée retenue (la plus longue des deux).
/// </summary>
public sealed record StatusApplied(int Tick, UnitId Unit, StatusKind Status, int EndTick) : CombatEvent(Tick);

/// <summary>L'état de l'unité prend fin.</summary>
public sealed record StatusEnded(int Tick, UnitId Unit, StatusKind Status) : CombatEvent(Tick);

/// <summary>L'unité tombe à terre : elle reste sur sa case, sa jauge est gelée.</summary>
public sealed record UnitDowned(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>L'unité est vaincue et retirée de la grille.</summary>
public sealed record UnitRemoved(int Tick, UnitId Unit) : CombatEvent(Tick);

/// <summary>Commande refusée : rien n'a changé, la jauge de l'unité reste pleine.</summary>
public sealed record CommandRejected(int Tick, CombatCommand Command, RejectionReason Reason) : CombatEvent(Tick);

public enum RejectionReason
{
    UnknownUnit,

    /// <summary>L'unité est à terre ou retirée.</summary>
    Incapacitated,

    /// <summary>L'unité est étourdie : elle ne peut pas agir, même jauge pleine.</summary>
    Stunned,

    NotReady,

    /// <summary>Ni déplacement ni action.</summary>
    Empty,

    /// <summary>Déplacement vers la case où l'unité se trouve déjà.</summary>
    InvalidDestination,

    /// <summary>Case d'arrivée bloquée, occupée, inaccessible ou hors de portée de la mobilité.</summary>
    Unreachable,

    /// <summary>L'unité ne possède pas cette compétence.</summary>
    UnknownSkill,

    /// <summary>Compétence en recharge.</summary>
    OnCooldown,

    /// <summary>Cible inconnue, alliée, l'unité elle-même, à terre ou retirée.</summary>
    InvalidTarget,

    /// <summary>Cible trop loin depuis la case d'arrivée.</summary>
    OutOfRange,

    /// <summary>
    /// Cible à portée mais cachée depuis la case d'arrivée : par un mur ou un ennemi debout, ou, au contact
    /// en diagonale, par un mur sur un coin.
    /// </summary>
    NoLineOfSight,
}

/// <summary>Commande telle qu'appliquée, avec son tick : de quoi rejouer le combat à l'identique.</summary>
public sealed record LoggedCommand(int Tick, CombatCommand Command);
