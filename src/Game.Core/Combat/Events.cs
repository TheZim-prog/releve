using Game.Core.Grids;

namespace Game.Core.Combat;

/// <summary>Ce qui s'est passé dans la simulation. Le client ne fait que rejouer les événements.</summary>
public abstract record CombatEvent(int Tick);

/// <summary>La jauge de l'unité vient d'atteindre sa capacité.</summary>
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

/// <summary>Commande refusée : rien n'a changé, la jauge de l'unité reste pleine.</summary>
public sealed record CommandRejected(int Tick, CombatCommand Command, RejectionReason Reason) : CombatEvent(Tick);

public enum RejectionReason
{
    UnknownUnit,
    NotReady,

    /// <summary>Ni déplacement ni action.</summary>
    Empty,

    /// <summary>Déplacement vers la case où l'unité se trouve déjà.</summary>
    InvalidDestination,

    /// <summary>Case d'arrivée bloquée, occupée, inaccessible ou hors de portée de la mobilité.</summary>
    Unreachable,
}

/// <summary>Commande telle qu'appliquée, avec son tick : de quoi rejouer le combat à l'identique.</summary>
public sealed record LoggedCommand(int Tick, CombatCommand Command);
