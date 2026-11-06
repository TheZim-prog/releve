using System.Net.NetworkInformation;
using System.Net.Sockets;
using Game.Core;
using Game.Core.Network;
using Godot;

namespace Game.Client;

/// <summary>
/// Racine du jeu : enchaîne les écrans (menu, salon, combat), tient la connexion au serveur et, quand le joueur
/// héberge, le serveur lui-même (le même que le serveur dédié, dans ce processus). Les messages du serveur sont lus à
/// chaque image et répartis entre le salon et le combat.
/// <para>
/// Outils de développement (arguments après « -- ») : --solo (et --ai-heroes=on) lance directement le combat solo ;
/// --host ou --join=adresse, avec --name=, --port=, --pick=&lt;n° du héros&gt; et --start=&lt;nb de joueurs&gt;, enchaînent
/// connexion, choix du héros et lancement ; --screenshot=fichier.png capture l'écran après --at=&lt;s&gt; (4 par défaut)
/// puis quitte.
/// </para>
/// </summary>
public partial class Main : Node
{
    private const string ScenarioId = "street";
    private const string SettingsPath = "user://settings.cfg";
    private const double CombatOverDelay = 5;
    private const int MaxRejoinAttempts = 5;

    private GameContent _content = null!;
    private Node? _screen;
    private MenuScreen? _menu;
    private LobbyScreen? _lobbyScreen;
    private NetworkCombat? _networkCombat;

    private GameServer? _server;
    private GameClient? _client;
    private int? _playerId;
    private LobbyState? _lobby;
    private (string Name, string Address, int Port) _target;
    private double _backToLobbyIn = -1;
    private int _connectionAttempt;

    // Reconnexion automatique en cours (désynchronisation, coupure en combat), jusqu'au retour du combat ou du salon.
    private bool _rejoining;
    private int _rejoinAttempts;
    private double _rejoinIn = -1;

    private string? _screenshotPath;
    private double _screenshotIn;
    private int? _devPick;
    private int? _devStartWith;

    /// <summary>Valeur d'un argument de développement « --nom=valeur » (ou « --nom » seul : chaîne vide), sinon <c>null</c>.</summary>
    public static string? DevArg(string name)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == $"--{name}")
                return "";
            if (arg.StartsWith($"--{name}="))
                return arg[(name.Length + 3)..];
        }
        return null;
    }

    public override void _Ready()
    {
        _content = GameData.Load();
        _screenshotPath = DevArg("screenshot");
        _screenshotIn = double.TryParse(DevArg("at"), System.Globalization.CultureInfo.InvariantCulture, out var at) ? at : 4;
        if (int.TryParse(DevArg("pick"), out var pick))
            _devPick = pick;
        if (int.TryParse(DevArg("start"), out var start))
            _devStartWith = start;

        var (name, address, port) = LoadSettings();
        name = DevArg("name") ?? name;
        port = int.TryParse(DevArg("port"), out var p) ? p : port;
        if (DevArg("solo") is not null || DevArg("ai-heroes") == "on")
            StartSolo();
        else if (DevArg("host") is not null)
            Host(name, port);
        else if (DevArg("join") is { Length: > 0 } joinAddress)
            Join(name, joinAddress, port);
        else
            ShowMenu();
    }

    public override void _Process(double delta)
    {
        PollServer();
        if (_backToLobbyIn > 0 && (_backToLobbyIn -= delta) <= 0)
            ShowLobby();
        if (_rejoinIn > 0 && (_rejoinIn -= delta) <= 0)
            Connect(_target.Name, _target.Address, _target.Port, rejoining: true);
        if (_screenshotPath is not null && (_screenshotIn -= delta) <= 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GetTree().Quit();
        }
    }

    public override void _ExitTree() => Disconnect();

    // --- Écrans ---

    private void Show(Node screen)
    {
        _screen?.QueueFree();
        _screen = screen;
        _menu = screen as MenuScreen;
        _lobbyScreen = screen as LobbyScreen;
        AddChild(screen);
    }

    private void ShowMenu(string status = "", bool error = false)
    {
        Disconnect();
        var (name, address, port) = LoadSettings();
        var menu = new MenuScreen { PlayerName = name, Address = address, Port = port };
        menu.SoloPressed += StartSolo;
        menu.HostPressed += Host;
        menu.JoinPressed += Join;
        Show(menu);
        menu.SetStatus(status, error);
    }

    private void StartSolo()
    {
        Disconnect();
        var view = new CombatView();
        view.Setup(new SoloCombat(_content, ScenarioId, aiHeroes: DevArg("ai-heroes") == "on"));
        view.LeaveRequested += () => ShowMenu();
        Show(view);
    }

    private void ShowLobby(string status = "", bool error = false)
    {
        _backToLobbyIn = -1;
        _rejoining = false;
        _networkCombat = null;
        if (_playerId is not { } me)
            return;
        var lobby = new LobbyScreen(CombatLaunch.PlayableHeroes(_content, ScenarioId), me, HostInfo());
        lobby.HeroPicked += hero => _client?.Send(new PickHero(hero));
        lobby.StartPressed += () => _client?.Send(new StartCombatRequest());
        lobby.LeavePressed += () => ShowMenu();
        Show(lobby);
        if (_lobby is not null)
            lobby.Refresh(_lobby);
        lobby.SetStatus(status, error);
    }

    private void ShowNetworkCombat(CombatState state)
    {
        _backToLobbyIn = -1;
        _rejoining = false;
        _networkCombat = new NetworkCombat(_content, _client!, _playerId!.Value, state, _lobby?.Players ?? Array.Empty<PlayerInfo>());
        var view = new CombatView();
        view.Setup(_networkCombat);
        view.LeaveRequested += () => ShowMenu();
        view.Desynced += reason =>
        {
            GD.PushWarning($"Desync: {reason}");
            Rejoin("Resynchronisation…");
        };
        Show(view);
    }

    // --- Connexion ---

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
            _client.Send(new JoinRequest(name, Protocol.Version, _content.Version));
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

    // --- Réglages (dernier nom, adresse et port) ---

    private static (string Name, string Address, int Port) LoadSettings()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath); // absent la première fois : valeurs par défaut
        return ((string)config.GetValue("player", "name", ""), (string)config.GetValue("network", "address", "127.0.0.1"),
            (int)config.GetValue("network", "port", Protocol.DefaultPort));
    }

    private static void SaveSettings(string name, string address, int port)
    {
        if (DevArg("name") is not null)
            return; // lancement de test : on ne touche pas aux réglages du joueur
        var config = new ConfigFile();
        config.SetValue("player", "name", name);
        config.SetValue("network", "address", address);
        config.SetValue("network", "port", port);
        config.Save(SettingsPath);
    }
}
