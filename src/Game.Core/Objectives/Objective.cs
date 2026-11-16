namespace Game.Core.Objectives;

/// <summary>Un objectif du combat en cours et où il en est. Seule la simulation le fait avancer.</summary>
public sealed class Objective
{
    internal Objective(ObjectiveDefinition definition) => Definition = definition;

    public ObjectiveDefinition Definition { get; }

    /// <summary>En cours, puis réussi ou raté une fois pour toutes.</summary>
    public ObjectiveState State { get; internal set; }
}
