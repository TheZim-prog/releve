using Game.Core.Reputation;

namespace Game.Client;

/// <summary>Le bilan de réputation d'une mission, en lignes pour le journal (affichage minimal, à polir plus tard).</summary>
public static class ReputationTexts
{
    public static IEnumerable<string> Lines(ReputationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        yield return $"Réputation de la mission : {report.Total:+0;-0;0}";
        foreach (var (count, label) in new[]
                 {
                     (report.CiviliansSaved, "civil(s) sauvé(s)"),
                     (report.SecondaryObjectivesCompleted, "objectif(s) secondaire(s) réussi(s)"),
                     (report.SceneryDestroyed, "élément(s) de décor détruit(s) par les héros"),
                     (report.CiviliansHurtByHeroes, "civil(s) blessé(s) par les héros"),
                     (report.CiviliansLostToHeroes, "civil(s) perdu(s) par la faute des héros"),
                     (report.CiviliansLostToEnemies, "civil(s) perdu(s) face aux ennemis"),
                 })
        {
            if (count > 0)
                yield return $"  {count} {label}";
        }
    }
}
