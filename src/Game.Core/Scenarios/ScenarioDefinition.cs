using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using Game.Core.Progression;
using Game.Core.Scripts;

namespace Game.Core.Scenarios;

/// <summary>
/// Combat décrit en données (data/scenarios) : la carte (une ligne de texte par rangée, voir <see cref="Grid.FromRows"/>),
/// les unités placées, la graine et les objectifs. <see cref="ToSetup"/> en fait l'état initial d'un combat.
/// </summary>
public sealed record ScenarioDefinition
{
    public required string Id { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    public required ulong Seed { get; init; }

    /// <summary>La carte, écrite dans le scénario ; ou bien <see cref="MapId"/> (exactement l'un des deux dans les données).</summary>
    public IReadOnlyList<string>? Map { get; init; }

    /// <summary>Une carte partagée de data/maps ; à la lecture, elle est recopiée dans <see cref="Map"/>.</summary>
    public string? MapId { get; init; }

    public required IReadOnlyList<ScenarioUnit> Units { get; init; }

    /// <summary>Objectifs de la mission ; sans eux, éliminer tous les ennemis (<see cref="ObjectiveDefinition.Default"/>).</summary>
    public IReadOnlyList<ObjectiveDefinition>? Objectives { get; init; }

    /// <summary>Renforts qui arrivent en cours de combat (aucun par défaut).</summary>
    public IReadOnlyList<ScenarioWave>? Waves { get; init; }

    /// <summary>Script de mission : déclencheurs et leurs effets (aucun par défaut).</summary>
    public IReadOnlyList<ScriptTrigger>? Script { get; init; }

    /// <summary>
    /// Cases où se placent les héros des joueurs (une quête de campagne), dans l'ordre de leur déploiement ; aucune pour
    /// un combat à héros fixes.
    /// </summary>
    public IReadOnlyList<Cell>? HeroSlots { get; init; }

    /// <summary>
    /// Lit un scénario ; sa carte est soit écrite dedans, soit désignée parmi <paramref name="maps"/> (elle y est alors
    /// recopiée).
    /// </summary>
    public static ScenarioDefinition FromJson(string json, IReadOnlyDictionary<string, MapDefinition>? maps = null)
    {
        var scenario = DataJson.Deserialize<ScenarioDefinition>(json);
        if ((scenario.Map is null) == (scenario.MapId is null))
            throw new ArgumentException($"Scenario '{scenario.Id}' needs either a map or a mapId.");
        if (scenario.MapId is { } mapId)
        {
            if (maps is null || !maps.TryGetValue(mapId, out var map))
                throw new ArgumentException($"Scenario '{scenario.Id}': unknown map '{mapId}'.");
            scenario = scenario with { Map = map.Rows };
        }
        scenario.Validate();
        return scenario;
    }

    /// <summary>
    /// Vérifie le scénario seul : carte lisible, unités sur des cases libres et distinctes, au plus un chef par camp (un
    /// ennemi, pas un héros), objectifs valides.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("A scenario needs an id and a name.");
        if (Map is null || Units is null || Units.Any(u => u is null))
            throw new ArgumentException($"Scenario '{Id}' needs a map and a unit list without null entries.");
        try
        {
            ObjectiveDefinition.ValidateMission(Objectives ?? ObjectiveDefinition.Default);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"Scenario '{Id}': {e.Message}", e);
        }
        var grid = Grid.FromRows(Map.ToArray());
        var taken = new HashSet<Cell>();
        foreach (var unit in Units)
        {
            var cell = new Cell(unit.X, unit.Y);
            if ((unit.Unit is null) == (unit.Hero is null) || string.IsNullOrWhiteSpace(unit.Unit ?? unit.Hero) || unit.Team < 0)
                throw new ArgumentException($"Scenario '{Id}': each entry places either a unit or a hero, in a team that is not negative.");
            if (!grid.IsWalkable(cell))
                throw new ArgumentException($"Scenario '{Id}': {unit.Label} stands on a blocked cell {cell}.");
            if (!taken.Add(cell))
                throw new ArgumentException($"Scenario '{Id}': two units on {cell}.");
            if (unit.Leader is not null && (unit.Hero is not null || unit.Team == CombatSimulation.PlayerTeam))
                throw new ArgumentException($"Scenario '{Id}': {unit.Label} cannot lead minions (only an enemy unit can, not a hero).");
        }
        foreach (var slot in HeroSlots ?? Array.Empty<Cell>())
        {
            if (!grid.IsWalkable(slot))
                throw new ArgumentException($"Scenario '{Id}': the hero slot {slot} is a blocked cell.");
            if (!taken.Add(slot))
                throw new ArgumentException($"Scenario '{Id}': the hero slot {slot} is already taken.");
        }
        if (Units.Where(u => u.Leader is not null).GroupBy(u => u.Team).FirstOrDefault(g => g.Count() > 1) is { } team)
            throw new ArgumentException($"Scenario '{Id}': team {team.Key} has more than one leader.");
        if (Waves?.Any(w => w is null) == true)
            throw new ArgumentException($"Scenario '{Id}': the wave list contains a null entry.");
        foreach (var wave in Waves ?? Array.Empty<ScenarioWave>())
            wave.Validate(Id);
    }

    /// <summary>
    /// Identifiant de combat du n-ième héros déployé (à partir de 0) : après les unités du scénario.
    /// </summary>
    public UnitId DeployedUnitId(int index) => new(Units.Count + index + 1);

    /// <summary>
    /// État initial du combat : la carte, et les unités (identifiants 1, 2, 3… dans l'ordre du scénario, jauges vides
    /// sauf Départ canon),
    /// tirées du catalogue <paramref name="units"/> ou, pour les héros, de <paramref name="heroes"/> ; puis les héros des
    /// joueurs <paramref name="deployed"/>, sur les cases <see cref="HeroSlots"/> dans l'ordre (cf.
    /// <see cref="DeployedUnitId"/>). Refuse une unité ou un héros inconnu, ou dont les points de stats portent
    /// l'initiative au-delà de la capacité de la jauge, et plus de héros déployés que de cases.
    /// </summary>
    public CombatSetup ToSetup(CombatRules rules, IReadOnlyDictionary<string, UnitDefinition> units, HeroCatalog? heroes = null,
        IReadOnlyList<HeroSheet>? deployed = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(units);
        // Les archétypes des héros et les unités placées telles quelles doivent venir du même catalogue.
        if (heroes is not null && !ReferenceEquals(heroes.Units, units))
            throw new ArgumentException($"Scenario '{Id}': the heroes were built from another unit catalog.");
        try
        {
            var placed = Units.Select((unit, index) => Place(unit, new UnitId(index + 1), units, heroes, rules))
                .Concat(Deploy(deployed ?? Array.Empty<HeroSheet>(), heroes, rules))
                .ToList();
            foreach (var unit in placed)
            {
                if (rules.InitiativeWith(unit.Definition.Initiative, unit.Points ?? StatPoints.None) > rules.GaugeCapacity)
                    throw new ArgumentException(
                        $"'{unit.Definition.Id}' has an initiative above the gauge capacity ({rules.GaugeCapacity}) with its stat points.");
            }
            // Les renforts reçoivent leur identifiant à leur arrivée (celui-ci n'est qu'une place réservée).
            var waves = (Waves ?? Array.Empty<ScenarioWave>())
                .Select(w => new WaveSetup(w.AtMs, w.Units.Select(u => Place(u, default, units, heroes, rules)).ToList()))
                .ToList();
            var setup = new CombatSetup(rules, Grid.FromRows(Map!.ToArray()), placed, Seed)
            {
                Objectives = Objectives ?? ObjectiveDefinition.Default,
                Waves = waves,
                Script = Script ?? Array.Empty<ScriptTrigger>(),
            };
            // Dès le chargement des données, pas au lancement du combat.
            setup.ValidateObjectives();
            setup.ValidateScript();
            return setup;
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"Scenario '{Id}': {e.Message}", e);
        }
    }

    /// <summary>Les héros des joueurs, sur les cases prévues, dans l'ordre.</summary>
    private IEnumerable<UnitSetup> Deploy(IReadOnlyList<HeroSheet> deployed, HeroCatalog? heroes, CombatRules rules)
    {
        if (deployed.Count == 0)
            yield break;
        var slots = HeroSlots ?? Array.Empty<Cell>();
        if (deployed.Count > slots.Count)
            throw new ArgumentException($"{deployed.Count} heroes deployed, {slots.Count} hero slots.");
        if (heroes is null)
            throw new ArgumentException("this content has no heroes to deploy.");
        for (var i = 0; i < deployed.Count; i++)
        {
            var hero = heroes.Unit(deployed[i]);
            var gauge = hero.Definition.Passive == PassiveKind.FlyingStart ? rules.GaugeCapacity : 0;
            yield return hero.Definition.ToSetup(DeployedUnitId(i), CombatSimulation.PlayerTeam, slots[i], gauge, hero.Points);
        }
    }

    /// <summary>
    /// Une unité du scénario, jauge vide ; un héros avec Départ canon commence jauge pleine (un script de mission peut
    /// toujours fixer une autre jauge de départ dans le <see cref="UnitSetup"/>).
    /// </summary>
    private static UnitSetup Place(ScenarioUnit unit, UnitId id, IReadOnlyDictionary<string, UnitDefinition> units, HeroCatalog? heroes,
        CombatRules rules)
    {
        var cell = new Cell(unit.X, unit.Y);
        if (unit.Hero is { } heroId)
        {
            var hero = (heroes ?? throw new ArgumentException($"{unit.Label}: this content has no heroes.")).Unit(heroId);
            var gauge = hero.Definition.Passive == PassiveKind.FlyingStart ? rules.GaugeCapacity : 0;
            // Un héros n'est ni poursuivi ni protégé : les marques passent pour que la vérification des objectifs le refuse.
            return WithBehavior(hero.Definition, unit.Behavior).ToSetup(id, unit.Team, cell, gauge, hero.Points) with
            {
                IsPursued = unit.Pursued, IsProtected = unit.Protected, Tag = unit.Tag,
            };
        }
        if (!units.TryGetValue(unit.Unit!, out var definition))
            throw new ArgumentException($"unknown {unit.Label}.");
        if (unit.Leader is not null && definition.IsCivilian)
            throw new ArgumentException($"{unit.Label} is a civilian, it cannot lead minions.");
        return WithBehavior(definition, unit.Behavior).ToSetup(id, unit.Team, cell) with
        {
            MinionsOnFall = unit.Leader, IsPursued = unit.Pursued, IsProtected = unit.Protected, Tag = unit.Tag,
        };
    }

    /// <summary>
    /// Le type d'unité avec le comportement que lui donne le scénario (<c>null</c> : le sien), revérifié : comportement
    /// connu, et compatible avec le type (un civil fuit, une structure est inerte…). Vaut aussi pour les renforts.
    /// </summary>
    private static UnitDefinition WithBehavior(UnitDefinition definition, string? behavior)
    {
        if (behavior is null)
            return definition;
        var changed = definition with { Behavior = behavior };
        changed.Validate();
        return changed;
    }
}
