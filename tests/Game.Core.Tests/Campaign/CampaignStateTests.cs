using Game.Core.Campaign;
using Game.Core.Progression;
using Game.Core.Reputation;

namespace Game.Core.Tests.Campaign;

public class CampaignStateTests
{
    internal static readonly ReputationScale Scale = new()
    {
        Min = -100, Max = 100, Start = 0,
        Tiers = new[] { new ReputationTier("hated", "Haïs", -100), new ReputationTier("neutral", "Neutres", -15), new ReputationTier("liked", "Appréciés", 15) },
    };

    private static ReputationReport Report(int total) => new(0, 0, 0, 0, 0, 0, total);

    [Fact]
    public void ANewCampaign_StartsEmpty_AtTheScalesStartingReputation()
    {
        var state = CampaignState.New(Scale with { Start = 10 });

        Assert.Empty(state.Players);
        Assert.Empty(state.Heroes);
        Assert.Equal(10, state.Reputation);
        Assert.Equal(0, state.Chapter);
    }

    [Fact]
    public void APlayer_IsAddedOnce_AndRenamedWhenTheirNameChanges()
    {
        var state = CampaignState.New(Scale).WithPlayer("uid-a", "Alice").WithPlayer("uid-b", "Bob");

        Assert.Same(state, state.WithPlayer("uid-a", "Alice")); // rien ne change : le même état
        var renamed = state.WithPlayer("uid-a", "Alicia");

        Assert.Equal(new[] { new CampaignPlayer("uid-a", "Alicia", 0), new CampaignPlayer("uid-b", "Bob", 0) }, renamed.Players);
    }

    [Fact]
    public void AnInvalidPlayerId_IsRefused() =>
        Assert.Throws<ArgumentException>(() => CampaignState.New(Scale).WithPlayer("a/b", "Alice"));

    [Fact]
    public void AMissionReport_MovesTheReputation_WithinTheScale()
    {
        var state = CampaignState.New(Scale);

        Assert.Equal(6, state.WithMissionReport(Report(6), Scale).Reputation);
        Assert.Equal(100, state.WithMissionReport(Report(250), Scale).Reputation);
        Assert.Equal(-100, state.WithMissionReport(Report(-250), Scale).Reputation);
    }

    [Fact]
    public void ACampaign_SurvivesASaveAndALoad()
    {
        var hero = new CampaignHero
        {
            Id = "hero-1", Name = "Rempart", Owner = "uid-a", Xp = 40, Falls = 1,
            Sheet = new HeroSheet { Id = "hero-1", Archetype = "colossus", Level = 3, Learned = new[] { "dome" }, Equipped = new[] { "dome" } },
        };
        var state = CampaignState.New(Scale).WithPlayer("uid-a", "Alice") with
        {
            Heroes = new[] { hero }, Materials = new Dictionary<string, int> { ["alloy"] = 3 }, Reputation = -20, Chapter = 1,
        };
        var path = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}.json");
        try
        {
            CampaignStore.Save(state, path);
            var loaded = CampaignStore.Load(path);

            Assert.Equal(state.Players, loaded.Players);
            Assert.Equal(hero with { Sheet = loaded.Heroes[0].Sheet }, loaded.Heroes[0]);
            Assert.Equal(hero.Sheet.Learned, loaded.Heroes[0].Sheet.Learned);
            Assert.Equal(3, loaded.Materials["alloy"]);
            Assert.Equal((-20, 1), (loaded.Reputation, loaded.Chapter));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASaveOfAnotherFormat_IsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "format": 99 }""");
            Assert.Throws<InvalidDataException>(() => CampaignStore.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ADamagedSave_IsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "format": 1, "players": null }""");
            Assert.Throws<InvalidDataException>(() => CampaignStore.Load(path));
            File.WriteAllText(path, """{ "format": 1, "heroes": [ { "id": "hero-1", "name": "X", "owner": "uid-a", "sheet": null } ] }""");
            Assert.Throws<InvalidDataException>(() => CampaignStore.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AReputationOutsideTheScale_IsBroughtBackInto_WhenOpened()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}.json");
        try
        {
            CampaignStore.Save(CampaignState.New(Scale) with { Reputation = 80 }, path);

            using var keeper = CampaignKeeper.Open(path, Scale with { Max = 50 });
            Assert.Equal(50, keeper.State.Reputation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AKeeper_CreatesTheCampaign_ThenSavesEachChange()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}", "campaign.json");
        try
        {
            using (var keeper = CampaignKeeper.Open(path, Scale))
            {
                Assert.True(File.Exists(path));
                keeper.Update(s => s.WithPlayer("uid-a", "Alice"));
            }

            using var reopened = CampaignKeeper.Open(path, Scale);
            Assert.Equal("Alice", reopened.State.Players.Single().Name);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ACampaign_IsHeldByOneGameAtATime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}", "campaign.json");
        try
        {
            using (CampaignKeeper.Open(path, Scale))
            {
                // Une autre fenêtre du jeu, ou un serveur : la sauvegarde est déjà tenue.
                Assert.Throws<CampaignInUseException>(() => CampaignKeeper.Open(path, Scale));
            }

            // Lâchée, elle se rouvre ; et le verrou ne traîne pas sur le disque.
            Assert.False(File.Exists(path + ".lock"));
            CampaignKeeper.Open(path, Scale).Dispose();
            Assert.False(File.Exists(path + ".lock"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
