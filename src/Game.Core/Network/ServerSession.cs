using Game.Core.Combat;

namespace Game.Core.Network;

/// <summary>Une connexion de client, vue du serveur : de quoi lui envoyer des messages.</summary>
public interface IPeer
{
    void Send(ServerMessage message);
}

/// <summary>
/// La partie côté serveur, indépendante du transport : salon, combat, pause, reconnexion. Le serveur fait foi : il
/// fixe l'ordre des commandes et joue les ticks ; les clients rejouent ses <see cref="TickPlayed"/>.
/// <para>
/// Pas thread-safe : le transport appelle <see cref="Receive"/>, <see cref="Disconnect"/> et <see cref="Tick"/>
/// depuis un seul fil (<see cref="Tick"/> au rythme de <see cref="CombatRules.TicksPerSecond"/>).
/// </para>
/// </summary>
public sealed class ServerSession
{
    private readonly GameContent _content;
    private readonly string _scenarioId;
    private readonly HashSet<UnitId> _playableHeroes;
    private readonly List<Player> _players = new();
    private readonly List<(int Player, CombatCommand Command)> _queue = new();
    private int _nextPlayerId = 1;

    private CombatLaunch? _launch;
    private bool _paused;
    private int? _pausedBy;

    public ServerSession(GameContent content, string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _scenarioId = scenarioId;
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
        }
    }

    /// <summary>
    /// Connexion perdue. Au salon, le joueur s'en va (son héros est libéré) ; en combat, il garde sa place et le combat
    /// se met en pause jusqu'à son retour (ou jusqu'à ce qu'un joueur relance).
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
            if (!_paused)
                SetPaused(true, byPlayer: null);
        }
        BroadcastLobby();
    }

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
        Broadcast(new TickPlayed(simulation.CurrentTick, commands.Select(c => c.Command).ToList(), simulation.Checksum()));

        if (CombatLaunch.Outcome(simulation) is { } victory)
            EndCombat(new CombatOver(victory));
    }

    /// <summary>Fin du combat : tout le monde revient au salon, les absents en sont retirés.</summary>
    private void EndCombat(ServerMessage end)
    {
        Broadcast(end);
        Simulation = null;
        _launch = null;
        _queue.Clear();
        _paused = false;
        _pausedBy = null;
        _players.RemoveAll(p => p.Peer is null);
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

        var known = _players.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (known is { Peer: not null })
        {
            peer.Send(new JoinRefused(JoinRefusal.NameTaken));
            return;
        }
        if (known is null && Simulation is not null)
        {
            peer.Send(new JoinRefused(JoinRefusal.CombatInProgress));
            return;
        }

        var player = known ?? new Player(_nextPlayerId++, name);
        if (known is null)
            _players.Add(player);
        player.Peer = peer;
        peer.Send(new Welcome(player.Id));
        BroadcastLobby();

        if (Simulation is { } simulation)
        {
            // Reconnexion : tout le combat, à rejouer ; s'il était en pause pour les déconnexions, il reprend quand
            // tout le monde est revenu.
            peer.Send(new CombatState(_launch!, simulation.CommandLog.ToList(), simulation.CurrentTick, _paused, _pausedBy));
            if (_paused && _pausedBy is null && _players.All(p => p.Peer is not null))
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
            if (!_playableHeroes.Contains(unit))
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
        if (_players.Any(p => p.Hero is null))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.HeroesMissing));
            return;
        }
        _launch = new CombatLaunch(_scenarioId, _players.Select(p => new HeroAssignment(p.Hero!.Value, p.Id)).ToList());
        Simulation = new CombatSimulation(_launch.BuildSetup(_content));
        _queue.Clear();
        _paused = false;
        _pausedBy = null;
        Broadcast(new CombatState(_launch, Array.Empty<LoggedCommand>(), 0, Paused: false, PausedBy: null));
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
        var unit = simulation.FindUnit(valid.Unit);
        if (player.Hero is not { } hero || unit is null || !CombatLaunch.IsCommandedBy(unit, hero))
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

    private void BroadcastLobby() =>
        Broadcast(new LobbyState(_scenarioId, _players.Select(p => new PlayerInfo(p.Id, p.Name, p.Peer is not null, p.Hero)).ToList()));

    private void Broadcast(ServerMessage message)
    {
        foreach (var player in _players)
            player.Peer?.Send(message);
    }

    private sealed class Player(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public IPeer? Peer { get; set; }
        public UnitId? Hero { get; set; }
    }
}
