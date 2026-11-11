using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Network;
using Xunit.Abstractions;
using static Game.Core.Tests.Network.ServerSessionTests;

namespace Game.Core.Tests.Network;

/// <summary>Le serveur et les clients sur une vraie connexion TCP locale.</summary>
public class TcpTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Le serveur joue en temps réel : un ennemi coriace, pour que le combat dure le temps du test.
    private static readonly GameContent LongCombat = new()
    {
        Rules = Content.Rules,
        Scenarios = Content.Scenarios,
        Version = Content.Version,
        Units = Content.Units.ToDictionary(u => u.Key, u => u.Key == "thug" ? u.Value with { MaxHp = 100_000 } : u.Value),
    };

    /// <summary>Les messages reçus par un client, gardés dans l'ordre.</summary>
    private sealed class Inbox(GameClient client)
    {
        public List<ServerMessage> All { get; } = new();

        public GameClient Client => client;

        public async Task<T> WaitFor<T>(Func<T, bool>? match = null) where T : ServerMessage
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (true)
            {
                while (client.TryReceive(out var message))
                    All.Add(message);
                if (All.OfType<T>().LastOrDefault(m => match?.Invoke(m) ?? true) is { } found)
                    return found;
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"No {typeof(T).Name} received. Last: "
                        + string.Join(", ", All.Where(m => m is not TickPlayed).TakeLast(5)) + $"; ticks: {All.OfType<TickPlayed>().Count()}");
                await Task.Delay(5);
            }
        }
    }

    private GameServer StartServer(GameContent content)
    {
        var server = new GameServer(content, "test", port: 0, log: line =>
        {
            try
            {
                output.WriteLine(line);
            }
            catch (InvalidOperationException)
            {
                // Test déjà terminé.
            }
        });
        server.Start();
        return server;
    }

    private static async Task<Inbox> Join(GameServer server, string name)
    {
        var inbox = new Inbox(await GameClient.ConnectAsync("127.0.0.1", server.Port));
        inbox.Client.Send(new JoinRequest(name, Protocol.Version, Protocol.CodeVersion, Content.Version));
        await inbox.WaitFor<Welcome>();
        return inbox;
    }

    [Fact]
    public async Task TwoClients_PlayTheSameCombat_OverTcp()
    {
        using var server = StartServer(LongCombat);
        var alice = await Join(server, "Alice");
        var bob = await Join(server, "Bob");

        alice.Client.Send(new PickHero(HeroA));
        bob.Client.Send(new PickHero(HeroB));
        await alice.WaitFor<LobbyState>(l => l.Players.Count == 2 && l.Players.All(p => p.Hero is not null));
        bob.Client.Send(new StartCombatRequest());
        var mirrors = new[]
        {
            new CombatMirror(LongCombat, await alice.WaitFor<CombatState>()), new CombatMirror(LongCombat, await bob.WaitFor<CombatState>()),
        };
        alice.Client.Send(new SendCommand(new OrderCommand(new ActivateCommand(HeroA, new Cell(2, 0)))));

        // Chaque client rejoue les ticks reçus jusqu'au 20e : même état que l'autre, empreintes vérifiées au passage.
        foreach (var (inbox, mirror) in new[] { alice, bob }.Zip(mirrors))
        {
            await inbox.WaitFor<TickPlayed>(t => t.Tick >= 20);
            foreach (var tick in inbox.All.OfType<TickPlayed>().Where(t => t.Tick <= 20))
                mirror.Apply(tick);
        }
        Assert.Equal(mirrors[0].Simulation.Checksum(), mirrors[1].Simulation.Checksum());
        Assert.Equal(new Cell(2, 0), mirrors[1].Simulation.FindUnit(HeroA)!.Position);

        // Bob perd la connexion : le combat se met en pause pour tout le monde.
        bob.Client.Dispose();
        await alice.WaitFor<PauseChanged>(p => p is { Paused: true, ByPlayer: null });

        // Il revient sous le même nom et rattrape le combat.
        var bobAgain = await Join(server, "Bob");
        var caughtUp = new CombatMirror(LongCombat, await bobAgain.WaitFor<CombatState>());
        Assert.True(caughtUp.Simulation.CurrentTick >= 20);
        await alice.WaitFor<PauseChanged>(p => !p.Paused);
    }

    [Fact]
    public async Task AClientWithOtherContent_IsRefused()
    {
        using var server = StartServer(Content);
        var stranger = new Inbox(await GameClient.ConnectAsync("127.0.0.1", server.Port));

        stranger.Client.Send(new JoinRequest("Eve", Protocol.Version, Protocol.CodeVersion, "other"));

        Assert.Equal(JoinRefusal.ContentMismatch, (await stranger.WaitFor<JoinRefused>()).Reason);
    }

    [Fact]
    public async Task StoppingTheServer_DisconnectsItsClients()
    {
        var server = StartServer(Content);
        var alice = await Join(server, "Alice");

        server.Dispose();

        var deadline = DateTime.UtcNow + Timeout;
        while (alice.Client.IsConnected && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.False(alice.Client.IsConnected);
    }

    [Fact]
    public async Task AnEndlessLine_GetsTheSenderDisconnected()
    {
        using var server = StartServer(Content);
        using var raw = new System.Net.Sockets.TcpClient();
        await raw.ConnectAsync("127.0.0.1", server.Port);
        var stream = raw.GetStream();
        var chunk = new byte[1024 * 1024];
        Array.Fill(chunk, (byte)'x');

        var cut = false;
        try
        {
            for (var i = 0; i < 64 && !cut; i++)
                await stream.WriteAsync(chunk);
            cut = await stream.ReadAsync(new byte[1]) == 0; // le serveur a fermé
        }
        catch (IOException)
        {
            cut = true;
        }

        Assert.True(cut);
    }

    [Fact]
    public async Task Connecting_ToNoServer_Fails()
    {
        var server = StartServer(Content);
        var port = server.Port;
        server.Dispose();

        await Assert.ThrowsAnyAsync<System.Net.Sockets.SocketException>(() => GameClient.ConnectAsync("127.0.0.1", port));
    }
}
