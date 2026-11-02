using Game.Core.Combat;
using Game.Core.Grids;
using Xunit.Abstractions;

namespace Game.Core.Tests;

/// <summary>
/// Vérifie le contenu réel de data/ : il se charge, respecte les règles, et des combats d'essai (IA contre IA)
/// se jouent jusqu'au bout et se rejouent à l'identique. Les combats dépendent de l'équilibrage : s'ils
/// échouent après un changement de valeurs, c'est le signe d'un combat qui s'enlise, pas forcément d'un bug.
/// </summary>
public class ContentTests
{
    private static readonly CombatRules Rules = CombatRules.FromJson(Data("combat-rules.json"));
    private static readonly IReadOnlyDictionary<string, UnitDefinition> Heroes = Load("heroes.json");
    private static readonly IReadOnlyDictionary<string, UnitDefinition> Enemies = Load("enemies.json");
    private static readonly IReadOnlyDictionary<string, UnitDefinition> Civilians = Load("civilians.json");

    private static readonly Grid Arena = Grid.FromRows(
        "................",
        "................",
        "....##......##..",
        "................",
        "................",
        "......####......",
        "................",
        "................");

    private readonly ITestOutputHelper _output;

    public ContentTests(ITestOutputHelper output) => _output = output;

    private static string Data(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", path));

    private static IReadOnlyDictionary<string, UnitDefinition> Load(string file) =>
        UnitDefinition.ListFromJson(Data(Path.Combine("units", file))).ToDictionary(u => u.Id);

    [Fact]
    public void Heroes_AreTheSixArchetypes()
    {
        Assert.Equal(
            new[] { "colossus", "energetic", "fighter", "speedster", "tech", "telepath" },
            Heroes.Keys.Order());
        Assert.All(Heroes.Values, h =>
        {
            Assert.Null(h.Behavior);
            Assert.Equal(DefeatOutcome.Downed, h.OnDefeat);
            h.ValidateAgainst(Rules);
        });
        Assert.Equal(PassiveKind.IgnoresOpportunityAttacks, Heroes["speedster"].Passive);
        Assert.Contains(Heroes["speedster"].Skills, s => s.Id == "hyperspeed" && s.Target == SkillTargeting.Self);
        Assert.Contains(Heroes["colossus"].Skills, s => s.Id == "projection" && s.ForcedMove!.Type == ForcedMoveType.Push);
        Assert.Contains(Heroes["colossus"].Skills, s => s.Id == "dome" && s.CreatesDome);
        Assert.Contains(Heroes["tech"].Skills, s => s.Summon?.Id == "turret");
        Assert.Contains(Heroes["tech"].Skills, s => s.Summon is { Id: "nano-beacon", MaxActivations: 3 });
        Assert.Equal(PassiveKind.WeakPoint, Heroes["fighter"].Passive);
        Assert.Contains(Heroes["fighter"].Skills, s => s.Id == "execution" && s.ExecuteAtOrBelowPercent > 0);
        Assert.True(Enemies["kingpin"].IsBoss);
        Assert.Contains(Heroes["speedster"].Skills, s => s.SelfMove == SelfMoveType.Pierce);
        Assert.Contains(Heroes["speedster"].Skills, s => s.IsRescue);
        Assert.Equal(PassiveKind.Grip, Heroes["telepath"].Passive);
        Assert.All(new[] { "paralysis", "confusion", "domination" },
            id => Assert.True(Heroes["telepath"].Skills.Single(s => s.Id == id).IsMental));
        Assert.Contains(Heroes["colossus"].Skills, s => s.SelfMove == SelfMoveType.Leap);
        Assert.Contains(Enemies["kingpin"].Skills, s => s.SelfMove == SelfMoveType.Rush);
        Assert.Contains(Heroes["telepath"].Skills, s => s.Id == "telekinesis" && s.ForcedMove!.Type == ForcedMoveType.Place);
        Assert.Contains(Heroes["energetic"].Skills, s => s.Id == "energy-form" && s.Target == SkillTargeting.Self);
        var order = Heroes["tech"].Skills.Single(s => s.Id == "tactical-order");
        Assert.Equal(SkillTargeting.Ally, order.Target);
        Assert.True(order.GaugeGainPercent > 0);
    }

    [Fact]
    public void Enemies_AreThePrologueOnes()
    {
        Assert.Equal(new[] { "brawler", "brute", "gunner", "kingpin" }, Enemies.Keys.Order());
        Assert.All(Enemies.Values, e =>
        {
            Assert.Equal(e.Id == "gunner" ? Behaviors.Gunner : Behaviors.Basic, e.Behavior);
            Assert.Equal(DefeatOutcome.Removed, e.OnDefeat);
            Assert.Equal(e.Id == "kingpin" ? 30 : null, e.FleeAtHpPercent);
            e.ValidateAgainst(Rules);
        });
    }

    [Fact]
    public void TheStreetScenario_LoadsAndPlays()
    {
        var catalog = Heroes.Concat(Enemies).Concat(Civilians).ToDictionary();
        var scenario = ScenarioDefinition.FromJson(Data(Path.Combine("scenarios", "street.json")));
        var setup = scenario.ToSetup(Rules, catalog);

        // Les héros attendent (personne ne les commande) : l'IA ennemie et les civils jouent.
        var sim = new CombatSimulation(setup);
        for (var i = 0; i < Rules.TicksPerSecond * 30; i++)
            sim.Tick();

        Assert.Contains(sim.EventLog, e => e is SkillUsed);
        Assert.Contains(sim.EventLog, e => e is UnitEscaped);
    }

    [Fact]
    public void Civilians_AreNeutralAndFlee()
    {
        var civilian = Civilians["civilian"];
        Assert.True(civilian.IsCivilian);
        Assert.Equal(Behaviors.Flee, civilian.Behavior);
        civilian.ValidateAgainst(Rules);
    }

    public static TheoryData<string, string[], (string Id, int X, int Y)[]> Skirmishes => new()
    {
        {
            "street",
            new[] { "colossus", "fighter", "speedster", "energetic", "tech" },
            new[] { ("brawler", 14, 1), ("brawler", 14, 3), ("brawler", 14, 5), ("gunner", 15, 0), ("gunner", 15, 7), ("brute", 13, 4) }
        },
        {
            "kingpin",
            new[] { "colossus", "fighter", "telepath", "energetic", "speedster" },
            new[] { ("kingpin", 14, 4), ("brawler", 14, 2), ("brawler", 13, 6), ("gunner", 15, 0) }
        },
    };

    [Theory]
    [MemberData(nameof(Skirmishes))]
    public void Skirmish_WithTheRealContent_EndsAndReplays(string name, string[] heroIds, (string Id, int X, int Y)[] enemies)
    {
        // Les héros sont confiés à l'IA de base pour que le combat se joue seul.
        var units = new List<UnitSetup>();
        var id = 1;
        for (var i = 0; i < heroIds.Length; i++)
            units.Add((Heroes[heroIds[i]] with { Behavior = Behaviors.Basic }).ToSetup(new UnitId(id++), team: 0, new Cell(0, 2 + i)));
        foreach (var (enemy, x, y) in enemies)
            units.Add(Enemies[enemy].ToSetup(new UnitId(id++), team: 1, new Cell(x, y)));
        // Des civils pris entre les deux camps : ils fuient, et les zones peuvent les blesser.
        foreach (var (x, y) in new[] { (7, 3), (8, 4), (9, 1) })
            units.Add(Civilians["civilian"].ToSetup(new UnitId(id++), team: 2, new Cell(x, y)));

        var setup = new CombatSetup(Rules, Arena, units, Seed: 2026);
        var sim = new CombatSimulation(setup);
        var maxTicks = Rules.TicksPerSecond * 60 * 10;
        while (sim.CurrentTick < maxTicks && StandingTeams(sim).Count > 1)
            sim.Tick();

        var winners = StandingTeams(sim);
        _output.WriteLine($"[{name}] durée : {sim.CurrentTick / (double)Rules.TicksPerSecond:0.0} s, équipe(s) debout : {string.Join(",", winners)}");
        foreach (var unit in sim.Units)
            _output.WriteLine($"  {unit.Definition.Name,-12} équipe {unit.Team}  {unit.Status,-8} {unit.Hp,4}/{unit.MaxHp} PV{(unit.IsFleeing ? " en fuite" : "")}{(unit.Escaped ? ", enfui" : "")}");

        Assert.True(winners.Count <= 1, $"Combat '{name}' still running after {maxTicks} ticks.");
        var replayed = CombatSimulation.Replay(setup, sim.CommandLog, sim.CurrentTick);
        Assert.Equal(sim.EventLog, replayed.EventLog);
    }

    private static List<int> StandingTeams(CombatSimulation sim) => sim.TeamsStillFighting().Order().ToList();
}
