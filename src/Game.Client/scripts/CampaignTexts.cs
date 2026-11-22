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
        var falls = hero.HalfFalls > 0 && !hero.IsDead ? $", {Falls(hero.HalfFalls)} / {Falls(hero.HalfFallsToDeath)} chutes" : "";
        return $"{hero.Name} ({archetypeName}, niveau {hero.Level}, {xp}{points}{falls}{(hero.IsDead ? ", mort" : "")})";
    }

    /// <summary>Des demi-chutes en chutes : « 1,5 ».</summary>
    internal static string Falls(int halves) => halves % 2 == 0 ? $"{halves / 2}" : $"{halves / 2},5";

    /// <summary>« Mémorial : Rempart, Éclair » : les héros morts, ou rien.</summary>
    public static string Memorial(IEnumerable<HeroInfo> heroes)
    {
        var dead = heroes.Where(h => h.IsDead).Select(h => $"{h.Name} ({h.Owner})").ToList();
        return dead.Count == 0 ? "" : $"Mémorial : {string.Join(", ", dead)}";
    }

    private static string Points(int count, string kind) => $"{count} {(count > 1 ? "points" : "point")} {kind}";

    /// <summary>« Dernière quête : Racket dans la rue, victoire : +20 XP par héros, 3 matériaux, réputation +2 ».</summary>
    public static string LastQuest(QuestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var materials = result.Materials.Values.Sum();
        return $"Dernière quête : {result.Quest}, {(result.Victory ? "victoire" : "défaite")} : +{result.Xp} XP par héros"
               + (materials > 0 ? $", {materials} matériaux" : "")
               + $", réputation {result.Reputation:+0;-0;0}"
               + (result.Deaths.Count > 0 ? $". Mort(s) au combat : {string.Join(", ", result.Deaths)}" : "");
    }

    /// <summary>Le nom affiché d'une case de l'arbre : compétence (ou variante), passif, ou compétence à venir.</summary>
    public static string NodeName(Game.Core.Progression.HeroCatalog catalog, Game.Core.Progression.SkillTree tree, string id)
    {
        if (catalog.Skills.TryGetValue(id, out var skill) && skill.Name is { } name)
            return name;
        if (tree.Planned.TryGetValue(id, out var planned))
            return $"{planned} (à venir)";
        var passive = tree.Branches.SelectMany(b => b.Tiers).SelectMany(t => t.Passives).FirstOrDefault(p => p.Id == id);
        return passive is null ? id : $"{passive.Name} (passif{(passive.Planned ? ", à venir" : "")})";
    }

    /// <summary>« Pot commun : 9 matériaux » (une sorte pour l'instant).</summary>
    public static string Pot(IReadOnlyDictionary<string, int> materials) =>
        $"Pot commun : {materials.Values.Sum()} matériaux";
}
