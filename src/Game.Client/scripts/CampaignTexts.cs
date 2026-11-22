using Game.Core.Network;

namespace Game.Client;

/// <summary>Les textes de campagne communs au QG et à l'académie (affichage minimal, à polir plus tard).</summary>
public static class CampaignTexts
{
    /// <summary>« Rempart (Colosse, niveau 2, 120 / 200 XP, à dépenser : 1 point de stats, 1 point de compétence) ».</summary>
    public static string Hero(HeroInfo hero, string archetypeName)
    {
        ArgumentNullException.ThrowIfNull(hero);
        var xp = hero.NextLevelXp is { } next ? $"{hero.Xp} / {next} XP" : $"{hero.Xp} XP, niveau maximum";
        var spend = new List<string>();
        if (hero.StatPointsToSpend > 0)
            spend.Add(Points(hero.StatPointsToSpend, "de stats"));
        if (hero.SkillPointsToSpend > 0)
            spend.Add(Points(hero.SkillPointsToSpend, "de compétence"));
        var points = spend.Count > 0 ? $", à dépenser : {string.Join(", ", spend)}" : "";
        return $"{hero.Name} ({archetypeName}, niveau {hero.Level}, {xp}{points}{(hero.IsDead ? ", mort" : "")})";
    }

    private static string Points(int count, string kind) => $"{count} {(count > 1 ? "points" : "point")} {kind}";

    /// <summary>« Dernière quête : Racket dans la rue, victoire : +20 XP par héros, 3 matériaux, réputation +2 ».</summary>
    public static string LastQuest(QuestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var materials = result.Materials.Values.Sum();
        return $"Dernière quête : {result.Quest}, {(result.Victory ? "victoire" : "défaite")} : +{result.Xp} XP par héros"
               + (materials > 0 ? $", {materials} matériaux" : "")
               + $", réputation {result.Reputation:+0;-0;0}";
    }

    /// <summary>« Pot commun : 9 matériaux » (une sorte pour l'instant).</summary>
    public static string Pot(IReadOnlyDictionary<string, int> materials) =>
        $"Pot commun : {materials.Values.Sum()} matériaux";
}
