using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Network;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Network;

public class ServerSessionTests
{
    private const string ScenarioJson = """
        {
          "id": "test", "name": "Test", "seed": 3,
          "map": [ "......", "......", "......" ],
          "units": [
            { "unit": "hero-a", "team": 0, "x": 0, "y": 0 },
            { "unit": "hero-b", "team": 0, "x": 0, "y": 1 },
            { "unit": "hero-c", "team": 0, "x": 0, "y": 2 },
            { "unit": "thug", "team": 1, "x": 5, "y": 1 }
          ]
        }
        """;

    internal static readonly UnitId HeroA = new(1), HeroB = new(2), HeroC = new(3), Thug = new(4);

    /// <summary>Contenu de test : trois héros (A, B, C) et un ennemi à l'IA de base.</summary>
    internal static readonly GameContent Content = new()
    {
        Rules = Rules,
        Units = new Dictionary<string, UnitDefinition>
        {
            ["hero-a"] = Unit(0, 0, 0, 0).Definition with { Id = "hero-a" },
            ["hero-b"] = Unit(0, 0, 0, 0).Definition with { Id = "hero-b" },
            ["hero-c"] = Unit(0, 0, 0, 0).Definition with { Id = "hero-c" },
            ["thug"] = Unit(0, 1, 0, 0, hp: 60, behavior: Behaviors.Basic).Definition with { Id = "thug" },
        },
        Scenarios = new Dictionary<string, ScenarioDefinition> { ["test"] = ScenarioDefinition.FromJson(ScenarioJson) },
        Version = "v1",
    };

    /// <summary>Client de test : chaque message reçu passe par le JSON, comme sur le réseau.</summary>
    private sealed class FakeClient(ServerSession session) : IPeer
    {
        public List<ServerMessage> Received { get; } = new();

        public void Send(ServerMessage message) => Received.Add(NetJson.DeserializeServer(NetJson.Serialize(message)));

        public void Say(ClientMessage message) => session.Receive(this, NetJson.DeserializeClient(NetJson.Serialize(message)));

        public T Last<T>() where T : ServerMessage => Received.OfType<T>().Last();

        public void Join(string name, string content = "v1", int protocol = Protocol.Version, string? code = null) =>
            Say(new JoinRequest(name, protocol, code ?? Protocol.CodeVersion, content));
    }

    private static (ServerSession Session, FakeClient Alice, FakeClient Bob) TwoPlayersInCombat()
    {
        var session = new ServerSession(Content, "test");
        var alice = new FakeClient(session);
        var bob = new FakeClient(session);
        alice.Join("Alice");
        bob.Join("Bob");
        alice.Say(new PickHero(HeroA));
        bob.Say(new PickHero(HeroB));
        alice.Say(new StartCombatRequest());
        return (session, alice, bob);
    }

    private static void AssertSameState(CombatSimulation expected, CombatSimulation actual)
    {
        Assert.Equal(expected.CurrentTick, actual.CurrentTick);
        Assert.Equal(expected.CommandLog, actual.CommandLog);
        Assert.Equal(expected.EventLog, actual.EventLog);
        Assert.Equal(
            expected.Units.Select(u => (u.Id, u.Position, u.Gauge, u.Hp, u.Status)),
            actual.Units.Select(u => (u.Id, u.Position, u.Gauge, u.Hp, u.Status)));
    }

    [Fact]
    public void Joining_IsRefused_OnVersionMismatchOrBadName()
    {
        var session = new ServerSession(Content, "test");
        var alice = new FakeClient(session);
        alice.Join("Alice");

        void Refused(JoinRefusal reason, string name, string content = "v1", int protocol = Protocol.Version, string? code = null)
        {
            var client = new FakeClient(session);
            client.Join(name, content, protocol, code);
            Assert.Equal(new JoinRefused(reason), Assert.Single(client.Received));
        }

        Refused(JoinRefusal.ProtocolMismatch, "Bob", protocol: Protocol.Version + 1);
        Refused(JoinRefusal.CodeMismatch, "Bob", code: "0000000000000000");
        Refused(JoinRefusal.ContentMismatch, "Bob", content: "v2");
        Refused(JoinRefusal.InvalidName, "  ");
        Refused(JoinRefusal.InvalidName, new string('x', Protocol.MaxNameLength + 1));
        Refused(JoinRefusal.NameTaken, "alice");
        Assert.Equal(new Welcome(1), alice.Received[0]);
    }

    [Fact]
    public void InTheLobby_EachPlayerPicksAFreeHero_ThenAnyoneStarts()
    {
        var session = new ServerSession(Content, "test");
        var alice = new FakeClient(session);
        var bob = new FakeClient(session);
        alice.Join("Alice");
        bob.Join("Bob");

        alice.Say(new PickHero(HeroA));
        bob.Say(new PickHero(HeroA));
        Assert.Equal(new RequestRefused(RequestRefusal.HeroTaken), bob.Received[^1]);
        bob.Say(new PickHero(Thug));
        Assert.Equal(new RequestRefused(RequestRefusal.NotAHero), bob.Received[^1]);
        bob.Say(new StartCombatRequest());
        Assert.Equal(new RequestRefused(RequestRefusal.HeroesMissing), bob.Received[^1]);

        bob.Say(new PickHero(HeroB));
        Assert.Equal(
            new[] { new PlayerInfo(1, "Alice", true, HeroA), new PlayerInfo(2, "Bob", true, HeroB) },
            alice.Last<LobbyState>().Players);
        bob.Say(new StartCombatRequest());

        var state = alice.Last<CombatState>();
        Assert.Equal(state.Launch.Heroes, bob.Last<CombatState>().Launch.Heroes);
        Assert.Equal(0, state.Tick);
        Assert.NotNull(session.Simulation);
        // Le héros que personne n'a choisi est joué par l'IA.
        var mirror = new CombatMirror(Content, state);
        Assert.Null(mirror.Simulation.Get(1).Definition.Behavior);
        Assert.Equal(Behaviors.Basic, mirror.Simulation.Get(3).Definition.Behavior);
        Assert.Equal(1, mirror.PlayerOf(HeroA));
        Assert.Null(mirror.PlayerOf(HeroC));

        var late = new FakeClient(session);
        late.Join("Carol");
        Assert.Equal(new JoinRefused(JoinRefusal.CombatInProgress), Assert.Single(late.Received));
    }

    [Fact]
    public void InCombat_ClientsReplayTheServerTicks_AndStayIdentical()
    {
        var (session, alice, bob) = TwoPlayersInCombat();
        var mirrors = new[] { new CombatMirror(Content, alice.Last<CombatState>()), new CombatMirror(Content, bob.Last<CombatState>()) };

        alice.Say(new SendCommand(new OrderCommand(new ActivateCommand(HeroA, new Cell(2, 0)))));
        alice.Say(new SendCommand(new OrderCommand(new ActivateCommand(HeroB, new Cell(2, 1)))));
        Assert.Equal(new RequestRefused(RequestRefusal.NotYourUnit), alice.Received[^1]);
        bob.Say(new SendCommand(new OrderCommand(new ActivateCommand(HeroB, new Cell(2, 2)))));
        for (var i = 0; i < 25; i++)
            session.Tick();

        Assert.Equal(25, alice.Received.OfType<TickPlayed>().Count());
        Assert.Equal(2, alice.Received.OfType<TickPlayed>().First().Commands.Count);
        foreach (var (mirror, client) in mirrors.Zip(new[] { alice, bob }))
        {
            foreach (var tick in client.Received.OfType<TickPlayed>())
                mirror.Apply(tick);
            AssertSameState(session.Simulation!, mirror.Simulation);
        }
        Assert.Equal(new Cell(2, 0), session.Simulation!.Get(1).Position);
        Assert.Equal(new Cell(2, 2), session.Simulation!.Get(2).Position);
    }

    [Fact]
    public void AnyPlayerPausesAndResumes_ForEveryone()
    {
        var (session, alice, bob) = TwoPlayersInCombat();

        bob.Say(new SetPause(true));
        session.Tick();
        Assert.Equal(new PauseChanged(true, 2), alice.Last<PauseChanged>());
        Assert.Empty(alice.Received.OfType<TickPlayed>());

        alice.Say(new SetPause(false));
        session.Tick();
        Assert.Equal(new PauseChanged(false, null), bob.Last<PauseChanged>());
        Assert.Single(bob.Received.OfType<TickPlayed>());
    }

    [Fact]
    public void ADisconnection_PausesTheCombat_UntilThePlayerComesBackAndCatchesUp()
    {
        var (session, alice, bob) = TwoPlayersInCombat();
        bob.Say(new SendCommand(new OrderCommand(new ActivateCommand(HeroB, new Cell(1, 1)))));
        for (var i = 0; i < 15; i++)
            session.Tick();

        session.Disconnect(bob);
        Assert.Equal(new PauseChanged(true, null), alice.Last<PauseChanged>());
        Assert.False(alice.Last<LobbyState>().Players.Single(p => p.Id == 2).Connected);
        session.Tick();
        Assert.Equal(15, alice.Received.OfType<TickPlayed>().Count());

        var bobAgain = new FakeClient(session);
        bobAgain.Join("Bob");

        Assert.Equal(new Welcome(2), bobAgain.Received[0]);
        var mirror = new CombatMirror(Content, bobAgain.Last<CombatState>());
        AssertSameState(session.Simulation!, mirror.Simulation);
        Assert.Equal(new PauseChanged(false, null), alice.Last<PauseChanged>()); // reprise automatique
        session.Tick();
        mirror.Apply(bobAgain.Last<TickPlayed>());
        AssertSameState(session.Simulation!, mirror.Simulation);
    }

    [Fact]
    public void APauseByAPlayer_IsKeptWhenADisconnectedPlayerComesBack()
    {
        var (session, alice, bob) = TwoPlayersInCombat();
        alice.Say(new SetPause(true));
        session.Disconnect(bob);

        var bobAgain = new FakeClient(session);
        bobAgain.Join("Bob");

        Assert.True(session.IsPaused);
        Assert.Equal(new PauseChanged(true, 1), alice.Last<PauseChanged>());
        Assert.True(bobAgain.Last<CombatState>().Paused);
    }

    [Fact]
    public void AnyPlayer_CanLiftADisconnectionPause()
    {
        var (session, alice, bob) = TwoPlayersInCombat();
        session.Disconnect(bob);

        alice.Say(new SetPause(false));
        session.Tick();

        Assert.False(session.IsPaused);
        Assert.Single(alice.Received.OfType<TickPlayed>());
    }

    [Fact]
    public void WhenTheCombatEnds_EveryoneGoesBackToTheLobby_WithTheirHeroes()
    {
        var (session, alice, bob) = TwoPlayersInCombat();

        for (var i = 0; i < 2000 && session.Simulation is not null; i++)
            session.Tick();

        Assert.Null(session.Simulation);
        Assert.Equal(new CombatOver(true), alice.Last<CombatOver>());
        Assert.Equal(new[] { HeroA, HeroB }, bob.Last<LobbyState>().Players.Select(p => p.Hero!.Value));
        bob.Say(new StartCombatRequest());
        Assert.NotNull(session.Simulation);
    }

    [Fact]
    public void AnOrderForSomeoneElsesHero_IsRefused_EvenWithASpoofedUnit()
    {
        var (session, alice, _) = TwoPlayersInCombat();
        // Message trafiqué : l'ordre se dit pour le héros d'Alice, mais son activation vise celui de Bob.
        var json = """
            {"type":"command","command":{"type":"order","unit":{"value":1},
             "activation":{"unit":{"value":2},"moveTo":{"x":1,"y":1}}}}
            """;

        session.Receive(alice, NetJson.DeserializeClient(json));
        session.Tick();

        Assert.Equal(new RequestRefused(RequestRefusal.NotYourUnit), alice.Received.OfType<RequestRefused>().Last());
        Assert.Empty(alice.Last<TickPlayed>().Commands);
    }

    [Fact]
    public void MalformedMessages_AreRejectedWithoutCrashing()
    {
        var (session, alice, _) = TwoPlayersInCombat();

        Assert.Throws<System.Text.Json.JsonException>(() =>
            NetJson.DeserializeClient("""{"type":"command","command":{"type":"order","activation":null}}"""));
        Assert.Throws<System.Text.Json.JsonException>(() => NetJson.DeserializeClient("""{"type":"nope"}"""));
        session.Receive(alice, NetJson.DeserializeClient(
            """{"type":"command","command":{"type":"activate","unit":{"value":1},"skill":{"target":{"value":4}}}}"""));
        session.Receive(alice, new SendCommand(null!));
        session.Tick();

        Assert.Equal(2, alice.Received.OfType<RequestRefused>().Count(r => r.Reason == RequestRefusal.UnknownCommand));
        Assert.Empty(alice.Last<TickPlayed>().Commands);
    }

    [Fact]
    public void QueuedCommands_AreCappedPerPlayer()
    {
        var (session, alice, _) = TwoPlayersInCombat();
        alice.Say(new SetPause(true));

        for (var i = 0; i <= Protocol.MaxQueuedCommandsPerPlayer; i++)
            alice.Say(new SendCommand(new OrderCommand(new ActivateCommand(HeroA, new Cell(1, 0)))));

        Assert.Equal(new RequestRefused(RequestRefusal.TooManyCommands), alice.Received[^1]);
        alice.Say(new SetPause(false));
        session.Tick();
        Assert.Equal(Protocol.MaxQueuedCommandsPerPlayer, alice.Last<TickPlayed>().Commands.Count);
    }

    [Fact]
    public void PausingDuringADisconnectionPause_KeepsTheCombatPausedWhenThePlayerReturns()
    {
        var (session, alice, bob) = TwoPlayersInCombat();
        session.Disconnect(bob);

        alice.Say(new SetPause(true));
        new FakeClient(session).Join("Bob");

        Assert.True(session.IsPaused);
        Assert.Equal(new PauseChanged(true, 1), alice.Last<PauseChanged>());
    }

    [Fact]
    public void AMirror_ShowsSentOrders_BeforeTheServerPlaysThem()
    {
        var (session, alice, _) = TwoPlayersInCombat();
        var mirror = new CombatMirror(Content, alice.Last<CombatState>());
        var order = new OrderCommand(new ActivateCommand(HeroA, new Cell(1, 0)));

        alice.Say(new SendCommand(order));
        mirror.Sent(order);
        Assert.Equal(new[] { order.Activation }, mirror.PlannedOrders());

        session.Tick();
        mirror.Apply(alice.Last<TickPlayed>());
        Assert.Equal(new[] { order.Activation }, mirror.PlannedOrders()); // désormais dans la simulation

        mirror.Sent(new CancelOrderCommand(HeroA));
        Assert.Empty(mirror.PlannedOrders());
        mirror.ForgetSent(); // refusée par le serveur : l'ordre réel réapparaît
        Assert.Equal(new[] { order.Activation }, mirror.PlannedOrders());
        Assert.Equal(HeroA, mirror.HeroOf(1));
    }

    [Fact]
    public void AMirrorThatDrifts_DetectsTheDesync()
    {
        var (session, alice, _) = TwoPlayersInCombat();
        var mirror = new CombatMirror(Content, alice.Last<CombatState>());
        session.Tick();
        var tick = alice.Last<TickPlayed>();

        Assert.Throws<DesyncException>(() => mirror.Apply(tick with { Checksum = tick.Checksum + 1 }));
        Assert.Throws<DesyncException>(() => new CombatMirror(Content, alice.Last<CombatState>()).Apply(tick with { Tick = 2 }));
    }

    [Fact]
    public void RequestsBeforeJoining_AreRefused()
    {
        var session = new ServerSession(Content, "test");
        var stranger = new FakeClient(session);

        stranger.Say(new SetPause(true));

        Assert.Equal(new RequestRefused(RequestRefusal.NotJoined), Assert.Single(stranger.Received));
    }

    [Fact]
    public void TheRealContent_LoadsWithAVersion_AndBuildsTheStreetCombat()
    {
        var content = GameContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var heroes = CombatLaunch.PlayableHeroes(content, "street");

        Assert.Equal(16, content.Version.Length);
        Assert.Equal(content.Version, GameContent.Load(Path.Combine(AppContext.BaseDirectory, "data")).Version);
        Assert.Equal(6, heroes.Count);
        var launch = new CombatLaunch("street", new[] { new HeroAssignment(heroes[0].Id, 1), new HeroAssignment(heroes[1].Id, 2) });
        var setup = launch.BuildSetup(content);
        Assert.Equal(4, setup.Units.Count(u => u.Team == CombatLaunch.PlayerTeam && u.Definition.Behavior == Behaviors.Basic));
    }
}
