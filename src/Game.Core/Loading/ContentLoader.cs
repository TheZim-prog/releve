using Game.Core.Combat;
using Game.Core.Progression;
using Game.Core.Scenarios;

namespace Game.Core.Loading;

/// <summary>
/// Charge et vérifie un dossier <c>data/</c> entier. L'ordre suit les dépendances : les règles, puis les compétences et
/// les unités (qui se désignent entre elles), les arbres et les héros (qui désignent des compétences et des unités),
/// enfin les scénarios (qui placent des unités et des héros). Une erreur nomme le fichier en cause.
/// </summary>
internal static class ContentLoader
{
    private const string ProgressionFile = "progression.json";

    public static GameContent Load(string dataDirectory)
    {
        using var files = new DataFiles(dataDirectory);

        var rules = files.Load("combat-rules.json", CombatRules.FromJson);

        var linker = new DataLinker();
        foreach (var file in files.List("skills"))
            linker.AddSkills(file, files.Read(file));
        foreach (var file in files.List("units"))
            linker.AddUnits(file, files.Read(file));
        var skills = linker.Skills().Select(item => One(item, "skill", SkillDefinition.ListFromJson)).ToDictionary(s => s.Id);
        var units = linker.Units().Select(item => One(item, "unit", UnitDefinition.ListFromJson)).ToDictionary(u => u.Id);
        foreach (var unit in units.Values)
            unit.ValidateAgainst(rules);

        var heroes = Heroes(files, units, skills);

        var scenarios = files.List("scenarios")
            .Select(file => (File: file, Scenario: files.Load(file, ScenarioDefinition.FromJson)))
            // Chaque scénario doit pouvoir se monter avec ce contenu (unités et héros connus, points de stats valides).
            .Select(s => DataFiles.InContext(s.File, () => { s.Scenario.ToSetup(rules, units, heroes); return s.Scenario; }))
            .ToDictionary(s => s.Id);

        return new GameContent
        {
            Rules = rules, Skills = skills, Units = units, Heroes = heroes, Scenarios = scenarios, Version = files.Version(),
        };
    }

    /// <summary>Les arbres et les héros prêts à jouer ; <c>null</c> pour un contenu sans progression (aucun <c>progression.json</c>).</summary>
    private static HeroCatalog? Heroes(DataFiles files, IReadOnlyDictionary<string, UnitDefinition> units,
        IReadOnlyDictionary<string, SkillDefinition> skills)
    {
        var treeFiles = files.List("trees");
        var heroFiles = files.List("heroes");
        if (!files.Exists(ProgressionFile))
        {
            return treeFiles.Count + heroFiles.Count == 0
                ? null
                : throw new FileNotFoundException($"Skill trees and heroes need {ProgressionFile}.");
        }
        return new HeroCatalog(
            files.Load(ProgressionFile, ProgressionRules.FromJson),
            units,
            skills,
            treeFiles.Select(file => files.Load(file, SkillTree.FromJson)).ToList(),
            heroFiles.SelectMany(file => files.Load(file, HeroSheet.ListFromJson)).ToList());
    }

    /// <summary>
    /// Lit et vérifie une compétence ou une unité dont les références sont résolues ; une erreur nomme son fichier et
    /// son identifiant.
    /// </summary>
    private static T One<T>(LinkedItem item, string kind, Func<string, IReadOnlyList<T>> listFromJson) =>
        DataFiles.InContext($"{item.Source}, {kind} '{item.Id}'", () => listFromJson($"[{item.Json}]").Single());
}
