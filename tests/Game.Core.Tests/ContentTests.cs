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
    private static readonly string[] Archetypes = { "colossus", "energetic", "fighter", "speedster", "tech", "telepath" };

    private static readonly GameContent Content = GameContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly CombatRules Rules = Content.Rules;
    private static readonly IReadOnlyDictionary<string, SkillDefinition> Skills = Content.Skills;
    private static readonly IReadOnlyDictionary<string, UnitDefinition> Units = Content.Units;

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

    [Fact]
    public void TheArchetypes_AreTheSixOfTheDesign_WithOnlyTheirBasicAttack()
    {
        Assert.Equal(Archetypes, Content.Heroes!.Trees.Keys.Order());
        Assert.All(Archetypes, id =>
        {
            var archetype = Units[id];
            Assert.Null(archetype.Behavior);
            Assert.Null(archetype.Passive);
            Assert.Equal(DefeatOutcome.Downed, archetype.OnDefeat);
            Assert.Equal(archetype.BasicAttack, archetype.Skills.Single().Id);
        });
    }

    [Fact]
    public void TheSkillLibrary_HoldsThePrologueKits()
    {
        Assert.Equal(SkillTargeting.Self, Skills["hyperspeed"].Target);
        Assert.Equal(ForcedMoveType.Push, Skills["projection"].ForcedMove!.Type);
        Assert.True(Skills["dome"].CreatesDome);
        Assert.Equal("turret", Skills["turret"].Summon!.Id);
        Assert.True(Skills["nano-heal"].Summon is { Id: "nano-beacon", MaxActivations: 3 });
        Assert.True(Skills["execution"].ExecuteAtOrBelowPercent > 0);
        Assert.Equal(SelfMoveType.Pierce, Skills["breakthrough"].SelfMove);
        Assert.True(Skills["flash-rescue"].IsRescue);
        Assert.All(new[] { "paralysis", "confusion", "domination" }, id => Assert.True(Skills[id].IsMental));
        Assert.Equal(SelfMoveType.Leap, Skills["leap"].SelfMove);
        Assert.Equal(SelfMoveType.Charge, Skills["charge"].SelfMove);
        Assert.Equal(ForcedMoveType.Place, Skills["telekinesis"].ForcedMove!.Type);
        Assert.Equal(SkillTargeting.Self, Skills["energy-form"].Target);
        Assert.Equal(SkillTargeting.Ally, Skills["tactical-order"].Target);
        Assert.True(Skills["tactical-order"].GaugeGainPercent > 0);
    }

    [Fact]
    public void EveryTree_HasThreeBranches_EighteenNodes_AndNineVariants()
    {
        Assert.All(Content.Heroes!.Trees.Values, tree =>
        {
            var nodes = tree.Nodes();
            Assert.Equal(3, tree.Branches.Count);
            Assert.Equal((12, 6), (nodes.Count(n => !n.IsPassive), nodes.Count(n => n.IsPassive)));
            Assert.Equal(9, nodes.Count(n => n.Variant is not null));
        });
    }

    [Fact]
    public void ThePrologueHeroes_KeepTheirKits()
    {
        // L'attaque de base, puis les quatre compétences du kit, dans l'ordre des boutons ; et le passif.
        (string Skills, PassiveKind? Passive) Kit(string hero)
        {
            var definition = Content.Heroes!.Unit(hero).Definition;
            return (string.Join(" ", definition.Skills.Select(s => s.Id)), definition.Passive);
        }

        Assert.Equal(("punch dome leap projection shockwave", PassiveKind.Vigilance), Kit("prologue-colossus"));
        Assert.Equal(("flurry whirlwind breakthrough flash-rescue hyperspeed", PassiveKind.IgnoresOpportunityAttacks), Kit("prologue-speedster"));
        Assert.Equal(("combo precise-strike throwing-blades stunning-blow execution", PassiveKind.WeakPoint), Kit("prologue-fighter"));
        Assert.Equal(("bolt beam blast nova energy-form", null), Kit("prologue-energetic"));
        Assert.Equal(("shot turret nano-heal tactical-order emp", null), Kit("prologue-tech"));
        Assert.Equal(("psi-dart paralysis confusion domination telekinesis", PassiveKind.Grip), Kit("prologue-telepath"));
    }

    [Fact]
    public void Enemies_AreThePrologueOnes()
    {
        var enemies = new[] { "brawler", "brute", "gunner", "kingpin" };

        // Les unités mobiles commandées par l'IA, sauf les gadgets que posent les compétences (le drone).
        var summoned = Skills.Values.Select(s => s.Summon?.Id).OfType<string>().ToHashSet();
        Assert.Equal(enemies, Units.Values
            .Where(u => u.Behavior is Behaviors.Basic or Behaviors.Gunner && u.Mobility > 0 && !summoned.Contains(u.Id))
            .Select(u => u.Id).Order());
        Assert.All(enemies, id =>
        {
            var enemy = Units[id];
            Assert.Equal(id == "gunner" ? Behaviors.Gunner : Behaviors.Basic, enemy.Behavior);
            Assert.Equal(DefeatOutcome.Removed, enemy.OnDefeat);
            Assert.Equal(id == "kingpin" ? 30 : null, enemy.FleeAtHpPercent);
        });
        Assert.True(Units["kingpin"].IsBoss);
    }

    [Fact]
    public void Civilians_AreNeutralAndFlee()
    {
        Assert.True(Units["civilian"].IsCivilian);
        Assert.Equal(Behaviors.Flee, Units["civilian"].Behavior);
    }

    [Fact]
    public void TheStreetScenario_LoadsAndPlays()
    {
        // Les héros attendent (personne ne les commande) : l'IA ennemie et les civils jouent.
        var sim = new CombatSimulation(Content.SetupFor("street"));
        for (var i = 0; i < Rules.TicksPerSecond * 30; i++)
            sim.Tick();

        Assert.Contains(sim.EventLog, e => e is SkillUsed);
        Assert.Contains(sim.EventLog, e => e is UnitEscaped);
    }

    [Fact]
    public void TheStreetScenario_FieldsTheEstablishedHeroes_AtLevel20()
    {
        var sim = new CombatSimulation(Content.SetupFor("street"));
        (int MaxHp, int Initiative, int Power) Stats(string id)
        {
            var unit = sim.Units.Single(u => u.Definition.Id == id);
            return (unit.MaxHp, unit.Initiative, unit.PowerPercent);
        }

        // Niveau 20 : 19 points de stats chacun, qui les portent des stats de base de l'archétype à celles du prologue.
        Assert.All(Content.Heroes!.Sheets.Values.Where(h => h.Id.StartsWith("prologue-")), hero =>
        {
            Assert.Equal(20, hero.Level);
            Assert.Equal(19, hero.Points.Hp + hero.Points.Power + hero.Points.Initiative);
        });
        Assert.Equal((160, 70, 115), Stats("colossus"));
        Assert.Equal((85, 160, 109), Stats("speedster"));
        Assert.Equal((110, 120, 124), Stats("fighter"));
        Assert.Equal((80, 100, 139), Stats("energetic"));
        Assert.Equal((85, 100, 121), Stats("tech"));
        Assert.Equal((75, 100, 121), Stats("telepath"));
        Assert.Equal(2, sim.Units.Single(u => u.Definition.Id == "colossus").OpportunityAttacksPerCycle); // Vigilance
        // Les autres unités n'ont pas de points.
        Assert.All(sim.Units.Where(u => !Archetypes.Contains(u.Definition.Id)), u => Assert.Equal(100, u.PowerPercent));
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
        // Les héros du prologue sont confiés à l'IA de base pour que le combat se joue seul.
        var units = new List<UnitSetup>();
        var id = 1;
        for (var i = 0; i < heroIds.Length; i++)
        {
            var hero = Content.Heroes!.Unit($"prologue-{heroIds[i]}");
            units.Add((hero.Definition with { Behavior = Behaviors.Basic }).ToSetup(new UnitId(id++), team: 0, new Cell(0, 2 + i), points: hero.Points));
        }
        foreach (var (enemy, x, y) in enemies)
            units.Add(Units[enemy].ToSetup(new UnitId(id++), team: 1, new Cell(x, y)));
        // Des civils pris entre les deux camps : ils fuient, et les zones peuvent les blesser.
        foreach (var (x, y) in new[] { (7, 3), (8, 4), (9, 1) })
            units.Add(Units["civilian"].ToSetup(new UnitId(id++), team: 2, new Cell(x, y)));

        var (sim, outcome) = ContentTestKit.PlayToTheEnd(new CombatSetup(Rules, Arena, units, Seed: 2026), name);

        _output.WriteLine(outcome);
        foreach (var unit in sim.Units)
            _output.WriteLine($"  {unit.Definition.Name,-12} équipe {unit.Team}  {unit.Status,-8} {unit.Hp,4}/{unit.MaxHp} PV{(unit.IsFleeing ? " en fuite" : "")}{(unit.Escaped ? ", enfui" : "")}");
    }
}
