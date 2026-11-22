using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Reputation;

namespace Game.Core.Network;

/// <summary>Une connexion de client, vue du serveur : de quoi lui envoyer des messages.</summary>
public interface IPeer
{
    void Send(ServerMessage message);
}

/// <summary>
/// La partie côté serveur, indépendante du transport : salon, combat, pause, reconnexion. Le serveur fait foi : il
/// fixe l'ordre des commandes et joue les ticks ; les clients rejouent ses <see cref="TickPlayed"/>. Un joueur est
/// reconnu à son identifiant (<see cref="JoinRequest.PlayerUid"/>). Sans campagne, le salon joue le scénario du serveur
/// et chacun y choisit un héros ; avec une campagne, le salon est le QG (cf. ServerSession.Campaign.cs).
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
    private readonly List<(int Player, CombatCommand Command)> _queue = new();
    private readonly CampaignKeeper? _campaign;
    private int _nextPlayerId = 1;

    private CombatLaunch? _launch;
    private bool _paused;
    private int? _pausedBy;

    /// <param name="campaign">La campagne que tient ce serveur, ou <c>null</c> (des combats sans suite).</param>
    public ServerSession(GameContent content, string scenarioId, CampaignKeeper? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _scenarioId = scenarioId;
        _campaign = campaign;
        _playableHeroes = CombatLaunch.PlayableHeroes(content, scenarioId).Select(u => u.Id).ToHashSet();
    }

    /// <summary>La simulation qui fait foi, pendant un combat ; <c>null</c> au salon.</summary>
    public CombatSimulation? Simulation { get; private set; }

    public bool IsPaused => _paused;

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
            case ChooseQuest choose:
                ChooseQuestFor(player, choose.QuestId);
                break;
            case DeployHero deploy:
                DeployFor(player, deploy.HeroId);
                break;
        }
    }

    /// <summary>
    /// Connexion perdue. Au salon, le joueur s'en va (son héros est libéré) ; en combat, il garde sa place et le combat
    /// se met en pause jusqu'à son retour (ou jusqu'à ce qu'un joueur relance), s'il y joue une unité (un joueur resté
    /// au QG ne bloque pas la quête des autres).
    /// </summary>
    public void Disconnect(IPeer peer)
    {
        if (PlayerOf(peer) is not { } player)
            return;
        if (Simulation is null)
        {
            _players.Remove(player);
        }
        else
        {
            player.Peer = null;
            if (!_paused && player.Hero is not null)
                SetPaused(true, byPlayer: null);
        }
        BroadcastLobby();
    }

    /// <summary>Tous les joueurs qui jouent une unité du combat sont là.</summary>
    private bool AllCombatantsConnected => _players.All(p => p.Hero is null || p.Peer is not null);

    /// <summary>Joue un tick du combat (rien au salon ni en pause) et le diffuse avec ses commandes.</summary>
    public void Tick()
    {
        if (Simulation is not { } simulation || _paused)
            return;
        var commands = _queue.ToList();
        _queue.Clear();
        try
        {
            foreach (var (_, command) in commands)
                simulation.Submit(command);
            simulation.Tick();
        }
        catch
        {
            // Bug de la simulation : son état est à moitié changé, plus rien ne serait fiable. Le combat est abandonné.
            EndCombat(new CombatAborted());
            throw;
        }
        Broadcast(new TickPlayed(simulation.Step, commands.Select(c => c.Command).ToList(), simulation.Checksum()));

        if (simulation.Outcome is { } victory)
        {
            // Une sauvegarde de campagne qui échoue (disque plein…) ne doit pas bloquer le combat fini : il se termine,
            // et l'erreur remonte au serveur, qui la journalise.
            try
            {
                RecordInCampaign(simulation);
            }
            finally
            {
                EndCombat(new CombatOver(victory));
            }
        }
    }

    /// <summary>
    /// Fin du combat : tout le monde revient au salon, les absents en sont retirés. Au QG, les unités jouées ne valent
    /// plus rien (chaque quête a les siennes) ; les héros déployés le restent.
    /// </summary>
    private void EndCombat(ServerMessage end)
    {
        Broadcast(end);
        Simulation = null;
        _launch = null;
        _queue.Clear();
        _paused = false;
        _pausedBy = null;
        _players.RemoveAll(p => p.Peer is null);
        _questInCombat = null;
        if (IsCampaign)
        {
            foreach (var player in _players)
                player.Hero = null;
        }
        BroadcastLobby();
    }

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
        if (known is null && Simulation is not null)
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
        peer.Send(new Welcome(player.Id));
        BroadcastLobby();

        if (Simulation is { } simulation)
        {
            // Reconnexion : tout le combat, à rejouer ; s'il était en pause pour les déconnexions, il reprend quand
            // tout le monde est revenu.
            peer.Send(new CombatState(_launch!, simulation.CommandLog.ToList(), simulation.Step, _paused, _pausedBy));
            if (_paused && _pausedBy is null && AllCombatantsConnected)
                SetPaused(false, byPlayer: null);
        }
    }

    private void Pick(Player player, UnitId? hero)
    {
        if (Simulation is not null)
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
        if (Simulation is not null)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInLobby));
            return;
        }
        if (IsCampaign)
        {
            StartQuest(player);
            return;
        }
        if (_players.Any(p => p.Hero is null))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.HeroesMissing));
            return;
        }
        var launch = new CombatLaunch(_scenarioId, _players.Select(p => new HeroAssignment(p.Hero!.Value, p.Id)).ToList());
        Launch(launch, launch.BuildSetup(_content));
    }

    private void Launch(CombatLaunch launch, CombatSetup setup)
    {
        _launch = launch;
        Simulation = new CombatSimulation(setup);
        _queue.Clear();
        _paused = false;
        _pausedBy = null;
        Broadcast(new CombatState(_launch, Array.Empty<LoggedCommand>(), 0, Paused: false, PausedBy: null));
        // Au QG, les unités jouées viennent d'être données : tout le monde doit savoir qui joue quoi.
        if (IsCampaign)
            BroadcastLobby();
    }

    private void Command(Player player, CombatCommand command)
    {
        if (Simulation is not { } simulation)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInCombat));
            return;
        }
        if (Normalize(simulation, command) is not { } valid)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownCommand));
            return;
        }
        // Son héros, ou une unité qu'il domine (une domination finie ou un héros à terre : la simulation tranchera).
        // « Continuer » un message du script ne vise aucune unité : n'importe quel joueur le peut.
        var unit = simulation.FindUnit(valid.Unit);
        if (valid is not ContinueCommand
            && (player.Hero is not { } hero || unit is null || !CombatLaunch.IsCommandedBy(unit, hero)))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotYourUnit));
            return;
        }
        if (_queue.Count(q => q.Player == player.Id) >= Protocol.MaxQueuedCommandsPerPlayer)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.TooManyCommands));
            return;
        }
        _queue.Add((player.Id, valid));
    }

    /// <summary>
    /// La commande reconstruite à partir de ses seuls champs utiles (l'unité d'un ordre est celle de son activation,
    /// quoi que dise le message), ou <c>null</c> si elle est inconnue ou malformée : la simulation ne doit jamais
    /// recevoir de quoi la faire planter.
    /// </summary>
    private static CombatCommand? Normalize(CombatSimulation simulation, CombatCommand? command)
    {
        var activation = command switch
        {
            ActivateCommand a => a,
            OrderCommand o => o.Activation,
            _ => null,
        };
        if (command is CancelOrderCommand cancel)
            return new CancelOrderCommand(cancel.Unit);
        if (command is ContinueCommand resume)
            return new ContinueCommand(resume.Message);
        if (activation is null || (activation.Skill is { } skill && skill.SkillId is null)
            || (activation.Interaction is { } interaction && !Enum.IsDefined(interaction.Kind)))
            return null;
        try
        {
            _ = simulation.Check(activation, ignoreTiming: true);
        }
        catch (Exception e) when (e is ArgumentException or NullReferenceException or InvalidOperationException)
        {
            return null;
        }
        var clean = new ActivateCommand(activation.Unit, activation.MoveTo, activation.Skill, activation.Interaction);
        return command is OrderCommand ? new OrderCommand(clean) : clean;
    }

    private void Pause(Player player, bool paused)
    {
        if (Simulation is null)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInCombat));
            return;
        }
        // Mettre en pause pendant une pause de déconnexion en fait une pause de joueur : elle ne sera plus levée
        // d'elle-même au retour du déconnecté.
        if (paused ? !_paused || _pausedBy is null : _paused)
            SetPaused(paused, paused ? player.Id : null);
    }

    private void SetPaused(bool paused, int? byPlayer)
    {
        _paused = paused;
        _pausedBy = paused ? byPlayer : null;
        Broadcast(new PauseChanged(paused, _pausedBy));
    }

    private Player? PlayerOf(IPeer peer) => _players.Find(p => p.Peer == peer);

    /// <summary>
    /// Le salon, à chacun : le résumé de campagne marque ses propres héros. Le scénario annoncé est, au QG, celui de la
    /// quête choisie (vide sans quête).
    /// </summary>
    private void BroadcastLobby()
    {
        var players = _players.Select(p => new PlayerInfo(p.Id, p.Name, p.Peer is not null, p.Hero, p.Deployed)).ToList();
        var scenario = IsCampaign ? ChosenQuest?.Scenario ?? "" : _scenarioId;
        foreach (var player in _players)
            player.Peer?.Send(new LobbyState(scenario, players, CampaignSummary(player.Uid)));
    }

    private void Broadcast(ServerMessage message)
    {
        foreach (var player in _players)
            player.Peer?.Send(message);
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
