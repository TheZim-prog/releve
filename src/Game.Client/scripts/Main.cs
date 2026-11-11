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
        try
        {
            _content = GameData.Load();
        }
        catch (Exception e)
        {
            // Le jeu exporté n'a pas de console : sans ce message, le joueur ne verrait qu'une fenêtre vide.
            GD.PushError(e.ToString());
            OS.Alert("Le contenu du jeu (dossier data) est absent ou abîmé.\n" +
                "Supprime ce dossier du jeu, puis dézippe à nouveau le jeu complet.\n\n" + e.Message, "Relève");
            SetProcess(false);
            GetTree().Quit(1);
            return;
        }
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
        // La version affichée : celle du code puis celle du contenu, à comparer entre amis quand le serveur refuse.
        var version = $"{Protocol.CodeVersion[..6]}-{_content.Version[..6]}";
        var menu = new MenuScreen { PlayerName = name, Address = address, Port = port, GameVersion = version };
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
