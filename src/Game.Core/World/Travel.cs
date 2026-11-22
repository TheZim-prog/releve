using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Progression;

namespace Game.Core.World;

/// <summary>
/// Un trajet vers une quête (docs/design.md 4) : le lieu où se trouve le groupe, le chemin parcouru, les crochets faits
/// (la gravité de la mission), le bonus gagné pour la quête, et l'événement du point d'intérêt où il vient d'arriver,
/// en attente d'un choix. Gardé dans la sauvegarde (cf. <see cref="CampaignState.Trip"/>) avec les héros partis, le
/// joueur qui décide et l'état du hasard de ses tests : un trajet interrompu reprend là où il en était.
/// </summary>
public sealed record Trip
{
    /// <summary>Les héros de campagne partis, dans l'ordre de leurs cases de départ.</summary>
    public IReadOnlyList<string> Heroes { get; init; } = Array.Empty<string>();

    /// <summary>L'identifiant du joueur qui a choisi la quête : il valide les pas et les choix (docs/design.md 4.2b).</summary>
    public string? Chooser { get; init; }

    /// <summary>L'état du générateur des tests (cf. <see cref="DeterministicRandom"/>), qui avance à chaque test tenté.</summary>
    public ulong RandomState { get; init; }

    public required string Quest { get; init; }

    /// <summary>Le lieu de la mission.</summary>
    public required string Site { get; init; }

    public required string Current { get; init; }

    public required IReadOnlyList<string> Path { get; init; }

    /// <summary>Crochets faits : chaque point d'intérêt traversé ajoute un niveau de gravité.</summary>
    public int Gravity { get; init; }

    public QuestModifiers Modifiers { get; init; } = QuestModifiers.None;

    /// <summary>L'événement à résoudre avant de repartir, ou <c>null</c>.</summary>
    public string? PendingEvent { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Arrived => Current == Site;
}

/// <summary>
/// Ce qui s'est passé à un événement : l'option choisie (<c>null</c> : il a passé son chemin), le héros qui a tenté le
/// test, sa réussite, et les effets appliqués.
/// </summary>
public sealed record EventOutcome(string Event, string? Option, string? TestedHero, bool? Success, EventEffects Effects);

/// <summary>Un héros déployé, tel que la route le voit : sa campagne et son unité de combat (pour les tests).</summary>
public sealed record Traveller(CampaignHero Hero, HeroUnit Unit);

/// <summary>
/// La route vers une quête (docs/design.md 4) : pas de déplacement libre, des chemins entre le QG, les lieux de mission
/// et les points d'intérêt. Aller d'un lieu à un lieu voisin ; passer par un point d'intérêt est un crochet (+1 gravité,
/// même épuisé) et, s'il n'est pas épuisé, déclenche son événement. Les lieux voisins de ceux où passe le groupe sont
/// découverts et le restent.
/// </summary>
public static class Travel
{
    /// <summary>Le départ du QG vers le lieu de la quête ; ses voisins sont découverts.</summary>
    public static (CampaignState State, Trip Trip) Start(CampaignState state, WorldMap map, string quest, string site)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        var trip = new Trip { Quest = quest, Site = site, Current = map.Hq.Id, Path = new[] { map.Hq.Id } };
        return (Discover(state, map, map.Hq.Id), trip);
    }

    /// <summary>
    /// Les lieux où le groupe peut aller : voisins, pas encore parcourus, ni le QG, et d'où le lieu de la mission reste
    /// joignable sans revenir sur ses pas (pas de cul-de-sac) ; aucun tant qu'un événement attend.
    /// </summary>
    public static IReadOnlyList<MapNode> Moves(WorldMap map, Trip trip)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(trip);
        if (trip.PendingEvent is not null || trip.Arrived)
            return Array.Empty<MapNode>();
        return map.Neighbors(trip.Current)
            .Where(n => n.Kind != MapNodeKind.Hq && !trip.Path.Contains(n.Id) && Reaches(map, n.Id, trip.Site, trip.Path))
            .ToList();
    }

    /// <summary>Le lieu <paramref name="to"/> est joignable depuis <paramref name="from"/> sans passer par le QG ni le chemin parcouru.</summary>
    private static bool Reaches(WorldMap map, string from, string to, IReadOnlyList<string> path)
    {
        var seen = new HashSet<string>(path) { from, map.Hq.Id };
        var frontier = new Queue<string>(new[] { from });
        while (frontier.TryDequeue(out var id))
        {
            if (id == to)
                return true;
            foreach (var next in map.Neighbors(id).Where(n => seen.Add(n.Id)))
                frontier.Enqueue(next.Id);
        }
        return false;
    }

    /// <summary>
    /// Le groupe va à ce lieu voisin, ou <c>null</c> si ce n'est pas un pas permis. Un point d'intérêt est un crochet, et
    /// son événement attend s'il n'est pas épuisé.
    /// </summary>
    public static (CampaignState State, Trip Trip)? Move(CampaignState state, WorldMap map, Trip trip, string to)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Moves(map, trip).FirstOrDefault(n => n.Id == to) is not { } node)
            return null;
        var poi = node.Kind == MapNodeKind.Poi;
        var next = trip with
        {
            Current = node.Id,
            Path = trip.Path.Append(node.Id).ToList(),
            Gravity = trip.Gravity + (poi ? 1 : 0),
            PendingEvent = poi && !state.ExhaustedPois.Contains(node.Id) ? node.Event : null,
        };
        var known = poi && !state.KnownPois.Contains(node.Id) ? state with { KnownPois = state.KnownPois.Append(node.Id).ToList() } : state;
        return (Discover(known, map, node.Id), next);
    }

    /// <summary>
    /// Les options de l'événement que ces héros (vivants) peuvent choisir : une option réservée demande l'un de ses
    /// archétypes ; une option qui coûte des matériaux demande que le pot puisse payer (même si son test échoue).
    /// </summary>
    public static IReadOnlyList<EventOption> Options(WorldEvent worldEvent, IReadOnlyList<Traveller> travellers, CampaignState state)
    {
        ArgumentNullException.ThrowIfNull(worldEvent);
        ArgumentNullException.ThrowIfNull(state);
        var alive = Alive(travellers, state);
        return worldEvent.Options
            .Where(o => o.RequiresArchetype.Count == 0 || alive.Any(t => o.RequiresArchetype.Contains(t.Hero.Sheet.Archetype)))
            .Where(o => Affordable(o.Success, state) && Affordable(o.Failure, state))
            .ToList();
    }

    private static bool Affordable(EventEffects effects, CampaignState state) =>
        effects.Materials.All(m => m.Value >= 0 || state.Materials.GetValueOrDefault(m.Key) + m.Value >= 0);

    /// <summary>Les héros du trajet encore en vie dans cet état de la campagne (un événement a pu en tuer un).</summary>
    private static IReadOnlyList<Traveller> Alive(IReadOnlyList<Traveller> travellers, CampaignState state)
    {
        ArgumentNullException.ThrowIfNull(travellers);
        return travellers.Where(t => state.Heroes.Any(h => h.Id == t.Hero.Id && !h.IsDead)).ToList();
    }

    /// <summary>Le héros qui tenterait ce test (le plus de chance), et sa chance, ramenée dans les bornes.</summary>
    public static (Traveller Hero, int Chance) BestAt(EventTest test, IReadOnlyList<Traveller> travellers, CombatRules combat, WorldRules rules)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(rules);
        if (travellers is not { Count: > 0 })
            throw new ArgumentException("A test needs at least one deployed hero.", nameof(travellers));
        return travellers
            .Select(t => (Hero: t, Chance: rules.Clamp(ChanceOf(test, t, combat))))
            .OrderByDescending(c => c.Chance)
            .First();
    }

    private static int ChanceOf(EventTest test, Traveller traveller, CombatRules combat)
    {
        var definition = traveller.Unit.Definition;
        var points = traveller.Unit.Points;
        var value = test.Stat switch
        {
            TestStat.Hp => combat.MaxHpWith(definition.MaxHp, points),
            TestStat.Power => combat.PowerPercentWith(points),
            TestStat.Initiative => combat.InitiativeWith(definition.Initiative, points),
            _ => definition.Mobility,
        };
        return test.BaseChance + test.PercentPerUnit * (value - test.Threshold)
               + test.ArchetypeBonus.GetValueOrDefault(traveller.Hero.Sheet.Archetype);
    }

    /// <summary>
    /// Résout l'événement en attente : l'option <paramref name="option"/> (son rang parmi <see cref="Options"/>), ou
    /// <c>null</c> pour passer son chemin. Un test réussit si <paramref name="roll"/> (de 0 à 99, tiré par le serveur) est
    /// sous la chance du meilleur héros. Le point d'intérêt est alors épuisé, et les effets appliqués : à la campagne
    /// (matériaux, réputation dans ses bornes, chutes) et au trajet (bonus de quête). <c>null</c> si rien n'attend ou si
    /// l'option n'existe pas.
    /// </summary>
    public static (CampaignState State, Trip Trip, EventOutcome Outcome)? Resolve(CampaignState state, Trip trip, WorldEvent worldEvent,
        int? option, int roll, IReadOnlyList<Traveller> travellers, Context context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(worldEvent);
        ArgumentNullException.ThrowIfNull(context);
        if (trip.PendingEvent != worldEvent.Id)
            return null;
        var exhausted = state with { ExhaustedPois = state.ExhaustedPois.Append(trip.Current).ToList() };
        var done = trip with { PendingEvent = null };
        if (option is not { } index)
            return (exhausted, done, new EventOutcome(worldEvent.Id, null, null, null, EventEffects.None));
        var options = Options(worldEvent, travellers, state);
        var alive = Alive(travellers, state);
        if (index < 0 || index >= options.Count || (options[index].Test is not null && alive.Count == 0))
            return null;

        var chosen = options[index];
        Traveller? tested = null;
        bool? success = null;
        if (chosen.Test is { } test)
        {
            var (hero, chance) = BestAt(test, alive, context.Combat, context.World);
            tested = hero;
            success = roll < chance;
        }
        var effects = success == false ? chosen.Failure : chosen.Success;
        var hurt = tested is not null ? new[] { tested.Hero.Id } : alive.Select(t => t.Hero.Id).ToArray();
        var after = Apply(exhausted, effects, hurt, context);
        return (after, done with { Modifiers = done.Modifiers.Plus(effects.Quest) },
            new EventOutcome(worldEvent.Id, chosen.Label, tested?.Hero.Name, success, effects));
    }

    /// <summary>Les règles dont la route a besoin : combat (stats des héros), monde (tests), progression (chutes), réputation.</summary>
    public sealed record Context(CombatRules Combat, WorldRules World, ProgressionRules Progression, Reputation.ReputationScale? Reputation);

    private static CampaignState Apply(CampaignState state, EventEffects effects, IReadOnlyCollection<string> hurt, Context context)
    {
        var materials = new Dictionary<string, int>(state.Materials);
        foreach (var (material, amount) in effects.Materials)
            materials[material] = Math.Max(0, materials.GetValueOrDefault(material) + amount);
        var reputation = context.Reputation is { } scale ? scale.Clamp(state.Reputation + effects.Reputation) : state.Reputation;
        return Injuries.Hurt(state with { Materials = materials, Reputation = reputation }, hurt, effects.HalfFalls, context.Progression);
    }

    /// <summary>Les points d'intérêt voisins de ce lieu deviennent connus.</summary>
    private static CampaignState Discover(CampaignState state, WorldMap map, string at)
    {
        var seen = map.Neighbors(at).Where(n => n.Kind == MapNodeKind.Poi && !state.KnownPois.Contains(n.Id)).Select(n => n.Id).ToList();
        return seen.Count == 0 ? state : state with { KnownPois = state.KnownPois.Concat(seen).ToList() };
    }
}
