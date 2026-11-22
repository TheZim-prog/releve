using Game.Core.Campaign;
using Game.Core.World;

namespace Game.Core.Network;

// Le trajet d'une quête (docs/design.md 4) : entre son lancement au QG et son combat, le groupe va de lieu en lieu sur la
// carte du monde. Le joueur qui a choisi la quête valide les pas et les choix des événements (4.2b) ; s'il n'est pas là,
// le premier joueur connecté le remplace, pour que le groupe ne reste jamais bloqué. Le trajet est dans la sauvegarde :
// un serveur qui redémarre le reprend là où il en était, et chaque joueur y retrouve son héros parti.
public sealed partial class ServerSession
{
    /// <summary>L'identifiant du joueur qui a choisi la quête, ou <c>null</c>.</summary>
    private string? _questChooser;

    /// <summary>Ce qu'a donné le dernier événement du trajet (montré au QG jusqu'au trajet suivant), ou <c>null</c>.</summary>
    private EventOutcome? _lastEvent;

    private Trip? CurrentTrip => _campaign?.State.Trip;

    /// <summary>Un trajet ou un combat est en cours : le QG attend, et un joueur qui part garde sa place.</summary>
    private bool InProgress => Simulation is not null || CurrentTrip is not null;

    private Player? Validator =>
        _players.Find(p => p.Uid == CurrentTrip?.Chooser && p.Peer is not null) ?? _players.Find(p => p.Peer is not null);

    /// <summary>La quête se rejoint par la carte : il y a une carte, et la quête y a son lieu.</summary>
    private bool HasRoute(QuestDefinition quest) => _content.World is not null && _content.WorldRules is not null && quest.Site is not null;

    /// <summary>
    /// Au lancement du serveur, le trajet de la sauvegarde reprend ; s'il ne peut plus continuer (les données ont changé :
    /// quête, lieu ou événement disparus, lieu de la mission plus joignable), il est abandonné.
    /// </summary>
    private void ResumeTrip()
    {
        if (CurrentTrip is not { } trip)
            return;
        var quest = _content.Quests.GetValueOrDefault(trip.Quest);
        var usable = quest is not null && HasRoute(quest) && quest.Site == trip.Site && _content.World!.Node(trip.Current) is not null
                     && (trip.PendingEvent is { } e ? _content.Events.ContainsKey(e) : Travel.Moves(_content.World, trip).Count > 0);
        if (usable)
            _questId = trip.Quest;
        else
            _campaign!.Update(state => state with { Trip = null });
    }

    /// <summary>Le groupe quitte le QG vers le lieu de la quête (ses voisins sont découverts) ; le trajet est sauvegardé.</summary>
    private void Depart(Player player, QuestDefinition quest, IReadOnlyList<CampaignHero> heroes)
    {
        var (state, trip) = Travel.Start(_campaign!.State, _content.World!, quest.Id, quest.Site!);
        trip = trip with
        {
            Heroes = heroes.Select(h => h.Id).ToList(),
            Chooser = _questChooser ?? player.Uid,
            RandomState = (ulong)_random.NextInt64(),
        };
        SaveOrTell(player, _ => state with { Trip = trip });
        _lastEvent = null;
        BroadcastLobby();
    }

    private void TravelFor(Player player, string? place)
    {
        if (!OnTheRoad(player, out var trip))
            return;
        if (place is null || Travel.Move(_campaign!.State, _content.World!, trip, place) is not { } moved)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotAMove));
            return;
        }
        // Arrivé, le trajet s'efface de la sauvegarde : quoi qu'il arrive au lancement du combat, le QG ne reste pas bloqué.
        var arrived = moved.Trip.Arrived;
        SaveOrTell(player, _ => moved.State with { Trip = arrived ? null : moved.Trip });
        _lastEvent = null;
        if (arrived)
            Arrive(moved.Trip);
        else
            BroadcastLobby();
    }

    private void ResolveFor(Player player, int? option)
    {
        if (!OnTheRoad(player, out var trip))
            return;
        var worldEvent = trip.PendingEvent is { } id ? _content.Events.GetValueOrDefault(id) : null;
        var travellers = Travellers(trip);
        // Le hasard n'avance que pour un test vraiment tenté : un choix refusé ou sans test ne change pas les suivants.
        var random = new DeterministicRandom(trip.RandomState);
        var options = worldEvent is null ? Array.Empty<EventOption>() : Travel.Options(worldEvent, travellers, _campaign!.State);
        var roll = option is { } index && index >= 0 && index < options.Count && options[index].Test is not null ? random.NextInt(0, 99) : 0;
        if (worldEvent is null
            || Travel.Resolve(_campaign!.State, trip, worldEvent, option, roll, travellers, RouteContext()) is not { } resolved)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownOption));
            return;
        }
        // Tous les héros sont morts en route : il n'y a plus personne pour la quête, le trajet s'arrête.
        var anyoneLeft = trip.Heroes.Any(h => resolved.State.Heroes.Any(c => c.Id == h && !c.IsDead));
        SaveOrTell(player, _ => resolved.State with { Trip = anyoneLeft ? resolved.Trip with { RandomState = random.State } : null });
        _lastEvent = resolved.Outcome;
        UndeployTheDead();
        if (!anyoneLeft)
            ForgetTheAbsent();
        BroadcastLobby();
    }

    /// <summary>Le joueur peut décider sur la route : un trajet est en cours, et c'est lui qui valide.</summary>
    private bool OnTheRoad(Player player, out Trip trip)
    {
        trip = CurrentTrip!;
        if (CurrentTrip is null)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotTravelling));
        else if (Validator != player)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotTheValidator));
        return CurrentTrip is not null && Validator == player;
    }

    /// <summary>
    /// Arrivé au lieu de la quête : le combat se lance avec les héros encore en vie (ceux dont le joueur n'est pas là
    /// sont joués par l'IA), la gravité des crochets (celle de la quête, sinon la règle de son objectif principal) et le
    /// bonus gagné en route. S'il ne peut pas se lancer, tout le monde sait pourquoi, et le groupe rentre.
    /// </summary>
    private void Arrive(Trip trip)
    {
        UndeployTheDead();
        var heroes = trip.Heroes.Select(id => _campaign!.State.Heroes.First(h => h.Id == id)).Where(h => !h.IsDead).ToList();
        if (ChosenQuest is { } quest && heroes.Count > 0)
        {
            var objectives = _content.Scenarios[quest.Scenario].Objectives ?? Objectives.ObjectiveDefinition.Default;
            var launch = LaunchOf(quest, heroes) with
            {
                Gravity = quest.Gravity ?? _content.WorldRules!.GravityFor(objectives),
                GravityLevel = trip.Gravity,
                Modifiers = trip.Modifiers,
            };
            if (Build(launch, reason => Broadcast(new RequestRefused(reason))) is { } setup)
            {
                LaunchQuest(quest, heroes, launch, setup);
                return;
            }
        }
        ForgetTheAbsent();
        BroadcastLobby();
    }

    /// <summary>Hors combat et hors trajet, les joueurs partis entre-temps quittent la partie (comme à la fin d'un combat).</summary>
    private void ForgetTheAbsent()
    {
        if (!InProgress)
            _players.RemoveAll(p => p.Peer is null);
    }

    /// <summary>Un héros mort en route ne part plus (son joueur reste au QG).</summary>
    private void UndeployTheDead()
    {
        var dead = _campaign!.State.Heroes.Where(h => h.IsDead).Select(h => h.Id).ToHashSet();
        foreach (var player in _players.Where(p => p.Deployed is { } hero && dead.Contains(hero)))
            player.Deployed = null;
    }

    /// <summary>Le héros que ce joueur a sur le trajet en cours, ou <c>null</c>.</summary>
    private string? HeroOnTheRoad(string uid) =>
        CurrentTrip?.Heroes.FirstOrDefault(id => _campaign!.State.Heroes.Any(h => h.Id == id && h.Owner == uid && !h.IsDead));

    /// <summary>Les héros vivants du trajet, tels que la route les voit (fiche de campagne, unité de combat).</summary>
    private IReadOnlyList<Traveller> Travellers(Trip trip) =>
        trip.Heroes
            .Select(id => _campaign!.State.Heroes.First(h => h.Id == id))
            .Where(h => !h.IsDead)
            .Select(h => new Traveller(h, _content.Heroes!.Unit(h.Sheet)))
            .ToList();

    private Travel.Context RouteContext() =>
        new(_content.Rules, _content.WorldRules!, _content.Heroes!.Rules, _content.Reputation?.Scale);

    /// <summary>Le trajet en cours, tel que le QG le montre (les options possibles, avec le libellé de leur test).</summary>
    private TripInfo? TripSummary()
    {
        if (CurrentTrip is not { } trip)
            return null;
        var travellers = Travellers(trip);
        EventInfo? pending = null;
        if (trip.PendingEvent is { } id && _content.Events.GetValueOrDefault(id) is { } worldEvent)
        {
            var options = Travel.Options(worldEvent, travellers, _campaign!.State)
                .Select(o => new OptionInfo(o.Label, o.Test is { } test && travellers.Count > 0
                    ? _content.WorldRules!.LabelOf(Travel.BestAt(test, travellers, _content.Rules, _content.WorldRules).Chance)
                    : null))
                .ToList();
            pending = new EventInfo(id, options);
        }
        return new TripInfo
        {
            Quest = trip.Quest, Site = trip.Site, Current = trip.Current, Path = trip.Path, Gravity = trip.Gravity,
            Modifiers = trip.Modifiers, Validator = Validator?.Id,
            Moves = Travel.Moves(_content.World!, trip).Select(n => n.Id).ToList(),
            Event = pending,
        };
    }
}
