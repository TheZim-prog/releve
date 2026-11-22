using Game.Core.Campaign;
using Game.Core.World;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests.World;

/// <summary>
/// La route vers une quête, sur une petite carte : le QG relié tout droit au lieu de mission « site », et par un point
/// d'intérêt « poi » (un crochet) ; un second point « far », plus loin, voisin de « poi » seulement.
/// </summary>
public class TravelTests
{
    private static readonly WorldMap Map = WorldMap.FromJson("""
        {
          "nodes": [
            { "id": "hq", "name": "QG", "x": 50, "y": 90, "kind": "hq" },
            { "id": "site", "name": "Site", "x": 50, "y": 10, "kind": "site" },
            { "id": "poi", "name": "Pont", "x": 20, "y": 50, "kind": "poi", "event": "car" },
            { "id": "far", "name": "Loin", "x": 5, "y": 50, "kind": "poi", "event": "car" }
          ],
          "paths": [ [ "hq", "site" ], [ "hq", "poi" ], [ "poi", "site" ], [ "poi", "far" ] ]
        }
        """);

    private static readonly WorldEvent Car = new()
    {
        Id = "car", Title = "Voiture", Text = "Une voiture bascule.",
        Options = new[]
        {
            new EventOption
            {
                Label = "Retenir",
                Test = new EventTest { Stat = TestStat.Hp, Threshold = 100, BaseChance = 50, PercentPerUnit = 1, ArchetypeBonus = new Dictionary<string, int> { ["colossus"] = 30 } },
                Success = new EventEffects { Reputation = 5, Quest = new QuestModifiers { StartGaugePercent = 50 } },
                Failure = new EventEffects { HalfFalls = 1 },
            },
            new EventOption { Label = "Se cotiser", Success = new EventEffects { Materials = new Dictionary<string, int> { ["materials"] = -2 }, HalfFalls = 1 } },
            new EventOption { Label = "Bricoler", RequiresArchetype = new[] { "tech" } },
        },
    };

    private static readonly Travel.Context Rules = new(Content.Rules, Content.WorldRules!, Content.Heroes!.Rules, Content.Reputation!.Scale);

    /// <summary>Alice part avec un colosse (hero-1) et un combattant (hero-2) ; le pot a 3 matériaux.</summary>
    private static (CampaignState State, IReadOnlyList<Traveller> Travellers) Party()
    {
        var state = CampaignState.New(Content.Reputation!.Scale).WithPlayer("uid-a", "Alice");
        foreach (var archetype in new[] { "colossus", "fighter" })
            state = state with { Heroes = state.Heroes.Append(new CampaignHero
            {
                Id = $"hero-{state.Heroes.Count + 1}", Name = archetype, Owner = "uid-a", Sheet = QuestContentTests.Recruit(archetype) with { Id = $"hero-{state.Heroes.Count + 1}" },
            }).ToList() };
        state = state with { Materials = new Dictionary<string, int> { ["materials"] = 3 } };
        return (state, state.Heroes.Select(h => new Traveller(h, Content.Heroes!.Unit(h.Sheet))).ToList());
    }

    private static (CampaignState State, Trip Trip) StartAndMove(CampaignState state, params string[] steps)
    {
        var (s, trip) = Travel.Start(state, Map, "q", "site");
        foreach (var step in steps)
            (s, trip) = Travel.Move(s, Map, trip, step)!.Value;
        return (s, trip);
    }

    [Fact]
    public void Leaving_DiscoversThePointsNextToTheHq()
    {
        var (state, trip) = Travel.Start(Party().State, Map, "q", "site");

        Assert.Equal(new[] { "poi" }, state.KnownPois);
        Assert.Equal(new[] { "site", "poi" }, Travel.Moves(Map, trip).Select(n => n.Id));
    }

    [Fact]
    public void GoingStraight_CostsNothing()
    {
        var (_, trip) = StartAndMove(Party().State, "site");

        Assert.True(trip.Arrived);
        Assert.Equal(0, trip.Gravity);
    }

    [Fact]
    public void ADetour_AddsGravity_AndWaitsForItsEvent()
    {
        var (state, trip) = StartAndMove(Party().State, "poi");

        Assert.Equal((1, "car"), (trip.Gravity, trip.PendingEvent));
        Assert.Empty(Travel.Moves(Map, trip)); // l'événement d'abord
        Assert.Contains("far", state.KnownPois); // ses voisins sont découverts
        Assert.Null(Travel.Move(state, Map, trip, "site"));
    }

    [Fact]
    public void PassingBy_ExhaustsThePoint_ThatStillCostsADetourNextTime()
    {
        var (state, trip) = StartAndMove(Party().State, "poi");
        (state, trip, var outcome) = Travel.Resolve(state, trip, Car, option: null, roll: 0, Party().Travellers, Rules)!.Value;
        Assert.Null(outcome.Option);
        Assert.Equal(new[] { "poi" }, state.ExhaustedPois);

        var (_, again) = StartAndMove(state, "poi");

        Assert.Equal((1, (string?)null), (again.Gravity, again.PendingEvent));
    }

    [Fact]
    public void ATest_IsTriedByTheBestHero_AndSucceedsUnderTheChance()
    {
        var (state, travellers) = Party();
        var (best, chance) = Travel.BestAt(Car.Options[0].Test!, travellers, Content.Rules, Content.WorldRules!);
        Assert.Equal("colossus", best.Hero.Name); // ses PV et son bonus
        var (moved, trip) = StartAndMove(state, "poi");

        var (won, wonTrip, success) = Travel.Resolve(moved, trip, Car, 0, roll: chance - 1, travellers, Rules)!.Value;
        var (lost, _, failure) = Travel.Resolve(moved, trip, Car, 0, roll: chance, travellers, Rules)!.Value;

        Assert.Equal((true, "colossus"), (success.Success!.Value, success.TestedHero));
        Assert.Equal(state.Reputation + 5, won.Reputation);
        Assert.Equal(50, wonTrip.Modifiers.StartGaugePercent);
        Assert.False(failure.Success);
        Assert.Equal(new[] { 1, 0 }, lost.Heroes.Select(h => h.HalfFalls)); // seul celui qui a tenté est blessé
    }

    [Fact]
    public void AChoiceWithoutTest_AppliesItsEffects_AndHurtsEveryDeployedHero()
    {
        var (state, travellers) = Party();
        var (moved, trip) = StartAndMove(state, "poi");

        var (after, _, _) = Travel.Resolve(moved, trip, Car, 1, roll: 0, travellers, Rules)!.Value;

        Assert.Equal(1, after.Materials["materials"]);
        Assert.Equal(new[] { 1, 1 }, after.Heroes.Select(h => h.HalfFalls));
    }

    [Fact]
    public void AnOptionReservedToAnArchetype_IsOfferedOnlyWithIt()
    {
        var (state, travellers) = Party();

        Assert.Equal(new[] { "Retenir", "Se cotiser" }, Travel.Options(Car, travellers, state).Select(o => o.Label));
    }

    [Fact]
    public void AnOptionThePotCannotPay_IsNotOffered()
    {
        var (state, travellers) = Party();

        var broke = state with { Materials = new Dictionary<string, int> { ["materials"] = 1 } };

        Assert.Equal(new[] { "Retenir" }, Travel.Options(Car, travellers, broke).Select(o => o.Label));
    }

    [Fact]
    public void AHeroKilledOnTheRoad_NoLongerTakesTests()
    {
        var (state, travellers) = Party();
        var colossusDead = state with { Heroes = state.Heroes.Select(h => h.Name == "colossus" ? h with { IsDead = true } : h).ToList() };
        var (moved, trip) = StartAndMove(colossusDead, "poi");

        var (_, _, outcome) = Travel.Resolve(moved, trip, Car, 0, roll: 0, travellers, Rules)!.Value;

        Assert.Equal("fighter", outcome.TestedHero);
    }

    [Fact]
    public void ADeadEnd_IsNeverOffered()
    {
        // « far » ne mène nulle part ailleurs qu'au point d'où l'on vient : le site n'y est plus joignable.
        var (_, trip) = StartAndMove(Party().State, "poi");
        trip = trip with { PendingEvent = null };

        Assert.Equal(new[] { "site" }, Travel.Moves(Map, trip).Select(n => n.Id));
    }

    [Fact]
    public void AMap_NeedsOneHq_KnownPaths_AndEveryPlaceReachable()
    {
        Assert.Throws<ArgumentException>(() => WorldMap.FromJson("""{ "nodes": [], "paths": [] }"""));
        Assert.Contains("cannot be reached", Assert.Throws<ArgumentException>(() => WorldMap.FromJson("""
            { "nodes": [ { "id": "hq", "name": "QG", "x": 0, "y": 0, "kind": "hq" }, { "id": "s", "name": "S", "x": 0, "y": 0, "kind": "site" } ],
              "paths": [] }
            """)).Message);
        Assert.Contains("has an event", Assert.Throws<ArgumentException>(() => WorldMap.FromJson("""
            { "nodes": [ { "id": "hq", "name": "QG", "x": 0, "y": 0, "kind": "hq" }, { "id": "p", "name": "P", "x": 0, "y": 0, "kind": "poi" } ],
              "paths": [ [ "hq", "p" ] ] }
            """)).Message);
    }
}
