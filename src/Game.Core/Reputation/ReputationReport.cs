using Game.Core.Combat;
using Game.Core.Objectives;

namespace Game.Core.Reputation;

/// <summary>
/// Le bilan de réputation d'une mission : ce qui s'y est passé, compté, et ce que ça rapporte ou coûte selon les
/// <see cref="ReputationRules"/>. Chaque civil a une seule issue : perdu par la faute des héros, perdu par la faute des
/// ennemis, blessé par les héros (vivant, même à la défaite : ils l'ont blessé), ou sauvé (évacué, ou encore sur la carte
/// à la victoire) ; un civil indemne encore sur la carte à la défaite ne compte pas. La réputation du groupe, qui cumule
/// les missions, viendra avec la campagne.
/// </summary>
public sealed record ReputationReport(
    int SceneryDestroyed,
    int CiviliansHurtByHeroes,
    int CiviliansLostToHeroes,
    int CiviliansLostToEnemies,
    int CiviliansSaved,
    int SecondaryObjectivesCompleted,
    int Total)
{
    /// <summary>Le bilan du combat tel qu'il est (à lire une fois terminé : avant, les civils et objectifs sont en cours).</summary>
    public static ReputationReport Of(CombatSimulation simulation, ReputationRules rules)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(rules);
        int hurt = 0, lostToHeroes = 0, lostToEnemies = 0, saved = 0;
        foreach (var civilian in simulation.Units.Where(u => u.IsCivilian))
        {
            if (civilian.Status == UnitStatus.Removed && !civilian.Escaped)
            {
                if (simulation.WasLostToHeroes(civilian.Id))
                    lostToHeroes++;
                else
                    lostToEnemies++;
            }
            else if (simulation.WasHurtByHeroes(civilian.Id))
                hurt++;
            else if (civilian.Escaped || simulation.Outcome == true)
                saved++;
        }
        var secondaries = simulation.Objectives.Count(o => o.Definition.Secondary && o.State == ObjectiveState.Completed);
        var scenery = simulation.SceneryDestroyedByHeroes;

        var total = scenery * rules.SceneryDestroyed
                    + hurt * rules.CivilianHurtByHeroes
                    + lostToHeroes * rules.CivilianLostToHeroes
                    + lostToEnemies * rules.CivilianLostToEnemies
                    + saved * rules.CivilianSaved
                    + secondaries * rules.SecondaryObjectiveCompleted;
        return new ReputationReport(scenery, hurt, lostToHeroes, lostToEnemies, saved, secondaries, total);
    }
}
