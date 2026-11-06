using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;

namespace Game.Core.Network;

/// <summary>
/// Connexion d'un joueur au serveur. Les messages reçus s'accumulent en arrière-plan ; le jeu les lit à son rythme
/// (<see cref="TryReceive"/>, à chaque image), sur son propre fil.
/// </summary>
public sealed class GameClient : IDisposable
{
    private readonly LineConnection _connection;
    private readonly ConcurrentQueue<ServerMessage> _received = new();
    private volatile bool _connected = true;

    private GameClient(LineConnection connection)
    {
        _connection = connection;
        _connection.Start(OnLine, () => _connected = false);
    }

    /// <summary>Faux dès que la connexion est perdue (les messages déjà reçus restent lisibles).</summary>
    public bool IsConnected => _connected;

    /// <summary>Se connecte au serveur ; lève une <see cref="SocketException"/> s'il ne répond pas.</summary>
    public static async Task<GameClient> ConnectAsync(string host, int port, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellation);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        return new GameClient(new LineConnection(client));
    }

    public void Send(ClientMessage message) => _connection.Send(NetJson.Serialize(message));

    public bool TryReceive(out ServerMessage message) => _received.TryDequeue(out message!);

    public void Dispose() => _connection.Close();

    private void OnLine(string line)
    {
        try
        {
            _received.Enqueue(NetJson.DeserializeServer(line));
        }
        catch (JsonException)
        {
            // Message illisible : serveur d'une autre version ; on coupe plutôt que de se désynchroniser.
            _connection.Close();
        }
    }
}
