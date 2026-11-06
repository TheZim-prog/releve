using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Game.Core.Network;

/// <summary>
/// Connexion TCP qui échange des lignes de texte (un message JSON par ligne). La lecture et l'écriture tournent chacune
/// sur leur tâche : <see cref="Send"/> ne bloque jamais l'appelant (la boucle du serveur, ou l'image du jeu).
/// </summary>
public sealed class LineConnection : IDisposable
{
    /// <summary>Messages en attente d'envoi au-delà desquels le correspondant est jugé bloqué, et déconnecté.</summary>
    private const int MaxPendingLines = 10_000;

    /// <summary>Taille maximale d'une ligne reçue (un combat entier rejoué tient largement dedans) ; au-delà, on coupe.</summary>
    public const int MaxLineBytes = 16 * 1024 * 1024;

    private readonly TcpClient _client;
    private readonly Channel<string> _outgoing = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaxPendingLines) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private int _closed;
    private Action? _onClosed;

    public LineConnection(TcpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _client.NoDelay = true; // petits messages fréquents : pas d'attente de regroupement
        RemoteAddress = client.Client.RemoteEndPoint?.ToString() ?? "?"; // gardée : illisible une fois le socket fermé
    }

    public string RemoteAddress { get; }

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    /// <summary>
    /// Démarre la lecture et l'écriture. <paramref name="onLine"/> est appelé pour chaque ligne reçue (sur la tâche de
    /// lecture), <paramref name="onClosed"/> une seule fois, quand la connexion se ferme pour une raison quelconque.
    /// </summary>
    public void Start(Action<string> onLine, Action onClosed)
    {
        ArgumentNullException.ThrowIfNull(onLine);
        _onClosed = onClosed;
        NetworkStream stream;
        try
        {
            stream = _client.GetStream();
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            Close(); // déjà coupée
            return;
        }
        _ = Task.Run(() => ReadLoop(stream, onLine));
        _ = Task.Run(() => WriteLoop(stream));
    }

    /// <summary>Envoie une ligne (sans retour à la ligne) ; ignoré si la connexion est fermée.</summary>
    public void Send(string line)
    {
        if (!IsOpen)
            return;
        if (!_outgoing.Writer.TryWrite(line))
            Close(); // file pleine : le correspondant ne lit plus
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        _outgoing.Writer.TryComplete();
        _stop.Cancel();
        _client.Dispose();
        _onClosed?.Invoke();
    }

    public void Dispose() => Close();

    /// <summary>Découpe le flux en lignes UTF-8 terminées par '\n', sans jamais garder plus de <see cref="MaxLineBytes"/>.</summary>
    private async Task ReadLoop(NetworkStream stream, Action<string> onLine)
    {
        try
        {
            var buffer = new byte[64 * 1024];
            using var line = new MemoryStream();
            int read;
            while ((read = await stream.ReadAsync(buffer, _stop.Token)) > 0)
            {
                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n')
                        continue;
                    line.Write(buffer, start, i - start);
                    start = i + 1;
                    if (line.Length > 0)
                        onLine(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r'));
                    line.SetLength(0);
                }
                line.Write(buffer, start, read - start);
                if (line.Length > MaxLineBytes)
                    return; // ligne démesurée : correspondant défaillant ou malveillant
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
        }
        finally
        {
            Close();
        }
    }

    private async Task WriteLoop(NetworkStream stream)
    {
        try
        {
            await foreach (var line in _outgoing.Reader.ReadAllAsync(_stop.Token))
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await stream.WriteAsync(bytes, _stop.Token);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
        }
        finally
        {
            Close();
        }
    }
}
