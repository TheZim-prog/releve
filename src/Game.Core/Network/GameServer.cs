using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace Game.Core.Network;

/// <summary>
/// Serveur de jeu sur TCP : accepte les clients et fait tourner une <see cref="ServerSession"/> sur un fil unique,
/// qui traite les messages reçus et joue les ticks à <see cref="Combat.CombatRules.TicksPerSecond"/>. Le même code
/// sert au serveur dédié (console) et à l'hébergement depuis le jeu.
/// </summary>
public sealed class GameServer : IDisposable
{
    /// <summary>Retard au-delà duquel on ne rattrape plus les ticks manqués (machine surchargée, mise en veille).</summary>
    private const int MaxCatchUpTicks = 5;

    private readonly ServerSession _session;
    private readonly TcpListener _listener;
    private readonly TimeSpan _tickPeriod;
    private readonly BlockingCollection<Action> _work = new();
    private readonly ConcurrentDictionary<LineConnection, byte> _connections = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<string> _log;
    private Thread? _loop;

    public GameServer(GameContent content, string scenarioId, int port, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _session = new ServerSession(content, scenarioId);
        _tickPeriod = TimeSpan.FromSeconds(1.0 / content.Rules.TicksPerSecond);
        _listener = TcpListener.Create(port); // IPv4 et IPv6 (Tailscale donne une adresse IPv4)
        _log = log ?? (_ => { });
    }

    /// <summary>Port d'écoute effectif (utile avec le port 0 : un port libre choisi par le système).</summary>
    public int Port { get; private set; }

    public void Start()
    {
        _listener.Start();
        Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = new Thread(RunLoop) { IsBackground = true, Name = "GameServer" };
        _loop.Start();
        _ = Task.Run(AcceptLoop);
        Log($"Listening on port {Port}.");
    }

    /// <summary>Arrête le serveur et coupe toutes les connexions (les clients voient la déconnexion).</summary>
    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
            return;
        _stop.Cancel();
        _listener.Stop();
        _work.CompleteAdding();
        _loop?.Join(TimeSpan.FromSeconds(2));
        foreach (var connection in _connections.Keys)
            connection.Close();
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException e)
            {
                if (_stop.IsCancellationRequested)
                    return;
                Log($"Accept failed: {e.Message}");
                continue;
            }

            // Une connexion ratée (coupée aussitôt…) ne doit pas empêcher d'accepter les suivantes.
            try
            {
                Accept(client);
            }
            catch (Exception e)
            {
                Log($"Connection setup failed: {e.Message}");
                client.Dispose();
            }
        }
    }

    private void Accept(TcpClient client)
    {
        var connection = new LineConnection(client);
        var peer = new Peer(connection);
        _connections[connection] = 0;
        Log($"Connection from {connection.RemoteAddress}.");
        connection.Start(
            line => Post(() => OnLine(peer, line)),
            () =>
            {
                _connections.TryRemove(connection, out _);
                Post(() =>
                {
                    _session.Disconnect(peer);
                    Log($"Disconnected: {connection.RemoteAddress}.");
                });
            });
        if (_stop.IsCancellationRequested)
            connection.Close(); // arrêt pendant l'acceptation
    }

    private void OnLine(Peer peer, string line)
    {
        // Une ligne lue juste avant la coupure arrive après : la session a déjà (ou va) traiter la déconnexion.
        if (!peer.Connection.IsOpen)
            return;
        ClientMessage message;
        try
        {
            message = NetJson.DeserializeClient(line);
        }
        catch (JsonException e)
        {
            Log($"Invalid message from {peer.Connection.RemoteAddress}: {e.Message}");
            return;
        }
        if (message is JoinRequest join)
            Log($"{peer.Connection.RemoteAddress} joins as '{join.Name}' (code {join.CodeVersion}, content {join.ContentVersion}).");
        _session.Receive(peer, message);
    }

    private void Post(Action action)
    {
        try
        {
            _work.Add(action);
        }
        catch (InvalidOperationException)
        {
            // Serveur arrêté.
        }
    }

    /// <summary>Le fil unique : messages reçus et ticks, dans l'ordre, sans jamais deux à la fois.</summary>
    private void RunLoop()
    {
        var clock = Stopwatch.StartNew();
        var nextTick = _tickPeriod;
        while (!_work.IsCompleted)
        {
            var wait = nextTick - clock.Elapsed;
            // Arrondi au-dessus : une attente de moins d'1 ms tronquée à 0 ferait tourner la boucle à vide.
            if (wait > TimeSpan.Zero && _work.TryTake(out var action, TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds))))
            {
                Run(action);
                continue;
            }

            var now = clock.Elapsed;
            if (now < nextTick)
                continue;
            if (now - nextTick > _tickPeriod * MaxCatchUpTicks)
                nextTick = now; // trop de retard : on repart d'ici plutôt que d'enchaîner les ticks
            Run(_session.Tick);
            nextTick += _tickPeriod;
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            // Une erreur sur un message ne doit pas arrêter la partie des autres (un combat cassé est déjà abandonné
            // par la session).
            Log($"Error: {e}");
        }
    }

    private void Log(string line)
    {
        try
        {
            _log(line);
        }
        catch (Exception)
        {
            // Le journal ne doit jamais arrêter le serveur.
        }
    }

    private sealed class Peer(LineConnection connection) : IPeer
    {
        public LineConnection Connection { get; } = connection;

        public void Send(ServerMessage message) => Connection.Send(NetJson.Serialize(message));
    }
}
