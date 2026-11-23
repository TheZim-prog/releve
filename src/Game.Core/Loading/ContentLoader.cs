using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Progression;
using Game.Core.Reputation;
using Game.Core.World;
using Game.Core.Scenarios;

namespace Game.Core.Loading;

/// <summary>
/// Charge et vérifie un dossier <c>data/</c> entier. L'ordre suit les dépendances : les règles, puis les compétences et
/// les unités (qui se désignent entre elles), les arbres et les héros (qui désignent des compétences et des unités), les
/// cartes partagées, enfin les scénarios (qui placent des unités et des héros sur une carte). Une erreur nomme le fichier
/// en cause.
/// </summary>
internal static class ContentLoader
{
    private const string ProgressionFile = "progression.json";
    private const string ReputationFile = "reputation.json";
    private const string MapFile = "world/map.json";
    private const string EventsFile = "world/events.json";
    private const string WorldRulesFile = "world/rules.json";

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

        var maps = files.List("maps").Select(file => files.Load(file, MapDefinition.FromJson)).ToDictionary(m => m.Id);
        var scenarios = files.List("scenarios")
            .Select(file => (File: file, Scenario: files.Load(file, json => ScenarioDefinition.FromJson(json, maps))))
            // Chaque scénario doit pouvoir se monter avec ce contenu (unités et héros connus, points de stats valides).
            .Select(s => DataFiles.InContext(s.File, () => { s.Scenario.ToSetup(rules, units, heroes); return s.Scenario; }))
            .ToDictionary(s => s.Id);

        var reputation = files.Exists(ReputationFile) ? files.Load(ReputationFile, ReputationRules.FromJson) : null;
        var quests = new Dictionary<string, QuestDefinition>();
        foreach (var file in files.List("quests"))
        {
            foreach (var quest in files.Load(file, QuestDefinition.ListFromJson))
            {
                DataFiles.InContext(file, () =>
                {
                    quest.ValidateAgainst(scenarios);
                    return quests.TryAdd(quest.Id, quest) ? quest : throw new ArgumentException($"Quest '{quest.Id}' is defined twice.");
                });
            }
        }
        // Le panneau n'est jamais vide au départ (docs/design.md 4.7).
        if (quests.Count > 0 && quests.Values.All(q => q.PublishedDay > 0))
            throw new ArgumentException("data/quests: at least one quest must be published on day 0.");

        var (world, events, worldRules) = World(files, units, quests);

        return new GameContent
        {
            Rules = rules, Skills = skills, Units = units, Heroes = heroes, Scenarios = scenarios, Reputation = reputation,
            Quests = quests, World = world, Events = events, WorldRules = worldRules, Version = files.Version(),
        };
    }

    /// <summary>
    /// La carte du monde, ses événements et ses règles (data/world), ou rien si la carte manque. Chaque point d'intérêt
    /// a un événement connu ; chaque quête a un lieu de mission de la carte ; la gravité n'ajoute que des unités connues.
    /// </summary>
    private static (WorldMap?, IReadOnlyDictionary<string, WorldEvent>, WorldRules?) World(DataFiles files,
        IReadOnlyDictionary<string, UnitDefinition> units, IReadOnlyDictionary<string, QuestDefinition> quests)
    {
        if (!files.Exists(MapFile))
            return (null, new Dictionary<string, WorldEvent>(), null);
        var map = files.Load(MapFile, WorldMap.FromJson);
        var events = files.Load(EventsFile, WorldEvent.ListFromJson).ToDictionary(e => e.Id);
        var rules = files.Load(WorldRulesFile, WorldRules.FromJson);
        DataFiles.InContext(WorldRulesFile, () =>
        {
            rules.ValidateAgainst(units);
            return rules;
        });
        DataFiles.InContext(MapFile, () =>
        {
            if (map.Nodes.FirstOrDefault(n => n.Event is { } e && !events.ContainsKey(e)) is { } poi)
                throw new ArgumentException($"Point of interest '{poi.Id}' has an unknown event '{poi.Event}'.");
            return map;
        });
        foreach (var quest in quests.Values)
        {
            DataFiles.InContext($"quest '{quest.Id}'", () =>
            {
                if (quest.Site is null || map.Node(quest.Site) is not { Kind: MapNodeKind.Site })
                    throw new ArgumentException($"Quest '{quest.Id}' needs a site of the world map (a mission place).");
                quest.Gravity?.Validate(units);
                return quest;
            });
        }
        return (map, events, rules);
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
