using Game.Core.Combat;

namespace Game.Core.Objectives;

/// <summary>
/// Le compte des civils du combat : évacués (sortis par le bord de carte), encore sur la carte, perdus (retirés sans être
/// sortis : tombés à 0 PV).
/// </summary>
public sealed record RescueTally(int Evacuated, int OnMap, int Lost)
{
    public static RescueTally Of(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        int evacuated = 0, onMap = 0, lost = 0;
        foreach (var unit in simulation.Units)
        {
            if (!unit.IsCivilian)
                continue;
            if (unit.Escaped)
                evacuated++;
            else if (unit.Status == UnitStatus.Removed)
                lost++;
            else
                onMap++;
        }
        return new RescueTally(evacuated, onMap, lost);
    }
}
