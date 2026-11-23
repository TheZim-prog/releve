using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Network;
using Game.Core.Progression;
using Game.Core.Reputation;
using Game.Core.Scenarios;
using Game.Core.World;
using Game.Core.Tests.Progression;
using static Game.Core.Tests.Combat.CombatTestKit;
using static Game.Core.Tests.Network.ServerSessionTests;

namespace Game.Core.Tests.Network;

/// <summary>Un serveur qui tient une campagne : son QG (académie, panneau de quêtes, héros déployés) et ce qu'il garde.</summary>
public class CampaignSessionTests
{
    private static readonly ReputationRules ReputationRules = new()
    {
        SceneryDestroyed = -2, CivilianHurtByHeroes = -3, CivilianLostToHeroes = -10, CivilianLostToEnemies = -5,
        CivilianSaved = 2, SecondaryObjectiveCompleted = 5, Scale = Campaign.CampaignStateTests.Scale,
    };

    /// <summary>
    /// La quête de test : trois cases pour les héros des joueurs à gauche, un civil qui s'enfuit (sauvé : +2) et un voyou
    /// de 60 PV.
    /// </summary>
    private const string QuestScenarioJson = """
        {
          "id": "street", "name": "Rue", "seed": 5,
          "map": [ "......", "......", "......" ],
          "heroSlots": [ { "x": 0, "y": 0 }, { "x": 0, "y": 1 }, { "x": 0, "y": 2 } ],
          "units": [
            { "unit": "civilian", "team": 2, "x": 3, "y": 0 },
            { "unit": "thug", "team": 1, "x": 5, "y": 1 }
          ]
        }
        """;

    /// <summary>
    /// Le contenu d'une campagne de test : l'archétype « hero » et son arbre (<see cref="ProgressionTestKit"/>), le combat
    /// de test à héros fixes, et la quête « q » (scénario « street »). Avec une carte (<paramref name="world"/>), la quête
    /// se joue au lieu « street », avec sa propre gravité, et les règles de la carte sont celles du jeu.
    /// </summary>
    internal static GameContent CampaignContent(WorldMap? world = null, IReadOnlyDictionary<string, WorldEvent>? events = null)
    {
        var civilian = Unit(0, 2, 0, 0).Definition with
        {
            Id = "civilian", IsCivilian = true, Behavior = Behaviors.Flee, Skills = Array.Empty<SkillDefinition>(), BasicAttack = null,
            OpportunityAttacksPerCycle = 0,
        };
        // Le catalogue des héros et le contenu partagent les mêmes unités.
        var units = new Dictionary<string, UnitDefinition>(Content.Units)
        {
            // Un héros tombe à terre à 0 PV (il n'est pas retiré comme un ennemi) : c'est une chute.
            ["civilian"] = civilian, ["hero"] = ProgressionTestKit.Units["hero"] with { OnDefeat = DefeatOutcome.Downed },
        };
        var catalog = new HeroCatalog(ProgressionTestKit.Rules, units, ProgressionTestKit.Skills, new[] { ProgressionTestKit.Tree },
            Array.Empty<HeroSheet>());
        return new GameContent
        {
            Rules = Content.Rules,
            Units = units,
            Heroes = catalog,
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["test"] = Content.Scenarios["test"], ["street"] = ScenarioDefinition.FromJson(QuestScenarioJson),
            },
            Quests = new Dictionary<string, QuestDefinition>
            {
                ["q"] = new()
                {
                    Id = "q", Name = "Quête", Scenario = "street", Difficulty = QuestDifficulty.Easy, Xp = 20, DurationHours = 24,
                    Site = world is null ? null : "street",
                    // La sienne, pas celle des règles du jeu (dont les renforts n'existent pas ici) : un voyou par crochet.
                    Gravity = world is null ? null : new GravityStep { ExtraUnits = new[] { "thug" } },
                },
            },
            Reputation = ReputationRules,
            World = world,
            Events = events ?? new Dictionary<string, WorldEvent>(),
            WorldRules = world is null ? null : ContentTestKit.Content.WorldRules,
            Version = "v1",
        };
    }

    internal static readonly Recruit Rookie = new("Rempart", "hero", new[] { "a1", "a2", "a3" });

    /// <summary>Une campagne dans un fichier temporaire, effacé à la fin.</summary>
    internal sealed class TempCampaign : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"campaign-{Guid.NewGuid():N}");

        private CampaignKeeper? _keeper;

        public string File => Path.Combine(Folder, "campaign.json");

        /// <summary>Un serveur sur cette campagne ; celui d'avant la lâche d'abord (comme un serveur qui redémarre).</summary>
        public ServerSession Session(GameContent? content = null, Random? random = null)
        {
            _keeper?.Dispose();
            _keeper = CampaignKeeper.Open(File, ReputationRules.Scale);
            return new(content ?? CampaignContent(), "test", _keeper, random);
        }

        public void Dispose()
        {
            _keeper?.Dispose();
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, recursive: true);
        }
    }

    internal static FakeClient Joined(ServerSession session, string name)
    {
        var client = new FakeClient(session);
        client.Join(name);
        return client;
    }

    /// <summary>Le joueur recrute un héros, et le déploie pour la quête « q ».</summary>
    internal static string RecruitAndDeploy(FakeClient client)
    {
        client.Say(new RecruitHero(Rookie));
        var hero = client.Last<LobbyState>().Campaign!.Heroes.Last(h => h.Mine).Id;
        client.Say(new ChooseQuest("q"));
        client.Say(new DeployHero(hero));
        return hero;
    }

    private static (int, string, int?) Summary(FakeClient client) =>
        client.Last<LobbyState>().Campaign is { } c ? (c.Reputation, c.Tier, c.LastQuest?.Reputation) : throw new InvalidOperationException("No campaign.");

    internal static void PlayToTheEnd(ServerSession session)
    {
        for (var i = 0; i < 4000 && session.Simulation is not null; i++)
            session.Tick();
    }

    [Fact]
    public void AtTheAcademy_APlayerRecruitsTheirHeroes_SeenByEveryone()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        var bob = new FakeClient(session);
        alice.Join("Alice");
        bob.Join("Bob");

        alice.Say(new RecruitHero(Rookie with { Learned = new[] { "a1", "b1", "a3" } }));
        Assert.Equal(new RecruitRefused(RecruitRefusal.InvalidSkills), alice.Received[^1]);
        alice.Say(new RecruitHero(Rookie));

        var seen = Assert.Single(alice.Last<LobbyState>().Campaign!.Heroes);
        Assert.Equal(new HeroInfo("hero-1", "Rempart", "hero", 1, "Alice", Mine: true, IsDead: false, Xp: 0, NextLevelXp: 100,
                HalfFallsToDeath: 6),
            seen with { Sheet = null });
        Assert.Equal(new[] { "a1", "a2", "a3" }, seen.Sheet!.Learned); // sa fiche complète, pour l'académie
        var others = Assert.Single(bob.Last<LobbyState>().Campaign!.Heroes);
        Assert.False(others.Mine);
        Assert.Null(others.Sheet); // pas celle des héros des autres
        Assert.Equal("Rempart", CampaignStore.Load(campaign.File).Heroes.Single().Name);
    }

    [Fact]
    public void AtTheAcademy_APlayerEquipsTheirHero_AndOnlyTheirs()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = Joined(session, "Alice");
        var bob = Joined(session, "Bob");
        alice.Say(new RecruitHero(Rookie));

        alice.Say(new EquipHero("hero-1", new[] { "a1v", "a3" }, null));
        bob.Say(new EquipHero("hero-1", new[] { "a1" }, null));
        alice.Say(new SpendStatPoint("hero-1", StatKind.Hp));

        Assert.Equal(new[] { "a1v", "a3" }, CampaignStore.Load(campaign.File).Heroes.Single().Sheet.Equipped);
        Assert.Equal(new TrainingRefused(TrainingRefusal.NotYourHero), bob.Received[^1]);
        Assert.Equal(new TrainingRefused(TrainingRefusal.NoPointToSpend), alice.Received[^1]);
    }

    [Fact]
    public void WithoutACampaign_TheHqIsClosed()
    {
        var session = new ServerSession(Content, "test");
        var alice = new FakeClient(session);
        alice.Join("Alice");

        alice.Say(new RecruitHero(Rookie));
        Assert.Equal(new RequestRefused(RequestRefusal.NoCampaign), alice.Received[^1]);
        alice.Say(new ChooseQuest("q"));
        Assert.Equal(new RequestRefused(RequestRefusal.NoCampaign), alice.Received[^1]);
    }

    [Fact]
    public void TheBoard_ListsTheQuests_AndAnyPlayerChoosesOne()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        var bob = new FakeClient(session);
        alice.Join("Alice");
        bob.Join("Bob");

        Assert.Equal(new QuestInfo("q", "Quête", "", QuestDifficulty.Easy, 20, new Dictionary<string, int>(), 3, DurationHours: 24) with
        {
            Materials = alice.Last<LobbyState>().Campaign!.Board.Single().Materials,
        }, alice.Last<LobbyState>().Campaign!.Board.Single());
        bob.Say(new ChooseQuest("nope"));
        Assert.Equal(new RequestRefused(RequestRefusal.UnknownQuest), bob.Received[^1]);
        bob.Say(new ChooseQuest("q"));

        Assert.Equal("q", alice.Last<LobbyState>().Campaign!.ChosenQuest);
        Assert.Equal("street", alice.Last<LobbyState>().ScenarioId);
    }

    [Fact]
    public void APlayer_DeploysOnlyTheirOwnLivingHero()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        var bob = new FakeClient(session);
        alice.Join("Alice");
        bob.Join("Bob");
        var hero = RecruitAndDeploy(alice);

        bob.Say(new DeployHero(hero));
        Assert.Equal(new RequestRefused(RequestRefusal.NotYourHero), bob.Received[^1]);
        Assert.Equal(hero, alice.Last<LobbyState>().Players.Single(p => p.Name == "Alice").Deployed);
        alice.Say(new DeployHero(null));
        Assert.Null(alice.Last<LobbyState>().Players.Single(p => p.Name == "Alice").Deployed);
    }

    [Fact]
    public void AQuest_NeedsAChoiceAndADeployedHero_BeforeLaunching()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        alice.Join("Alice");

        alice.Say(new StartCombatRequest());
        Assert.Equal(new RequestRefused(RequestRefusal.NoQuestChosen), alice.Received[^1]);
        alice.Say(new ChooseQuest("q"));
        alice.Say(new StartCombatRequest());
        Assert.Equal(new RequestRefused(RequestRefusal.NoHeroDeployed), alice.Received[^1]);
        Assert.Null(session.Simulation);
    }

    [Fact]
    public void ALaunchedQuest_PlacesTheDeployedHeroes_WhileTheOthersStayAtTheHq()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        var bob = new FakeClient(session);
        alice.Join("Alice");
        bob.Join("Bob"); // Bob reste au QG
        RecruitAndDeploy(alice);

        bob.Say(new StartCombatRequest());

        var state = bob.Last<CombatState>();
        Assert.Equal("street", state.Launch.ScenarioId);
        var mirror = new CombatMirror(CampaignContent(), state);
        var rookie = mirror.Simulation.Get(3); // après le civil et le voyou du scénario
        Assert.Equal((new Cell(0, 0), "hero"), (rookie.Position, rookie.Definition.Id));
        Assert.Equal(1, mirror.PlayerOf(rookie.Id));
        Assert.Equal(new UnitId(3), alice.Last<LobbyState>().Players.Single(p => p.Name == "Alice").Hero);
    }

    [Fact]
    public void AHeroTheDataNoLongerAllows_IsRefusedAtLaunch_WithoutChangingAnything()
    {
        using var campaign = new TempCampaign();
        var hero = RecruitAndDeploy(Joined(campaign.Session(), "Alice"));
        // Les données ont changé depuis son recrutement : sa fiche apprend une case qui n'existe plus dans l'arbre.
        var saved = CampaignStore.Load(campaign.File);
        CampaignStore.Save(saved with
        {
            Heroes = saved.Heroes.Select(h => h with { Sheet = h.Sheet with { Learned = new[] { "a1", "a2", "gone" } } }).ToList(),
        }, campaign.File);
        var session = campaign.Session();
        var alice = Joined(session, "Alice");
        alice.Say(new ChooseQuest("q"));
        alice.Say(new DeployHero(hero));

        alice.Say(new StartCombatRequest());

        Assert.Equal(new RequestRefused(RequestRefusal.HeroUnavailable), alice.Received[^1]);
        Assert.Null(session.Simulation);
        Assert.Null(alice.Last<LobbyState>().Players.Single().Hero);
    }

    [Fact]
    public void APlayerWhoStayedAtTheHq_DoesNotPauseTheQuestWhenTheyLeave()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        var bob = new FakeClient(session);
        alice.Join("Alice");
        bob.Join("Bob"); // reste au QG
        RecruitAndDeploy(alice);
        alice.Say(new StartCombatRequest());

        session.Disconnect(bob);

        Assert.False(session.IsPaused);
    }

    [Fact]
    public void AHeroWhoFallsOnceTooOften_DiesAtTheEndOfTheQuest()
    {
        using var campaign = new TempCampaign();
        var hero = RecruitAndDeploy(Joined(campaign.Session(), "Alice"));
        // Deux chutes déjà : la prochaine le tue.
        var saved = CampaignStore.Load(campaign.File);
        CampaignStore.Save(saved with { Heroes = saved.Heroes.Select(h => h with { HalfFalls = 4 }).ToList() }, campaign.File);
        var session = campaign.Session();
        var alice = Joined(session, "Alice");
        alice.Say(new ChooseQuest("q"));
        alice.Say(new DeployHero(hero));

        // Alice ne donne aucun ordre : son héros finit à terre (défaite).
        alice.Say(new StartCombatRequest());
        PlayToTheEnd(session);

        Assert.False(alice.Last<CombatOver>().Victory);
        var dead = Assert.Single(CampaignStore.Load(campaign.File).Heroes);
        Assert.True(dead.IsDead);
        Assert.Equal(new[] { "Rempart" }, alice.Last<LobbyState>().Campaign!.LastQuest!.Deaths);
        Assert.Null(alice.Last<LobbyState>().Players.Single().Deployed); // un mort ne part plus en quête
    }

    [Fact]
    public void WithACampaign_PlayersAreEnrolled_AndEachQuestsReportIsSaved()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        alice.Join("Alice");
        Assert.Equal((0, "Neutres", (int?)null), Summary(alice));
        RecruitAndDeploy(alice);

        alice.Say(new StartCombatRequest());
        PlayToTheEnd(session);

        // Alice ne donne aucun ordre : l'issue importe peu, le civil s'est enfui (sauvé : +2) de toute façon.
        Assert.Single(alice.Received.OfType<CombatOver>());
        Assert.Equal((2, "Neutres", (int?)2), Summary(alice));
        var saved = CampaignStore.Load(campaign.File);
        Assert.Equal(2, saved.Reputation);
        // La quête a duré un jour pour Alice, et a quitté le panneau.
        Assert.Equal(new CampaignPlayer(UidOf("Alice"), "Alice", 24), Assert.Single(saved.Players));
        Assert.Equal(new[] { "q" }, saved.DoneQuests);
        Assert.Empty(alice.Last<LobbyState>().Campaign!.Board);
        Assert.Null(alice.Last<LobbyState>().Campaign!.ChosenQuest);
        Assert.Equal(24, alice.Last<LobbyState>().Campaign!.Hour);
        alice.Say(new ChooseQuest("q"));
        Assert.Equal(new RequestRefused(RequestRefusal.UnknownQuest), alice.Received[^1]);
        // Le héros a reçu l'XP de la quête (20, ou 10 si elle est perdue), et le pot ses matériaux si elle est gagnée.
        var over = alice.Last<CombatOver>();
        var hero = Assert.Single(saved.Heroes);
        Assert.Equal(over.Victory ? 20 : 10, hero.Xp);
        var last = alice.Last<LobbyState>().Campaign!.LastQuest!;
        Assert.Equal(("Quête", over.Victory, 2, hero.Xp), (last.Quest, last.Victory, last.Reputation, last.Xp));
        Assert.Equal(hero.Xp, alice.Last<LobbyState>().Campaign!.Heroes.Single().Xp);
        // De retour au QG, le héros reste déployé pour la quête suivante.
        Assert.NotNull(alice.Last<LobbyState>().Players.Single().Deployed);
        Assert.Null(alice.Last<LobbyState>().Players.Single().Hero);
    }

    [Fact]
    public void AFailingCampaignSave_RefusesTheNewcomer_AndStillEndsTheQuest()
    {
        using var campaign = new TempCampaign();
        var session = campaign.Session();
        var alice = new FakeClient(session);
        alice.Join("Alice");
        RecruitAndDeploy(alice);
        // La sauvegarde devient impossible : un dossier prend la place du fichier.
        System.IO.File.Delete(campaign.File);
        Directory.CreateDirectory(campaign.File);

        var bob = new FakeClient(session);
        Assert.ThrowsAny<Exception>(() => bob.Join("Bob"));
        Assert.Equal(new JoinRefused(JoinRefusal.CampaignUnavailable), Assert.Single(bob.Received));

        // Le bilan de la quête ne peut pas être sauvegardé : l'erreur remonte, mais la quête se termine.
        alice.Say(new StartCombatRequest());
        var failures = 0;
        for (var i = 0; i < 4000 && session.Simulation is not null; i++)
        {
            try
            {
                session.Tick();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failures++;
            }
        }
        Assert.Equal(1, failures);
        Assert.Null(session.Simulation);
        Assert.Single(alice.Received.OfType<CombatOver>());
    }
}
