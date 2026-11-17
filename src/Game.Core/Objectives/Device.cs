using Game.Core.Grids;

namespace Game.Core.Objectives;

/// <summary>Un objet à neutraliser pendant le combat, et où en est le travail des héros. Seule la simulation le fait avancer.</summary>
public sealed class Device
{
    internal Device(DeviceDefinition definition, Objective objective)
    {
        Definition = definition;
        Objective = objective;
    }

    public DeviceDefinition Definition { get; }

    /// <summary>La course contre la montre à laquelle il appartient.</summary>
    public Objective Objective { get; }

    public Cell Cell => Definition.Cell;

    /// <summary>Interactions déjà faites (jusqu'à <see cref="DeviceDefinition.Interactions"/>).</summary>
    public int Done { get; internal set; }

    public bool IsNeutralized => Done >= Definition.Interactions;
}
