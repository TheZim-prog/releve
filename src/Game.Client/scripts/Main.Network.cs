using System.Net.NetworkInformation;
using System.Net.Sockets;
using Game.Core.Network;
using Godot;

namespace Game.Client;

// Connexion : héberger, rejoindre, se reconnecter, et répartir les messages du serveur entre le salon et le combat.
public partial class Main
{
    /// <summary>
    /// Héberge la campagne de ce PC (<paramref name="withCampaign"/>), ou un combat libre du scénario choisi.
    /// <paramref name="solo"/> : la campagne en solo, sur un serveur que ce PC seul peut joindre (avec le port 0 : choisi
    /// par le système), dans ce processus comme pour toute partie hébergée.
    /// </summary>
    private void Host(string name, int port, bool withCampaign, bool solo = false)
    {
        Disconnect();
        if (!ReloadContent())
            return;
        _solo = solo;
        Game.Core.Campaign.CampaignKeeper? campaign = null;
        if (withCampaign)
        {
            campaign = OpenHostedCampaign();
            if (campaign is null)
                return;
        }
        try
        {
            _server = new GameServer(_content, _scenarioId, port, line => GD.Print($"[server] {line}"), campaign, localOnly: solo);
            _server.Start();
        }
        catch (SocketException e)
        {
            if (_server is null)
                campaign?.Dispose(); // le serveur n'a pas pu la prendre : elle est lâchée ici
            _server?.Dispose();
            _server = null;
            ShowMenu(solo ? $"Impossible de lancer la campagne en solo : {e.Message}" : $"Impossible d'héberger sur le port {port} : {e.Message}",
                error: true);
            return;
        }
        Connect(name, "127.0.0.1", _server.Port, rejoining: false);
    }

    /// <summary>La campagne de ce PC, ou <c>null</c> (retour au menu avec la raison).</summary>
    private Game.Core.Campaign.CampaignKeeper? OpenHostedCampaign()
    {
        try
        {
            if (HostedCampaign() is { } campaign)
                return campaign;
            ShowMenu("Ce contenu ne permet pas de campagne (reputation.json manque dans data/).", error: true);
        }
        catch (Game.Core.Campaign.CampaignInUseException)
        {
            ShowMenu("Cette campagne est déjà ouverte dans une autre fenêtre du jeu (ou un serveur sur ce PC).", error: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            ShowMenu($"Sauvegarde de campagne illisible ({ProjectSettings.GlobalizePath(CampaignPath)}) : {e.Message}", error: true);
        }
        return null;
    }

    /// <summary>La campagne en solo, sous ce nom (lancement de test : --name=, sinon le nom des réglages, ou « Joueur »).</summary>
    private void StartSoloCampaign(string name) => Host(name, port: 0, withCampaign: true, solo: true);

    private void StartSoloCampaign() =>
        StartSoloCampaign(DevArg("name") ?? (LoadSettings().Name is { Length: > 0 } saved ? saved : "Joueur"));

    private void Join(string name, string address, int port) => Connect(name, address, port, rejoining: false);

    private async void Connect(string name, string address, int port, bool rejoining)
    {
        var attempt = ++_connectionAttempt;
        _target = (name, address, port);
        // Le port du solo est choisi par le système : il ne remplace pas celui des réglages.
        SaveSettings(name, _server is null ? address : LoadSettings().Address, _solo ? LoadSettings().Port : port);
        _client?.Dispose();
        _client = null;
        _menu?.SetBusy(true);
        _menu?.SetStatus(_solo ? "Ouverture de la campagne…" : $"Connexion à {address}:{port}…");
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
            _client.Send(new JoinRequest(name, PlayerUid(), Protocol.Version, Protocol.CodeVersion, _content.Version));
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
    /// Reconnexion sous le même identifiant (désynchronisation, connexion perdue en combat) : le serveur renvoie tout le
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
        _solo = false;
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
            case JoinRefused { Reason: JoinRefusal.AlreadyConnected } when _rejoining && ++_rejoinAttempts < MaxRejoinAttempts:
                // Reconnexion : le serveur n'a pas encore vu partir l'ancienne connexion ; on réessaie un peu plus tard.
                _client?.Dispose();
                _client = null;
                _rejoinIn = 1;
                break;
            case JoinRefused refused:
                ShowMenu(Texts.JoinRefusal(refused.Reason), error: true);
                break;
            case LobbyState lobby:
                var recruited = MyHeroCount(lobby) > MyHeroCount(_lobby);
                _lobby = lobby;
                // Un trajet commence (quelqu'un a lancé la quête) ou s'arrête sans combat : le QG laisse place à la
                // carte, ou l'inverse (l'académie reste ouverte : en revenir mène à la carte). Un refus reçu juste avant
                // (le combat n'a pas pu se lancer à l'arrivée) dit pourquoi.
                var travelling = lobby.Campaign?.Trip is not null && _content.World is not null;
                var refusal = _tripRefusal;
                _tripRefusal = null;
                if (_networkCombat is null && (_hqScreen is not null || _mapScreen is not null) && travelling != (_mapScreen is not null))
                    ShowLobby(travelling ? "" : refusal ?? "Le trajet s'est arrêté : retour au QG.", error: refusal is not null);
                else if (_mapScreen is not null)
                    _mapScreen.Refresh(lobby);
                else if (_academyScreen is not null)
                    _academyScreen.Refresh(lobby, recruited);
                else if (_hqScreen is not null && _networkCombat is null)
                    _hqScreen.Refresh(lobby);
                else if (_networkCombat is not null)
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
            case TrainingRefused refused:
                _academyScreen?.SetStatus(Texts.TrainingRefusal(refused.Reason), error: true);
                break;
            case RecruitRefused refused:
                _academyScreen?.SetStatus(Texts.RecruitRefusal(refused.Reason), error: true);
                break;
            case RequestRefused refused:
                _lobbyScreen?.SetStatus(Texts.RequestRefusal(refused.Reason), error: true);
                _academyScreen?.SetStatus(Texts.RequestRefusal(refused.Reason), error: true);
                _hqScreen?.SetStatus(Texts.RequestRefusal(refused.Reason), error: true);
                _mapScreen?.SetStatus(Texts.RequestRefusal(refused.Reason), error: true);
                if (_mapScreen is not null)
                    _tripRefusal = Texts.RequestRefusal(refused.Reason);
                break;
        }
    }

    private static int MyHeroCount(LobbyState? lobby) => lobby?.Campaign?.Heroes.Count(h => h.Mine) ?? 0;

    /// <summary>
    /// Outils de développement : choisir son héros et lancer tout seuls (--pick, --start), ouvrir l'académie (--academy),
    /// lancer une quête avec son premier héros (--quest) et, en route, choisir une option (--option, « pass » pour passer
    /// son chemin) puis aller à un lieu (--travel).
    /// </summary>
    private void DevLobbyActions(LobbyState lobby)
    {
        if (_networkCombat is not null || _playerId is not { } me)
            return;
        if (_devQuest is { } quest && lobby.Campaign is { } campaign)
        {
            _devQuest = null; // un trajet repris est en cours : la quête demandée n'est pas lancée
            if (campaign.Trip is null)
            {
                _client?.Send(new AcceptQuest(quest));
                _client?.Send(new DeployHero(campaign.Heroes.FirstOrDefault(h => h.Mine && !h.IsDead)?.Id));
                _client?.Send(new StartCombatRequest());
            }
        }
        if (_devOption is { } option && lobby.Campaign?.Trip?.Event is not null)
        {
            _devOption = null;
            if (int.TryParse(option, out var rank))
                _client?.Send(new ResolveEvent(rank));
            else if (option == "pass")
                _client?.Send(new ResolveEvent(null));
            else
                GD.PushWarning($"--option={option}: expected an option's rank or 'pass'.");
        }
        if (_devTravel is { } place && lobby.Campaign?.Trip?.Moves.Contains(place) == true)
        {
            _devTravel = null;
            _client?.Send(new TravelTo(place));
        }
        if (_devAcademy && lobby.Campaign is not null && _hqScreen is not null)
        {
            _devAcademy = false;
            CallDeferred(nameof(ShowAcademy));
        }
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
        if (_solo)
            return "Campagne en solo, sur ce PC (la même que tu héberges pour tes amis).";
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
