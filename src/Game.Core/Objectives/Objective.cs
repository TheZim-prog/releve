using Game.Core.Combat;

namespace Game.Core.Objectives;

/// <summary>Un objectif du combat en cours et où il en est. Seule la simulation le fait avancer.</summary>
public sealed class Objective
{
    internal Objective(ObjectiveDefinition definition, CombatRules rules)
    {
        Definition = definition;
        Devices = (definition.Devices ?? Array.Empty<DeviceDefinition>()).Select(d => new Device(d, this)).ToList().AsReadOnly();
        EndTick = definition.DurationMs is { } ms ? rules.MillisecondsToTicks(ms) : null;
    }

    public ObjectiveDefinition Definition { get; }

    /// <summary>En cours, puis réussi ou raté une fois pour toutes.</summary>
    public ObjectiveState State { get; internal set; }

    /// <summary>Course contre la montre : les objets à neutraliser (vide pour les autres types).</summary>
    public IReadOnlyList<Device> Devices { get; }

    /// <summary>Course contre la montre : le tick où le compte à rebours arrive au bout (<c>null</c> sans compte à rebours).</summary>
    public int? EndTick { get; }
}
