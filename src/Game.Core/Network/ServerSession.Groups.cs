using Game.Core.Campaign;
using Game.Core.Combat;

namespace Game.Core.Network;

// Les groupes du QG (docs/design.md 4.3) : un joueur accepte une quête de son panneau et en devient le chef ; d'autres le
// rejoignent tant qu'il reste de la place ; le chef lance quand il est prêt. Plusieurs groupes vivent en même temps,
// chacun sa quête (prise tant qu'il l'a), son trajet et son combat. Le chef qui quitte son groupe avant le départ le
// dissout, et la quête revient au panneau ; une quête jouée dissout son groupe.
public sealed partial class ServerSession
{
    private readonly List<Party> _parties = new();
    private int _nextPartyId = 1;

    /// <summary>
    /// Un groupe : sa quête, son chef (identifiant du joueur qui l'a acceptée) et ses joueurs dans l'ordre d'arrivée (le
    /// chef d'abord) ; son combat quand il la joue. Son trajet est dans la campagne (<see cref="CampaignState.Trips"/>).
    /// </summary>
    private sealed class Party(int id, QuestDefinition quest, string leader)
    {
        public int Id { get; } = id;
        public QuestDefinition Quest { get; } = quest;
        public string Leader { get; } = leader;
        public List<string> Members { get; } = new() { leader };
        public CombatRoom? Room { get; set; }
    }

    private Party? PartyOf(string uid) => _parties.Find(p => p.Members.Contains(uid));

    private GroupPhase PhaseOf(Party party) =>
        party.Room is not null ? GroupPhase.Fighting
        : _campaign?.State.TripTo(party.Quest.Id) is not null ? GroupPhase.Travelling
        : GroupPhase.Forming;

    /// <summary>Les quêtes que des groupes ont acceptées : prises, elles ne sont plus au panneau des autres.</summary>
    private IReadOnlyCollection<string> TakenQuests => _parties.Select(p => p.Quest.Id).ToList();

    private int SlotsOf(QuestDefinition quest) => _content.Scenarios[quest.Scenario].HeroSlots!.Count;

    /// <summary>Le joueur accepte une quête de son panneau (parue à sa date, ni jouée ni prise) : il forme un groupe.</summary>
    private void AcceptFor(Player player, string? questId)
    {
        if (!InCampaignLobby(player))
            return;
        if (PartyOf(player.Uid) is not null)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.AlreadyInGroup));
            return;
        }
        if (questId is not null && TakenQuests.Contains(questId))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.QuestTaken));
            return;
        }
        if (Calendar.Board(_campaign!.State, _content.Quests.Values, player.Uid).FirstOrDefault(q => q.Id == questId) is not { } quest)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownQuest));
            return;
        }
        _parties.Add(new Party(_nextPartyId++, quest, player.Uid));
        BroadcastLobby();
    }

    /// <summary>Le joueur rejoint un groupe qui se forme au QG, s'il y reste de la place (un héros par joueur).</summary>
    private void JoinGroupFor(Player player, int groupId)
    {
        if (!InCampaignLobby(player))
            return;
        if (PartyOf(player.Uid) is not null)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.AlreadyInGroup));
            return;
        }
        if (_parties.Find(p => p.Id == groupId) is not { } party || PhaseOf(party) != GroupPhase.Forming)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownGroup));
            return;
        }
        if (party.Members.Count >= SlotsOf(party.Quest))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.TooManyHeroes));
            return;
        }
        party.Members.Add(player.Uid);
        BroadcastLobby();
    }

    private void LeaveGroupFor(Player player)
    {
        if (!InCampaignLobby(player))
            return;
        if (PartyOf(player.Uid) is null)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInGroup));
            return;
        }
        Leave(player);
        BroadcastLobby();
    }

    /// <summary>Le joueur quitte le groupe qui se forme (s'il en a un) ; le chef le dissout.</summary>
    private void Leave(Player player)
    {
        if (PartyOf(player.Uid) is not { } party || PhaseOf(party) != GroupPhase.Forming)
            return;
        if (party.Leader == player.Uid)
            _parties.Remove(party);
        else
            party.Members.Remove(player.Uid);
    }

    /// <summary>
    /// Le chef lance la quête de son groupe avec les héros déployés de ses joueurs : le trajet sur la carte, s'il y en a
    /// une, sinon le combat tout de suite ; ou il sait pourquoi c'est impossible. Le combat est construit avant que rien
    /// ne change : une fiche de héros que les données ne permettent plus (une compétence renommée depuis son recrutement)
    /// est refusée sans laisser de trace, et sans faire le trajet pour rien.
    /// </summary>
    private void StartQuest(Player player)
    {
        if (QuestHeroes(player) is not { } going)
            return;
        var (party, heroes) = going;
        var launch = LaunchOf(party.Quest, heroes);
        if (Build(launch, reason => player.Peer!.Send(new RequestRefused(reason))) is not { } setup)
            return;
        if (HasRoute(party.Quest))
        {
            Depart(player, party, heroes);
            return;
        }
        SaveOrTell(player, state => Leaving(state, party.Quest, heroes));
        LaunchQuest(party, heroes, launch, setup, OwnersOf(heroes));
    }

    /// <summary>
    /// Le groupe du chef qui lance, et les héros déployés de ses joueurs (dans leur ordre d'arrivée), ou <c>null</c> (le
    /// chef sait pourquoi). Rien n'est encore changé.
    /// </summary>
    private (Party Party, IReadOnlyList<CampaignHero> Heroes)? QuestHeroes(Player player)
    {
        if (!IsCampaign)
            return Refuse(player, RequestRefusal.NoCampaign);
        if (PartyOf(player.Uid) is not { } party)
            return Refuse(player, RequestRefusal.NotInGroup);
        if (PhaseOf(party) != GroupPhase.Forming)
            return Refuse(player, RequestRefusal.NotInLobby);
        if (party.Leader != player.Uid)
            return Refuse(player, RequestRefusal.NotTheLeader);
        var going = party.Members.Select(uid => _players.Find(p => p.Uid == uid)).ToList();
        if (going.Any(p => p?.Deployed is null))
            return Refuse(player, RequestRefusal.HeroesMissing);
        // Revérifiés : un héros a pu mourir entre son déploiement et le lancement.
        if (going.Select(p => HeroRefusal(p!, p!.Deployed!)).FirstOrDefault(r => r is not null) is { } refusal)
            return Refuse(player, refusal);
        return (party, going.Select(p => _campaign!.State.Heroes.First(h => h.Id == p!.Deployed)).ToList());
    }

    private static (Party, IReadOnlyList<CampaignHero>)? Refuse(Player player, RequestRefusal reason)
    {
        player.Peer!.Send(new RequestRefused(reason));
        return null;
    }

    /// <summary>
    /// Les joueurs de ces héros partent ensemble : chacun saute à la date la plus avancée d'entre eux, jamais avant la
    /// parution de la quête (cf. 4.7).
    /// </summary>
    private CampaignState Leaving(CampaignState state, QuestDefinition quest, IReadOnlyList<CampaignHero> heroes) =>
        _content.Heroes?.Rules is { } rules ? Calendar.Depart(state, OwnersOf(heroes), quest, rules) : state;

    /// <summary>Les joueurs de ces héros.</summary>
    private static IReadOnlyList<string> OwnersOf(IEnumerable<CampaignHero> heroes) => heroes.Select(h => h.Owner).Distinct().ToList();

    /// <summary>Le combat de ce lancement, ou <c>null</c> si les données ne permettent plus un héros (dit à <paramref name="tell"/>).</summary>
    private CombatSetup? Build(CombatLaunch launch, Action<RequestRefusal> tell)
    {
        try
        {
            return launch.BuildSetup(_content);
        }
        catch (ArgumentException)
        {
            tell(RequestRefusal.HeroUnavailable);
            return null;
        }
    }

    /// <summary>
    /// Le combat de la quête avec ces héros, sur les cases du scénario dans cet ordre : chacun est joué par son joueur
    /// s'il est dans la partie, sinon par l'IA.
    /// </summary>
    private CombatLaunch LaunchOf(QuestDefinition quest, IReadOnlyList<CampaignHero> heroes)
    {
        var scenario = _content.Scenarios[quest.Scenario];
        var assignments = heroes
            .Select((h, i) => _players.Find(p => p.Uid == h.Owner) is { } player ? new HeroAssignment(scenario.DeployedUnitId(i), player.Id) : null)
            .OfType<HeroAssignment>()
            .ToList();
        return new CombatLaunch(quest.Scenario, assignments, heroes.Select(h => h.Sheet).ToList());
    }

    /// <summary>Le combat de la quête du groupe commence (au départ du QG, ou à l'arrivée au bout du trajet).</summary>
    private void LaunchQuest(Party party, IReadOnlyList<CampaignHero> heroes, CombatLaunch launch, CombatSetup setup,
        IReadOnlyList<string> owners)
    {
        foreach (var assignment in launch.Heroes)
            _players.Single(p => p.Id == assignment.Player).Hero = assignment.Unit;
        var scenario = _content.Scenarios[party.Quest.Scenario];
        var fight = new QuestFight(party.Quest, heroes.Select((h, i) => (h.Id, scenario.DeployedUnitId(i))).ToList(), owners);
        foreach (var uid in party.Members)
            _lastEvents.Remove(uid); // ce que la route a donné n'appartient plus qu'à la quête d'avant
        Open(new CombatRoom(launch, setup, party, fight));
    }

    /// <summary>Les groupes, tels que le QG les montre.</summary>
    private IReadOnlyList<GroupInfo> GroupInfos(CampaignState state) =>
        _parties
            .Select(p => new GroupInfo(p.Id, p.Quest.Id, NameOf(state, p.Leader), p.Members.Select(uid => NameOf(state, uid)).ToList(),
                PhaseOf(p), SlotsOf(p.Quest)))
            .ToList();

    private static string NameOf(CampaignState state, string uid) => state.PlayerOf(uid)?.Name ?? "?";
}
