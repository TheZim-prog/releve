using Game.Core;
using Game.Core.Network;
using Godot;

namespace Game.Client;

/// <summary>
/// Racine du jeu : enchaîne les écrans (menu, salon, combat), tient la connexion au serveur et, quand le joueur
/// héberge, le serveur lui-même (le même que le serveur dédié, dans ce processus). Les messages du serveur sont lus à
/// chaque image et répartis entre le salon et le combat.
/// <para>
/// Outils de développement (arguments après « -- ») : --scenario=&lt;id&gt; choisit le scénario (solo et partie
/// hébergée) ; --solo (et --ai-heroes=on) lance directement le combat solo ; --solo-campaign, la campagne en solo ;
/// --host ou --join=adresse, avec --name=, --port=, --pick=&lt;n° du héros&gt; et --start=&lt;nb de joueurs&gt;, enchaînent
/// connexion, choix du héros et lancement ; --campaign héberge la campagne (sa sauvegarde de test) plutôt qu'un combat
/// libre ; --academy ouvre l'académie depuis le QG (--train=&lt;héros&gt; : et son entraînement) ; --screenshot=fichier.png capture l'écran après --at=&lt;s&gt; (4 par défaut)
/// puis quitte.
/// </para>
/// </summary>
public partial class Main : Node
{
    private const string DefaultScenario = "street";
    private const string SettingsPath = "user://settings.cfg";
    private const double CombatOverDelay = 5;
    private const int MaxRejoinAttempts = 5;

    private GameContent _content = null!;

    // Scénario du solo et de la partie hébergée, choisi dans le menu. Dans le salon, c'est celui du serveur qui compte.
    private string _scenarioId = DefaultScenario;
    private Node? _screen;
    private MenuScreen? _menu;
    private LobbyScreen? _lobbyScreen;
    private AcademyScreen? _academyScreen;
    private HqScreen? _hqScreen;
    private MapScreen? _mapScreen;

    /// <summary>Le dernier refus reçu sur la carte, à montrer au QG si le trajet s'arrête juste après (arrivée impossible).</summary>
    private string? _tripRefusal;
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
    private bool _devAcademy;
    private string? _devQuest;
    private string? _devTravel;
    private string? _devOption;

    // Campagne en solo : le serveur de ce jeu n'accepte que ce PC.
    private bool _solo;

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
        _devAcademy = DevArg("academy") is not null;
        _devQuest = DevArg("quest");
        _devTravel = DevArg("travel");
        _devOption = DevArg("option");
        if (DevArg("scenario") is { } scenario)
        {
            if (_content.Scenarios.ContainsKey(scenario))
                _scenarioId = scenario;
            else
                GD.PushError($"Unknown scenario '{scenario}' (available: {string.Join(", ", _content.Scenarios.Keys)}).");
        }
        KeepAKnownScenario();

        var (name, address, port) = LoadSettings();
        name = DevArg("name") ?? name;
        port = int.TryParse(DevArg("port"), out var p) ? p : port;
        if (DevArg("solo-campaign") is not null)
            StartSoloCampaign();
        else if (DevArg("solo") is not null || DevArg("ai-heroes") == "on")
            StartSolo();
        else if (DevArg("host") is not null)
            Host(name, port, withCampaign: DevArg("campaign") is not null);
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
        _academyScreen = screen as AcademyScreen;
        _hqScreen = screen as HqScreen;
        _mapScreen = screen as MapScreen;
        AddChild(screen);
    }

    private void ShowMenu(string status = "", bool error = false)
    {
        Disconnect();
        var (name, address, port) = LoadSettings();
        // La version affichée : celle du code puis celle du contenu, à comparer entre amis quand le serveur refuse.
        var version = $"{Protocol.CodeVersion[..6]}-{_content.Version[..6]}";
        var menu = new MenuScreen
        {
            PlayerName = name, Address = address, Port = port, GameVersion = version,
            // Les scénarios de quête n'ont pas de héros à eux : ils se jouent depuis le QG d'une campagne.
            Scenarios = _content.Scenarios.Values.Where(s => s.HeroSlots is null).Select(s => (s.Id, s.Name)).OrderBy(s => s.Name).ToList(),
            ScenarioId = _scenarioId,
        };
        menu.ScenarioChosen += id => _scenarioId = id;
        menu.SoloPressed += StartSolo;
        menu.SoloCampaignPressed += StartSoloCampaign;
        menu.HostPressed += (name, port, withCampaign) => Host(name, port, withCampaign);
        menu.JoinPressed += Join;
        Show(menu);
        menu.SetStatus(status, error);
    }

    private void StartSolo()
    {
        Disconnect();
        if (!ReloadContent())
            return;
        var view = new CombatView();
        view.Setup(new SoloCombat(_content, _scenarioId, aiHeroes: DevArg("ai-heroes") == "on"));
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
        if (_lobby?.Campaign is not null)
        {
            ShowHq(me, status, error);
            return;
        }
        var scenario = _lobby?.ScenarioId ?? _scenarioId;
        if (!_content.Scenarios.TryGetValue(scenario, out var played))
        {
            ShowMenu($"Le serveur joue un scénario inconnu de ce jeu (« {scenario} »).", error: true);
            return;
        }
        var lobby = new LobbyScreen(CombatLaunch.PlayableHeroes(_content, scenario), me, $"Scénario : {played.Name}\n{HostInfo()}");
        lobby.HeroPicked += hero => _client?.Send(new PickHero(hero));
        lobby.StartPressed += () => _client?.Send(new StartCombatRequest());
        lobby.LeavePressed += () => ShowMenu();
        Show(lobby);
        if (_lobby is not null)
            lobby.Refresh(_lobby);
        lobby.SetStatus(status, error);
    }

    /// <summary>
    /// Le QG d'une partie avec campagne : panneau de quêtes, héros déployés, académie ; ou, pendant le trajet d'une
    /// quête, la carte.
    /// </summary>
    private void ShowHq(int me, string status, bool error)
    {
        if (_lobby?.Campaign?.Trip is not null && _content.World is not null)
        {
            ShowMap(me, status, error);
            return;
        }
        var hq = new HqScreen(_content, me, HostInfo());
        hq.QuestChosen += quest => _client?.Send(new ChooseQuest(quest));
        hq.HeroDeployed += hero => _client?.Send(new DeployHero(hero));
        hq.StartPressed += () => _client?.Send(new StartCombatRequest());
        hq.WaitPressed += () => _client?.Send(new WaitHalfDay());
        hq.AcademyPressed += ShowAcademy;
        hq.AcademyAvailable = _content.Heroes is not null;
        hq.LeavePressed += () => ShowMenu();
        Show(hq);
        hq.Refresh(_lobby!);
        hq.SetStatus(status, error);
    }

    /// <summary>Le trajet vers la quête lancée : la carte, et les choix du joueur qui valide.</summary>
    private void ShowMap(int me, string status, bool error)
    {
        var map = new MapScreen(_content, me);
        map.TravelPressed += place => _client?.Send(new TravelTo(place));
        map.OptionPressed += option => _client?.Send(new ResolveEvent(option));
        map.LeavePressed += () => ShowMenu();
        Show(map);
        map.Refresh(_lobby!);
        map.SetStatus(status, error);
    }

    /// <summary>L'académie de la campagne du serveur (depuis le QG).</summary>
    private void ShowAcademy()
    {
        if (_content.Heroes is null || _lobby?.Campaign is null)
            return;
        var academy = new AcademyScreen(_content);
        academy.RecruitPressed += recruit => _client?.Send(new RecruitHero(recruit));
        academy.SpendPressed += (hero, stat) => _client?.Send(new SpendStatPoint(hero, stat));
        academy.LearnPressed += (hero, skill) => _client?.Send(new LearnSkill(hero, skill));
        academy.EquipPressed += (hero, equipped, passive) => _client?.Send(new EquipHero(hero, equipped, passive));
        academy.BackPressed += () => ShowLobby();
        Show(academy);
        academy.Refresh(_lobby);
        if (DevArg("train") is { Length: > 0 } trained)
            academy.Train(trained);
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

    // --- Contenu ---

    /// <summary>
    /// Relit le dossier data avant un solo ou une partie hébergée : ce qui a été modifié depuis le lancement du jeu est
    /// pris en compte. Si le contenu n'est plus valide, retour au menu avec l'erreur (l'ancien contenu est gardé).
    /// </summary>
    private bool ReloadContent()
    {
        try
        {
            _content = GameData.Load();
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            ShowMenu($"Le contenu du jeu (dossier data) n'est pas valide : {e.Message}", error: true);
            return false;
        }
        KeepAKnownScenario();
        return true;
    }

    /// <summary>
    /// Le scénario choisi doit être un combat libre du contenu chargé (pas une quête, qui n'a pas de héros à elle) ;
    /// sinon, le scénario par défaut, ou le premier combat libre.
    /// </summary>
    private void KeepAKnownScenario()
    {
        bool IsFreeCombat(string id) => _content.Scenarios.TryGetValue(id, out var scenario) && scenario.HeroSlots is null;
        if (!IsFreeCombat(_scenarioId))
            _scenarioId = IsFreeCombat(DefaultScenario) ? DefaultScenario : _content.Scenarios.Keys.Order().First(IsFreeCombat);
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
