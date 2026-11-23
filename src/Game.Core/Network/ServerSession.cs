using Game.Core.Campaign;
using Game.Core.Combat;

namespace Game.Core.Network;

/// <summary>Une connexion de client, vue du serveur : de quoi lui envoyer des messages.</summary>
public interface IPeer
{
    void Send(ServerMessage message);
}

/// <summary>
/// La partie côté serveur, indépendante du transport : salon, combats, pause, reconnexion. Le serveur fait foi : il
/// fixe l'ordre des commandes et joue les ticks ; les clients rejouent ses <see cref="TickPlayed"/>. Un joueur est
/// reconnu à son identifiant (<see cref="JoinRequest.PlayerUid"/>). Sans campagne, le salon joue le scénario du serveur
/// et chacun y choisit un héros (un seul combat, avec tout le monde) ; avec une campagne, le salon est le QG, où des
/// groupes se forment autour des quêtes et partent chacun de leur côté, en même temps (cf. ServerSession.Groups.cs).
/// <para>
/// Pas thread-safe : le transport appelle <see cref="Receive"/>, <see cref="Disconnect"/> et <see cref="Tick"/>
/// depuis un seul fil (<see cref="Tick"/> au rythme de <see cref="CombatRules.TicksPerSecond"/>).
/// </para>
/// </summary>
public sealed partial class ServerSession
{
    private readonly GameContent _content;
    private readonly string _scenarioId;
    private readonly HashSet<UnitId> _playableHeroes;
    private readonly List<Player> _players = new();
    private readonly CampaignKeeper? _campaign;
    private readonly Random _random;
    private int _nextPlayerId = 1;

    /// <param name="campaign">La campagne que tient ce serveur, ou <c>null</c> (des combats sans suite).</param>
    /// <param name="random">
    /// D'où vient la graine de chaque trajet (celle de ses tests, gardée avec lui) ; par défaut, imprévisible.
    /// </param>
    public ServerSession(GameContent content, string scenarioId, CampaignKeeper? campaign = null, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _scenarioId = scenarioId;
        _campaign = campaign;
        _random = random ?? new Random();
        _playableHeroes = CombatLaunch.PlayableHeroes(content, scenarioId).Select(u => u.Id).ToHashSet();
        ResumeTrips();
    }

    public void Receive(IPeer peer, ClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(message);
        if (message is JoinRequest join)
        {
            Join(peer, join);
            return;
        }
        if (PlayerOf(peer) is not { } player)
        {
            peer.Send(new RequestRefused(RequestRefusal.NotJoined));
            return;
        }
        switch (message)
        {
            case PickHero pick:
                Pick(player, pick.Unit);
                break;
            case StartCombatRequest:
                Start(player);
                break;
            case SendCommand command:
                Command(player, command.Command);
                break;
            case SetPause pause:
                Pause(player, pause.Paused);
                break;
            case RecruitHero recruit:
                RecruitFor(player, recruit.Recruit);
                break;
            case AcceptQuest accept:
                AcceptFor(player, accept.QuestId);
                break;
            case JoinGroup joinGroup:
                JoinGroupFor(player, joinGroup.GroupId);
                break;
            case LeaveGroup:
                LeaveGroupFor(player);
                break;
            case DeployHero deploy:
                DeployFor(player, deploy.HeroId);
                break;
            case SpendStatPoint spend:
                TrainFor(player, (state, catalog) => Training.SpendStatPoint(state, catalog, player.Uid, spend.HeroId, spend.Stat));
                break;
            case LearnSkill learn:
                TrainFor(player, (state, catalog) => Training.Learn(state, catalog, player.Uid, learn.HeroId, learn.Skill));
                break;
            case EquipHero equip:
                TrainFor(player, (state, catalog) => Training.Equip(state, catalog, player.Uid, equip.HeroId, equip.Equipped, equip.Passive));
                break;
            case TravelTo travel:
                TravelFor(player, travel.Place);
                break;
            case ResolveEvent resolve:
                ResolveFor(player, resolve.Option);
                break;
            case WaitHalfDay:
                WaitFor(player);
                break;
        }
    }

    /// <summary>
    /// Connexion perdue. Au salon (ou dans un groupe qui se forme, qu'il quitte), le joueur s'en va ; en route ou en
    /// combat, il garde sa place. En combat, celui-ci se met en pause jusqu'à son retour (ou jusqu'à ce qu'un joueur
    /// relance), s'il y joue une unité (un joueur resté au QG ne bloque pas la quête des autres).
    /// </summary>
    public void Disconnect(IPeer peer)
    {
        if (PlayerOf(peer) is not { } player)
            return;
        if (!Busy(player))
        {
            Leave(player);
            _players.Remove(player);
        }
        else
        {
            player.Peer = null;
            if (RoomOf(player) is { Paused: false } room && player.Hero is not null)
                SetPaused(room, true, byPlayer: null);
        }
        BroadcastLobby();
    }

    /// <summary>
    /// Joue un tick de chaque combat (sauf ceux en pause) et le diffuse à ses joueurs avec ses commandes. Un combat qui
    /// plante est abandonné sans arrêter les autres ; l'erreur remonte ensuite au serveur, qui la journalise.
    /// </summary>
    public void Tick()
    {
        Exception? failure = null;
        foreach (var room in _rooms.ToList())
        {
            try
            {
                TickRoom(room);
            }
            catch (Exception e)
            {
                failure ??= e;
            }
        }
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }

    /// <summary>
    /// Le joueur est pris ailleurs qu'au salon : dans le combat libre, ou dans un groupe en route ou en combat. Il garde
    /// alors sa place s'il part, et ne peut rien faire au QG.
    /// </summary>
    private bool Busy(Player player) =>
        IsCampaign ? PartyOf(player.Uid) is { } party && PhaseOf(party) != GroupPhase.Forming : _rooms.Count > 0;

    /// <summary>Les joueurs partis qui ne sont plus attendus nulle part quittent la partie.</summary>
    private void ForgetTheAbsent() => _players.RemoveAll(p => p.Peer is null && !Busy(p));

    private void Join(IPeer peer, JoinRequest join)
    {
        if (PlayerOf(peer) is not null)
            return;
        if (join.ProtocolVersion != Protocol.Version)
        {
            peer.Send(new JoinRefused(JoinRefusal.ProtocolMismatch));
            return;
        }
        if (join.CodeVersion != Protocol.CodeVersion)
        {
            peer.Send(new JoinRefused(JoinRefusal.CodeMismatch));
            return;
        }
        if (join.ContentVersion != _content.Version)
        {
            peer.Send(new JoinRefused(JoinRefusal.ContentMismatch));
            return;
        }
        var name = join.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > Protocol.MaxNameLength)
        {
            peer.Send(new JoinRefused(JoinRefusal.InvalidName));
            return;
        }
        if (!PlayerIdentity.IsValid(join.PlayerUid))
        {
            peer.Send(new JoinRefused(JoinRefusal.InvalidIdentity));
            return;
        }

        var known = _players.Find(p => p.Uid == join.PlayerUid);
        if (known is { Peer: not null })
        {
            peer.Send(new JoinRefused(JoinRefusal.AlreadyConnected));
            return;
        }
        if (_players.Any(p => p != known && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            peer.Send(new JoinRefused(JoinRefusal.NameTaken));
            return;
        }
        // Le combat libre réunit tout le monde : on n'y entre pas en cours de route. Au QG, les combats des groupes
        // n'empêchent personne d'arriver.
        if (known is null && !IsCampaign && _rooms.Count > 0)
        {
            peer.Send(new JoinRefused(JoinRefusal.CombatInProgress));
            return;
        }

        // La campagne d'abord : si sa sauvegarde échoue, rien n'a changé ici, et le joueur le sait (l'erreur remonte au
        // serveur, qui la journalise).
        try
        {
            _campaign?.Update(state => state.WithPlayer(join.PlayerUid, name));
        }
        catch
        {
            peer.Send(new JoinRefused(JoinRefusal.CampaignUnavailable));
            throw;
        }
        var player = known ?? new Player(_nextPlayerId++, join.PlayerUid);
        if (known is null)
            _players.Add(player);
        player.Name = name;
        player.Peer = peer;
        // Un trajet repris au lancement du serveur : le joueur y retrouve son héros parti.
        if (known is null && HeroOnTheRoad(player.Uid) is { } traveller)
            player.Deployed = traveller;
        peer.Send(new Welcome(player.Id));
        BroadcastLobby();
        if (RoomOf(player) is { } room)
            Rejoin(player, room);
    }

    private void Pick(Player player, UnitId? hero)
    {
        if (_rooms.Count > 0)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInLobby));
            return;
        }
        if (hero is { } unit)
        {
            // Au QG, on ne choisit pas un héros du scénario : on déploie un des siens (DeployHero).
            if (IsCampaign || !_playableHeroes.Contains(unit))
            {
                player.Peer!.Send(new RequestRefused(RequestRefusal.NotAHero));
                return;
            }
            if (_players.Any(p => p != player && p.Hero == unit))
            {
                player.Peer!.Send(new RequestRefused(RequestRefusal.HeroTaken));
                return;
            }
        }
        player.Hero = hero;
        BroadcastLobby();
    }

    private void Start(Player player)
    {
        if (IsCampaign)
        {
            StartQuest(player);
            return;
        }
        if (_rooms.Count > 0)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInLobby));
            return;
        }
        if (_players.Any(p => p.Hero is null))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.HeroesMissing));
            return;
        }
        var launch = new CombatLaunch(_scenarioId, _players.Select(p => new HeroAssignment(p.Hero!.Value, p.Id)).ToList());
        Open(new CombatRoom(launch, launch.BuildSetup(_content), party: null, fight: null));
    }

    private Player? PlayerOf(IPeer peer) => _players.Find(p => p.Peer == peer);

    /// <summary>
    /// Le salon, à chacun : le résumé de campagne est vu par lui. Le scénario annoncé est, au QG, celui de la quête de
    /// son groupe (vide sans groupe).
    /// </summary>
    private void BroadcastLobby()
    {
        var players = _players
            .Select(p => new PlayerInfo(p.Id, p.Name, p.Peer is not null, p.Hero, p.Deployed, _campaign?.State.PlayerOf(p.Uid)?.Hour ?? 0))
            .ToList();
        foreach (var player in _players)
        {
            var scenario = IsCampaign ? PartyOf(player.Uid)?.Quest.Scenario ?? "" : _scenarioId;
            player.Peer?.Send(new LobbyState(scenario, players, CampaignSummary(player.Uid)));
        }
    }

    private sealed class Player(int id, string uid)
    {
        public int Id { get; } = id;
        public string Uid { get; } = uid;
        public string Name { get; set; } = "";
        public IPeer? Peer { get; set; }

        /// <summary>L'unité qu'il joue (choisie au salon sans campagne ; donnée au lancement d'une quête).</summary>
        public UnitId? Hero { get; set; }

        /// <summary>Au QG : le héros de campagne qu'il déploie, ou <c>null</c> (il reste au QG).</summary>
        public string? Deployed { get; set; }
    }
}
