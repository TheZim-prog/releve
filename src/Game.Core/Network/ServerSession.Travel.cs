using Game.Core.Campaign;
using Game.Core.World;

namespace Game.Core.Network;

// Le trajet d'une quête (docs/design.md 4) : entre son lancement au QG et son combat, chaque groupe va de lieu en lieu
// sur la carte du monde. Son chef valide les pas et les choix des événements (4.2b) ; s'il n'est pas là, le premier
// joueur connecté du groupe le remplace, pour que le groupe ne reste jamais bloqué. Les trajets sont dans la
// sauvegarde : un serveur qui redémarre reforme leurs groupes, et chaque joueur y retrouve son héros parti.
public sealed partial class ServerSession
{
    /// <summary>Ce qu'a donné le dernier événement du trajet de chaque joueur (montré jusqu'à son pas suivant), par identifiant.</summary>
    private readonly Dictionary<string, EventOutcome> _lastEvents = new();

    /// <summary>La quête se rejoint par la carte : il y a une carte, et la quête y a son lieu.</summary>
    private bool HasRoute(QuestDefinition quest) => _content.World is not null && _content.WorldRules is not null && quest.Site is not null;

    /// <summary>Le joueur qui décide en route : le chef s'il est là, sinon le premier joueur connecté du groupe.</summary>
    private Player? ValidatorOf(Party party) =>
        _players.Find(p => p.Uid == party.Leader && p.Peer is not null)
        ?? party.Members.Select(uid => _players.Find(p => p.Uid == uid && p.Peer is not null)).FirstOrDefault(p => p is not null);

    /// <summary>
    /// Au lancement du serveur, chaque trajet de la sauvegarde reforme son groupe (son chef, et les joueurs de ses
    /// héros) ; un trajet qui ne peut plus continuer (les données ont changé : quête, lieu ou événement disparus, lieu de
    /// la mission plus joignable) est abandonné.
    /// </summary>
    private void ResumeTrips()
    {
        if (_campaign is null)
            return;
        foreach (var trip in _campaign.State.Trips)
        {
            var quest = _content.Quests.GetValueOrDefault(trip.Quest);
            var usable = quest is not null && HasRoute(quest) && quest.Site == trip.Site && _content.World!.Node(trip.Current) is not null
                         && (trip.PendingEvent is { } e ? _content.Events.ContainsKey(e) : Travel.Moves(_content.World, trip).Count > 0)
                         && trip.Heroes.All(id => _campaign.State.Heroes.Any(h => h.Id == id));
            if (!usable)
            {
                _campaign.Update(state => state.WithTrip(trip.Quest, null));
                continue;
            }
            var owners = OwnersOf(trip.Heroes.Select(id => _campaign.State.Heroes.First(h => h.Id == id)));
            var party = new Party(_nextPartyId++, quest!, trip.Chooser ?? owners[0]);
            party.Members.AddRange(owners.Where(uid => uid != party.Leader));
            _parties.Add(party);
        }
    }

    /// <summary>
    /// Le groupe quitte le QG vers le lieu de sa quête, à la date la plus avancée de ses joueurs (les voisins du QG sont
    /// découverts) ; le trajet est sauvegardé.
    /// </summary>
    private void Depart(Player leader, Party party, IReadOnlyList<CampaignHero> heroes)
    {
        var quest = party.Quest;
        var (state, trip) = Travel.Start(Leaving(_campaign!.State, quest, heroes), _content.World!, quest.Id, quest.Site!);
        trip = trip with
        {
            Heroes = heroes.Select(h => h.Id).ToList(),
            Chooser = party.Leader,
            RandomState = (ulong)_random.NextInt64(),
        };
        SaveOrTell(leader, _ => state.WithTrip(quest.Id, trip));
        ForgetRoadNews(party);
        BroadcastLobby();
    }

    private void TravelFor(Player player, string? place)
    {
        if (!OnTheRoad(player, out var party, out var trip))
            return;
        if (place is null || Travel.Move(_campaign!.State, _content.World!, trip, place) is not { } moved)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotAMove));
            return;
        }
        // Arrivé, le trajet s'efface de la sauvegarde : quoi qu'il arrive au lancement du combat, rien ne reste bloqué.
        var arrived = moved.Trip.Arrived;
        SaveOrTell(player, _ => moved.State.WithTrip(party.Quest.Id, arrived ? null : moved.Trip));
        ForgetRoadNews(party);
        if (arrived)
            Arrive(party, moved.Trip);
        else
            BroadcastLobby();
    }

    private void ResolveFor(Player player, int? option)
    {
        if (!OnTheRoad(player, out var party, out var trip))
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
        // Tous les héros sont morts en route : il n'y a plus personne pour la quête, le trajet s'arrête (cf. 4.2b).
        var anyoneLeft = trip.Heroes.Any(h => resolved.State.Heroes.Any(c => c.Id == h && !c.IsDead));
        SaveOrTell(player, _ => resolved.State.WithTrip(party.Quest.Id, anyoneLeft ? resolved.Trip with { RandomState = random.State } : null));
        foreach (var uid in party.Members)
            _lastEvents[uid] = resolved.Outcome;
        UndeployTheDead();
        if (!anyoneLeft)
            Disband(party);
        BroadcastLobby();
    }

    /// <summary>Le joueur peut décider sur la route : son groupe est en route, et c'est lui qui valide.</summary>
    private bool OnTheRoad(Player player, out Party party, out Trip trip)
    {
        party = PartyOf(player.Uid)!;
        trip = party is null ? null! : _campaign?.State.TripTo(party.Quest.Id)!;
        if (party is null || trip is null)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotTravelling));
        else if (ValidatorOf(party) != player)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotTheValidator));
        return party is not null && trip is not null && ValidatorOf(party) == player;
    }

    /// <summary>
    /// Arrivé au lieu de la quête : le combat se lance avec les héros encore en vie (ceux dont le joueur n'est pas là
    /// sont joués par l'IA), la gravité des crochets (celle de la quête, sinon la règle de son objectif principal) et le
    /// bonus gagné en route. S'il ne peut pas se lancer, tout le groupe sait pourquoi, et rentre.
    /// </summary>
    private void Arrive(Party party, Trip trip)
    {
        UndeployTheDead();
        var quest = party.Quest;
        var travelled = trip.Heroes.Select(id => _campaign!.State.Heroes.First(h => h.Id == id)).ToList();
        var heroes = travelled.Where(h => !h.IsDead).ToList();
        if (heroes.Count > 0)
        {
            var objectives = _content.Scenarios[quest.Scenario].Objectives ?? Objectives.ObjectiveDefinition.Default;
            var launch = LaunchOf(quest, heroes) with
            {
                Gravity = quest.Gravity ?? _content.WorldRules!.GravityFor(objectives),
                GravityLevel = trip.Gravity,
                Modifiers = trip.Modifiers,
            };
            var members = _players.Where(p => party.Members.Contains(p.Uid)).ToList();
            if (Build(launch, reason => members.ForEach(p => p.Peer?.Send(new RequestRefused(reason)))) is { } setup)
            {
                // Les joueurs de tous les héros partis, même morts en route : ils ont fait le voyage.
                LaunchQuest(party, heroes, launch, setup, OwnersOf(travelled));
                return;
            }
        }
        Disband(party);
        BroadcastLobby();
    }

    /// <summary>Le groupe rentre sans avoir joué sa quête : il se dissout, et la quête revient au panneau.</summary>
    private void Disband(Party party)
    {
        _parties.Remove(party);
        ForgetTheAbsent();
    }

    /// <summary>Le groupe repart : le dernier événement n'est plus d'actualité.</summary>
    private void ForgetRoadNews(Party party)
    {
        foreach (var uid in party.Members)
            _lastEvents.Remove(uid);
    }

    /// <summary>Le héros que ce joueur a sur un trajet en cours, ou <c>null</c>.</summary>
    private string? HeroOnTheRoad(string uid) =>
        _campaign?.State.Trips.SelectMany(t => t.Heroes)
            .FirstOrDefault(id => _campaign.State.Heroes.Any(h => h.Id == id && h.Owner == uid && !h.IsDead));

    /// <summary>Les héros vivants du trajet, tels que la route les voit (fiche de campagne, unité de combat).</summary>
    private IReadOnlyList<Traveller> Travellers(Trip trip) =>
        trip.Heroes
            .Select(id => _campaign!.State.Heroes.First(h => h.Id == id))
            .Where(h => !h.IsDead)
            .Select(h => new Traveller(h, _content.Heroes!.Unit(h.Sheet)))
            .ToList();

    private Travel.Context RouteContext() =>
        new(_content.Rules, _content.WorldRules!, _content.Heroes!.Rules, _content.Reputation?.Scale);

    /// <summary>Le trajet du groupe, tel que ses joueurs le voient (les options possibles, avec le libellé de leur test), ou <c>null</c>.</summary>
    private TripInfo? TripSummary(Party party)
    {
        if (_campaign?.State.TripTo(party.Quest.Id) is not { } trip)
            return null;
        var travellers = Travellers(trip);
        EventInfo? pending = null;
        if (trip.PendingEvent is { } id && _content.Events.GetValueOrDefault(id) is { } worldEvent)
        {
            var options = Travel.Options(worldEvent, travellers, _campaign.State)
                .Select(o => new OptionInfo(o.Label, o.Test is { } test && travellers.Count > 0
                    ? _content.WorldRules!.LabelOf(Travel.BestAt(test, travellers, _content.Rules, _content.WorldRules).Chance)
                    : null))
                .ToList();
            pending = new EventInfo(id, options);
        }
        return new TripInfo
        {
            Quest = trip.Quest, Site = trip.Site, Current = trip.Current, Path = trip.Path, Gravity = trip.Gravity,
            Modifiers = trip.Modifiers, Validator = ValidatorOf(party)?.Id,
            Moves = Travel.Moves(_content.World!, trip).Select(n => n.Id).ToList(),
            Event = pending,
        };
    }
}
