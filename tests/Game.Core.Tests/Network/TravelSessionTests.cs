using Game.Core.Campaign;
using Game.Core.Network;
using Game.Core.World;
using static Game.Core.Tests.Network.CampaignSessionTests;
using static Game.Core.Tests.Network.ServerSessionTests;

namespace Game.Core.Tests.Network;

/// <summary>
/// Le trajet d'une quête sur un serveur de campagne : du QG au lieu de la mission (tout droit, ou par le point « poi »
/// et son événement), décidé par le joueur qui a choisi la quête, jusqu'au combat.
/// </summary>
public class TravelSessionTests
{
    private const string MapJson = """
        {
          "nodes": [
            { "id": "hq", "name": "QG", "x": 50, "y": 90, "kind": "hq" },
            { "id": "street", "name": "Rue", "x": 50, "y": 10, "kind": "site" },
            { "id": "poi", "name": "Pont", "x": 20, "y": 50, "kind": "poi", "event": "car" }
          ],
          "paths": [ [ "hq", "street" ], [ "hq", "poi" ], [ "poi", "street" ] ]
        }
        """;

    /// <summary>Une voiture qui bascule : la retenir (un test à 50 %), ou s'entraîner un peu (bonus, mais une demi-chute à chacun).</summary>
    private static readonly WorldEvent Car = new()
    {
        Id = "car", Title = "Voiture", Text = "Une voiture bascule.",
        Options = new[]
        {
            new EventOption
            {
                Label = "Retenir",
                Test = new EventTest { Stat = TestStat.Hp, Threshold = 0, BaseChance = 50, PercentPerUnit = 0 },
                Success = new EventEffects { Reputation = 5 },
                Failure = new EventEffects { HalfFalls = 2 },
            },
            new EventOption { Label = "S'échauffer", Success = new EventEffects { HalfFalls = 1, Quest = new QuestModifiers { StartGaugePercent = 50 } } },
        },
    };

    private static GameContent WorldContent() =>
        CampaignContent(WorldMap.FromJson(MapJson), new Dictionary<string, WorldEvent> { ["car"] = Car });

    /// <summary>Un hasard qui donne toujours cette graine au trajet.</summary>
    private sealed class Fixed(long seed) : Random
    {
        public override long NextInt64() => seed;
    }

    /// <summary>Une graine de trajet dont le premier test, à 50 %, réussit (ou échoue).</summary>
    private static long SeedWhere(bool success) =>
        Enumerable.Range(0, 1000).First(s => new DeterministicRandom((ulong)s).NextInt(0, 99) < 50 == success);

    /// <summary>Alice (qui choisit la quête et déploie son héros) et Bob (resté au QG) ; Alice lance : le trajet commence.</summary>
    private static (ServerSession Session, FakeClient Alice, FakeClient Bob) OnTheRoad(TempCampaign campaign, long seed = 0)
    {
        var session = campaign.Session(WorldContent(), new Fixed(seed));
        var alice = Joined(session, "Alice");
        var bob = Joined(session, "Bob");
        RecruitAndDeploy(alice);
        alice.Say(new StartCombatRequest());
        return (session, alice, bob);
    }

    private static TripInfo Trip(FakeClient client) => client.Last<LobbyState>().Campaign!.Trip!;

    private static EventOutcome? LastEvent(FakeClient client) => client.Last<LobbyState>().Campaign!.LastEvent;

    [Fact]
    public void LaunchingAQuest_StartsTheTrip_DecidedByTheOneWhoChoseIt()
    {
        using var campaign = new TempCampaign();
        var (session, alice, bob) = OnTheRoad(campaign);

        Assert.Null(session.Simulation);
        var trip = Trip(bob);
        Assert.Equal(("q", "street", "hq", 1), (trip.Quest, trip.Site, trip.Current, trip.Validator));
        Assert.Equal(new[] { "street", "poi" }, trip.Moves);
        Assert.Equal(new[] { "poi" }, CampaignStore.Load(campaign.File).KnownPois); // découvert au départ, et gardé
        bob.Say(new TravelTo("street"));
        Assert.Equal(new RequestRefused(RequestRefusal.NotTheValidator), bob.Received[^1]);
        alice.Say(new TravelTo("nowhere"));
        Assert.Equal(new RequestRefused(RequestRefusal.NotAMove), alice.Received[^1]);
    }

    [Fact]
    public void DuringTheTrip_TheHqWaits()
    {
        using var campaign = new TempCampaign();
        var (_, alice, bob) = OnTheRoad(campaign);

        alice.Say(new DeployHero(null));
        Assert.Equal(new RequestRefused(RequestRefusal.NotInLobby), alice.Received[^1]);
        bob.Say(new StartCombatRequest());
        Assert.Equal(new RequestRefused(RequestRefusal.NotInLobby), bob.Received[^1]);
        bob.Say(new RecruitHero(Rookie));
        Assert.Equal(new RequestRefused(RequestRefusal.NotInLobby), bob.Received[^1]);
    }

    [Fact]
    public void GoingStraight_LaunchesTheCombat_WithoutGravity()
    {
        using var campaign = new TempCampaign();
        var (session, alice, _) = OnTheRoad(campaign);

        alice.Say(new TravelTo("street"));

        Assert.NotNull(session.Simulation);
        var launch = alice.Last<CombatState>().Launch;
        Assert.Equal((0, (QuestModifiers?)QuestModifiers.None), (launch.GravityLevel, launch.Modifiers));
        Assert.Null(alice.Last<LobbyState>().Campaign!.Trip);
    }

    [Fact]
    public void ADetour_OpensItsEvent_AndBringsItsBonusAndGravityToTheCombat()
    {
        using var campaign = new TempCampaign();
        var (session, alice, _) = OnTheRoad(campaign);

        alice.Say(new TravelTo("poi"));
        var trip = Trip(alice);
        Assert.Equal(1, trip.Gravity);
        Assert.Empty(trip.Moves); // l'événement d'abord
        Assert.Equal("car", trip.Event!.Id);
        Assert.Equal(new[] { new OptionInfo("Retenir", QuestDifficulty.Hard), new OptionInfo("S'échauffer", null) }, trip.Event.Options);

        alice.Say(new ResolveEvent(1));
        trip = Trip(alice);
        Assert.Equal(("car", "S'échauffer"), (LastEvent(alice)!.Event, LastEvent(alice)!.Option));
        Assert.Equal(50, trip.Modifiers.StartGaugePercent);
        var saved = CampaignStore.Load(campaign.File);
        Assert.Equal(new[] { "poi" }, saved.ExhaustedPois);
        Assert.Equal(1, saved.Heroes.Single().HalfFalls);

        alice.Say(new TravelTo("street"));

        var launch = alice.Last<CombatState>().Launch;
        Assert.Equal((1, 50), (launch.GravityLevel, launch.Modifiers!.StartGaugePercent));
        Assert.Equal(new[] { "thug" }, launch.Gravity!.ExtraUnits); // celle de la quête, qui remplace la règle
        session.Tick(); // les renforts arrivent dès le premier pas
        Assert.Equal(2, session.Simulation!.Units.Count(u => u.Definition.Id == "thug")); // le voyou du scénario, et le renfort
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATest_IsRolledFromTheTripsSeed_ThatOnlyATriedTestMoves(bool success)
    {
        using var campaign = new TempCampaign();
        var (_, alice, _) = OnTheRoad(campaign, SeedWhere(success));
        alice.Say(new TravelTo("poi"));
        alice.Say(new ResolveEvent(7)); // refusé : le hasard n'a pas bougé

        alice.Say(new ResolveEvent(0));

        Assert.Equal((success, "Rempart"), (LastEvent(alice)!.Success!.Value, LastEvent(alice)!.TestedHero));
        var saved = CampaignStore.Load(campaign.File);
        Assert.Equal(success ? (5, 0) : (0, 2), (saved.Reputation, saved.Heroes.Single().HalfFalls));
    }

    [Fact]
    public void PassingBy_CostsNothing_ButExhaustsThePoint()
    {
        using var campaign = new TempCampaign();
        var (_, alice, _) = OnTheRoad(campaign);
        alice.Say(new TravelTo("poi"));
        alice.Say(new ResolveEvent(7));
        Assert.Equal(new RequestRefused(RequestRefusal.UnknownOption), alice.Received[^1]);

        alice.Say(new ResolveEvent(null));

        Assert.Equal((null, (string?)null), (Trip(alice).Event, LastEvent(alice)!.Option));
        Assert.Equal(new[] { "street" }, Trip(alice).Moves);
        Assert.Equal(new[] { "poi" }, CampaignStore.Load(campaign.File).ExhaustedPois);
        alice.Say(new ResolveEvent(null));
        Assert.Equal(new RequestRefused(RequestRefusal.UnknownOption), alice.Received[^1]);
    }

    [Fact]
    public void WhenTheValidatorLeaves_AnotherPlayerDecides_UntilTheyComeBack()
    {
        using var campaign = new TempCampaign();
        var (session, alice, bob) = OnTheRoad(campaign);

        session.Disconnect(alice);
        Assert.Equal(2, Trip(bob).Validator); // Alice garde sa place, Bob décide
        Assert.False(bob.Last<LobbyState>().Players.Single(p => p.Name == "Alice").Connected);
        var back = Joined(session, "Alice");
        Assert.Equal(1, Trip(back).Validator);
        session.Disconnect(back);

        bob.Say(new TravelTo("street"));

        // Le combat attend Alice, dont le héros est parti.
        Assert.NotNull(session.Simulation);
        Assert.True(session.IsPaused);
    }

    [Fact]
    public void WhenTheLastHeroDiesOnTheRoad_TheTripEnds()
    {
        using var campaign = new TempCampaign();
        var hero = RecruitAndDeploy(Joined(campaign.Session(WorldContent()), "Alice"));
        // Une demi-chute de plus le tue (mort à 6).
        var saved = CampaignStore.Load(campaign.File);
        CampaignStore.Save(saved with { Heroes = saved.Heroes.Select(h => h with { HalfFalls = 5 }).ToList() }, campaign.File);
        var session = campaign.Session(WorldContent());
        var alice = Joined(session, "Alice");
        alice.Say(new ChooseQuest("q"));
        alice.Say(new DeployHero(hero));
        alice.Say(new StartCombatRequest());
        alice.Say(new TravelTo("poi"));

        alice.Say(new ResolveEvent(1));

        var after = CampaignStore.Load(campaign.File);
        Assert.True(after.Heroes.Single().IsDead);
        Assert.Null(after.Trip);
        var lobby = alice.Last<LobbyState>();
        Assert.Null(lobby.Campaign!.Trip);
        Assert.Equal("S'échauffer", lobby.Campaign.LastEvent!.Option); // ce qui l'a tué reste affiché
        Assert.Null(lobby.Players.Single().Deployed);
        Assert.Null(session.Simulation);
    }

    [Fact]
    public void ATripInterrupted_ResumesWhereItWas_WithItsHeroesAndItsChooser()
    {
        using var campaign = new TempCampaign();
        var (_, alice, bob) = OnTheRoad(campaign);
        alice.Say(new TravelTo("poi"));

        // Le serveur redémarre : Bob revient le premier, puis Alice.
        var session = campaign.Session(WorldContent());
        bob = Joined(session, "Bob");
        alice = Joined(session, "Alice");

        var trip = Trip(alice);
        Assert.Equal(("poi", 1, "car"), (trip.Current, trip.Gravity, trip.Event!.Id));
        Assert.Equal(alice.Last<Welcome>().PlayerId, trip.Validator); // celle qui a choisi la quête décide toujours
        Assert.Equal("hero-1", alice.Last<LobbyState>().Players.Single(p => p.Name == "Alice").Deployed);
        Assert.Null(alice.Last<LobbyState>().Players.Single(p => p.Name == "Bob").Deployed);
        alice.Say(new ResolveEvent(null));
        alice.Say(new TravelTo("street"));
        Assert.Equal(1, alice.Last<CombatState>().Launch.GravityLevel);
        Assert.Null(CampaignStore.Load(campaign.File).Trip); // arrivé, le trajet n'est plus dans la sauvegarde
    }

    [Fact]
    public void AHeroWhosePlayerIsAway_IsPlayedByTheAi_OnArrival()
    {
        using var campaign = new TempCampaign();
        OnTheRoad(campaign);

        // Le serveur redémarre, et seul Bob revient : c'est lui qui décide, et le héros d'Alice part quand même.
        var session = campaign.Session(WorldContent());
        var bob = Joined(session, "Bob");
        bob.Say(new TravelTo("street"));

        var launch = bob.Last<CombatState>().Launch;
        Assert.Single(launch.Deployed!);
        Assert.Empty(launch.Heroes); // personne ne le joue : l'IA
        Assert.False(session.IsPaused);
    }

    [Fact]
    public void ATripTheDataNoLongerAllows_IsDropped_WhenTheServerStarts()
    {
        using var campaign = new TempCampaign();
        OnTheRoad(campaign);
        var saved = CampaignStore.Load(campaign.File);
        CampaignStore.Save(saved with { Trip = saved.Trip! with { Current = "gone" } }, campaign.File);

        var session = campaign.Session(WorldContent());

        Assert.Null(CampaignStore.Load(campaign.File).Trip);
        Assert.Null(Joined(session, "Alice").Last<LobbyState>().Campaign!.Trip);
    }
}
