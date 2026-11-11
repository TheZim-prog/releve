using System.Net.NetworkInformation;
using System.Net.Sockets;
using Game.Core.Network;
using Godot;

namespace Game.Client;

// Connexion : héberger, rejoindre, se reconnecter, et répartir les messages du serveur entre le salon et le combat.
public partial class Main
{
    private void Host(string name, int port)
    {
        Disconnect();
        try
        {
            _server = new GameServer(_content, ScenarioId, port, line => GD.Print($"[server] {line}"));
            _server.Start();
        }
        catch (SocketException e)
        {
            _server?.Dispose();
            _server = null;
            ShowMenu($"Impossible d'héberger sur le port {port} : {e.Message}", error: true);
            return;
        }
        Connect(name, "127.0.0.1", port, rejoining: false);
    }

    private void Join(string name, string address, int port) => Connect(name, address, port, rejoining: false);

    private async void Connect(string name, string address, int port, bool rejoining)
    {
        var attempt = ++_connectionAttempt;
        _target = (name, address, port);
        SaveSettings(name, _server is null ? address : LoadSettings().Address, port);
        _client?.Dispose();
        _client = null;
        _menu?.SetBusy(true);
        _menu?.SetStatus($"Connexion à {address}:{port}…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var client = await GameClient.ConnectAsync(address, port, timeout.Token);
            // Entre-temps, le joueur a pu quitter, ou relancer une autre connexion : celle-ci ne sert plus.
            if (attempt != _connectionAttempt || !IsInsideTree())
            {
                client.Dispose();
                return;
            }
            _client = client;
            _client.Send(new JoinRequest(name, Protocol.Version, Protocol.CodeVersion, _content.Version));
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            if (attempt != _connectionAttempt || !IsInsideTree())
                return;
            if (rejoining && _rejoining && ++_rejoinAttempts < MaxRejoinAttempts)
            {
                _rejoinIn = 1;
                return;
            }
            ShowMenu($"Connexion impossible à {address}:{port}.", error: true);
        }
    }

    /// <summary>
    /// Reconnexion sous le même nom (désynchronisation, connexion perdue en combat) : le serveur renvoie tout le
    /// combat, rejoué ici. La vue du combat reste affichée, figée, en attendant.
    /// </summary>
    private void Rejoin(string message)
    {
        (_screen as CombatView)?.Freeze(message);
        _client?.Dispose();
        _client = null;
        _networkCombat = null;
        _rejoining = true;
        _rejoinAttempts = 0;
        _rejoinIn = 0.5; // laisse au serveur le temps de voir l'ancienne connexion partir
    }

    private void Disconnect()
    {
        _connectionAttempt++; // une connexion en cours ne servira plus
        _client?.Dispose();
        _client = null;
        _server?.Dispose();
        _server = null;
        _playerId = null;
        _lobby = null;
        _networkCombat = null;
        _backToLobbyIn = -1;
        _rejoinIn = -1;
        _rejoining = false;
    }

    private void PollServer()
    {
        if (_client is not { } client)
            return;
        while (client.TryReceive(out var message))
        {
            Handle(message);
            if (_client != client)
                return; // déconnecté pendant le traitement
        }
        if (client.IsConnected)
            return;
        if (_server is not null)
            ShowMenu("La partie hébergée s'est arrêtée.", error: true);
        else if (_networkCombat is not null)
            Rejoin("Connexion perdue : reconnexion…"); // le combat est en pause chez les autres en attendant
        else
            ShowMenu("Connexion perdue.", error: true);
    }

    private void Handle(ServerMessage message)
    {
        switch (message)
        {
            case Welcome welcome:
                _playerId = welcome.PlayerId;
                break;
            case JoinRefused { Reason: JoinRefusal.NameTaken } when _rejoining && ++_rejoinAttempts < MaxRejoinAttempts:
                // Reconnexion : le serveur n'a pas encore vu partir l'ancienne connexion ; on réessaie un peu plus tard.
                _client?.Dispose();
                _client = null;
                _rejoinIn = 1;
                break;
            case JoinRefused refused:
                ShowMenu(Texts.JoinRefusal(refused.Reason), error: true);
                break;
            case LobbyState lobby:
                _lobby = lobby;
                if (_networkCombat is not null)
                    _networkCombat.UpdatePlayers(lobby.Players);
                else if (_lobbyScreen is not null)
                    _lobbyScreen.Refresh(lobby);
                else if (_rejoining)
                    _backToLobbyIn = 1; // reconnexion : le combat suit normalement ; sinon (il s'est fini), le salon
                else if (_backToLobbyIn < 0)
                    ShowLobby();
                DevLobbyActions(lobby);
                break;
            case CombatState state:
                ShowNetworkCombat(state);
                break;
            case TickPlayed or PauseChanged:
                _networkCombat?.Receive(message);
                break;
            case RequestRefused when _networkCombat is not null:
                _networkCombat.Receive(message); // dans l'ordre des ticks déjà reçus
                break;
            case CombatOver:
                _networkCombat?.Receive(message);
                _backToLobbyIn = CombatOverDelay;
                break;
            case CombatAborted:
                ShowLobby("Le combat a été abandonné (erreur du serveur).", error: true);
                break;
            case RequestRefused refused:
                _lobbyScreen?.SetStatus(Texts.RequestRefusal(refused.Reason), error: true);
                break;
        }
    }

    /// <summary>Outils de développement : choisir son héros et lancer tout seuls (--pick, --start).</summary>
    private void DevLobbyActions(LobbyState lobby)
    {
        if (_networkCombat is not null || _playerId is not { } me)
            return;
        if (_devPick is { } pick && lobby.Players.FirstOrDefault(p => p.Id == me)?.Hero is null)
        {
            _client?.Send(new PickHero(new Game.Core.Combat.UnitId(pick)));
            _devPick = null;
        }
        if (_devStartWith is { } count && lobby.Players.Count >= count && lobby.Players.All(p => p.Hero is not null))
        {
            _client?.Send(new StartCombatRequest());
            _devStartWith = null;
        }
    }

    /// <summary>Pour l'hôte : le port et les adresses où ses amis peuvent le joindre (Tailscale en premier).</summary>
    private string HostInfo()
    {
        if (_server is null)
            return $"Connecté à {_target.Address}:{_target.Port}";
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a))
            .Select(a => a.ToString())
            .OrderBy(a => a.StartsWith("100.") ? 0 : 1)
            .ToList();
        var where = addresses.Count == 0 ? "" : " Tes amis rejoignent à : " + string.Join(", ",
            addresses.Select(a => a.StartsWith("100.") ? $"{a} (Tailscale)" : a)) + ".";
        return $"Tu héberges la partie (port {_target.Port}).{where}";
    }
}
