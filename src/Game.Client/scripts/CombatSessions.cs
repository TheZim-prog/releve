using Game.Core;
using Game.Core.Combat;
using Game.Core.Network;

namespace Game.Client;

/// <summary>
/// D'où vient le combat affiché par <see cref="CombatView"/> : une simulation locale (solo) ou celle du serveur
/// (réseau). La vue lit la simulation, lui envoie les commandes du joueur et gère la pause par ici.
/// </summary>
public interface ICombatSession
{
    GameContent Content { get; }

    string ScenarioId { get; }

    CombatSimulation Simulation { get; }

    bool IsNetwork { get; }

    bool IsPaused { get; }

    /// <summary>Pourquoi le combat est en pause (qui l'a mise, qui est déconnecté), pour l'affichage.</summary>
    string PauseText { get; }

    /// <summary>Victoire, défaite, ou <c>null</c> tant que le combat dure.</summary>
    bool? Outcome { get; }

    /// <summary>Avance le combat du temps écoulé (en solo) ou des ticks reçus (en réseau) ; renvoie les événements produits.</summary>
    IReadOnlyList<CombatEvent> Advance(double delta);

    /// <summary>Messages pour le journal apparus depuis le dernier appel (refus du serveur…).</summary>
    IReadOnlyList<string> TakeNotices();

    /// <summary>Le joueur peut-il donner des ordres à cette unité ?</summary>
    bool CanCommand(Unit unit);

    void Submit(CombatCommand command);

    void TogglePause();

    IReadOnlyList<ActivateCommand> PlannedOrders();

    /// <summary>Qui joue ce héros (nom du joueur, « IA »), ou <c>null</c> en solo.</summary>
    string? ControllerName(UnitId hero);

    /// <summary>Un nouveau combat identique, données relues (solo) ; <c>null</c> en réseau.</summary>
    ICombatSession? Restart();
}

/// <summary>Combat solo : la simulation tourne ici ; le joueur commande tous les héros (ou aucun, avec l'IA).</summary>
public sealed class SoloCombat : ICombatSession
{
    private const int MaxTicksPerFrame = 5;

    private readonly bool _aiHeroes;
    private double _accumulator;

    public SoloCombat(GameContent content, string scenarioId, bool aiHeroes)
    {
        Content = content;
        ScenarioId = scenarioId;
        _aiHeroes = aiHeroes;
        var heroes = aiHeroes
            ? new List<HeroAssignment>()
            : CombatLaunch.PlayableHeroes(content, scenarioId).Select(u => new HeroAssignment(u.Id, Player: 1)).ToList();
        Simulation = new CombatSimulation(new CombatLaunch(scenarioId, heroes).BuildSetup(content));
    }

    public GameContent Content { get; }
    public string ScenarioId { get; }
    public CombatSimulation Simulation { get; }
    public bool IsNetwork => false;
    public bool IsPaused { get; private set; }
    public string PauseText => "PAUSE";
    public bool? Outcome { get; private set; }

    public IReadOnlyList<CombatEvent> Advance(double delta)
    {
        var events = new List<CombatEvent>();
        if (IsPaused || Outcome is not null)
            return events;
        var tickDuration = 1.0 / Content.Rules.TicksPerSecond;
        _accumulator = Math.Min(_accumulator + delta, tickDuration * MaxTicksPerFrame);
        while (_accumulator >= tickDuration && Outcome is null)
        {
            _accumulator -= tickDuration;
            events.AddRange(Simulation.Tick());
            Outcome = CombatLaunch.Outcome(Simulation);
        }
        return events;
    }

    public IReadOnlyList<string> TakeNotices() => Array.Empty<string>();
    public bool CanCommand(Unit unit) => Simulation.IsPlayerControlled(unit.Id);
    public void Submit(CombatCommand command) => Simulation.Submit(command);
    public void TogglePause() => IsPaused = !IsPaused;
    public IReadOnlyList<ActivateCommand> PlannedOrders() => Simulation.PlannedOrders();
    public string? ControllerName(UnitId hero) => null;
    public ICombatSession Restart() => new SoloCombat(GameData.Load(), ScenarioId, _aiHeroes);
}

/// <summary>
/// Combat en réseau : la simulation rejoue les ticks du serveur (<see cref="CombatMirror"/>) ; les commandes et la
/// pause partent au serveur. Le joueur ne commande que son héros (et les unités qu'il domine).
/// </summary>
public sealed class NetworkCombat : ICombatSession
{
    private readonly GameClient _client;
    private readonly int _player;
    private readonly Queue<ServerMessage> _inbox = new();
    private readonly List<string> _notices = new();
    private IReadOnlyList<PlayerInfo> _players;
    private int? _pausedBy;

    public NetworkCombat(GameContent content, GameClient client, int player, CombatState state, IReadOnlyList<PlayerInfo> players)
    {
        Content = content;
        _client = client;
        _player = player;
        _players = players;
        Mirror = new CombatMirror(content, state);
        IsPaused = state.Paused;
        _pausedBy = state.PausedBy;
        MyHero = Mirror.HeroOf(player);
    }

    public CombatMirror Mirror { get; }
    public UnitId? MyHero { get; }
    public GameContent Content { get; }
    public string ScenarioId => Mirror.Launch.ScenarioId;
    public CombatSimulation Simulation => Mirror.Simulation;
    public bool IsNetwork => true;
    public bool IsPaused { get; private set; }
    public bool? Outcome { get; private set; }

    public string PauseText
    {
        get
        {
            var away = _players.Where(p => !p.Connected).Select(p => p.Name).ToList();
            if (_pausedBy is { } by)
                return $"PAUSE ({NameOf(by)})";
            return away.Count > 0 ? $"En attente de {string.Join(", ", away)}" : "PAUSE";
        }
    }

    /// <summary>
    /// Message du serveur pour ce combat (ticks, pause, fin, refus), traité dans l'ordre d'arrivée à la prochaine image.
    /// </summary>
    public void Receive(ServerMessage message) => _inbox.Enqueue(message);

    public void UpdatePlayers(IReadOnlyList<PlayerInfo> players) => _players = players;

    public IReadOnlyList<string> TakeNotices()
    {
        var notices = _notices.ToList();
        _notices.Clear();
        return notices;
    }

    /// <summary>Lève <see cref="DesyncException"/> si la simulation ne suit plus celle du serveur.</summary>
    public IReadOnlyList<CombatEvent> Advance(double delta)
    {
        var events = new List<CombatEvent>();
        while (_inbox.TryDequeue(out var message))
        {
            switch (message)
            {
                case TickPlayed tick:
                    events.AddRange(Mirror.Apply(tick));
                    break;
                case PauseChanged pause:
                    IsPaused = pause.Paused;
                    _pausedBy = pause.ByPlayer;
                    break;
                case CombatOver over:
                    Outcome = over.Victory;
                    break;
                case RequestRefused refused:
                    // On ne sait pas laquelle : les commandes en route sont oubliées (les acceptées reviendront au
                    // prochain tick) plutôt que d'afficher un ordre que le serveur n'a pas pris.
                    Mirror.ForgetSent();
                    _notices.Add(Texts.RequestRefusal(refused.Reason));
                    break;
            }
        }
        return events;
    }

    public bool CanCommand(Unit unit) =>
        MyHero is { } hero && Simulation.IsPlayerControlled(unit.Id) && CombatLaunch.IsCommandedBy(unit, hero);

    public void Submit(CombatCommand command)
    {
        _client.Send(new SendCommand(command));
        Mirror.Sent(command);
    }

    public void TogglePause() => _client.Send(new SetPause(!IsPaused));
    public IReadOnlyList<ActivateCommand> PlannedOrders() => Mirror.PlannedOrders();
    public string? ControllerName(UnitId hero) => Mirror.PlayerOf(hero) is { } player ? NameOf(player) : "IA";
    public ICombatSession? Restart() => null;

    private string NameOf(int player) =>
        player == _player ? "toi" : _players.FirstOrDefault(p => p.Id == player)?.Name ?? $"joueur {player}";
}
