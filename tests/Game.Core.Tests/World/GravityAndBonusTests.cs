using Game.Core.Combat;
using Game.Core.Network;
using Game.Core.Objectives;
using Game.Core.World;
using Game.Core.Tests.Combat;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests.World;

/// <summary>Ce que la route laisse à la mission : la gravité des crochets, et le bonus gagné pour ses héros.</summary>
public class GravityAndBonusTests
{
    private static CombatSetup Quest(string scenario, GravityStep? gravity = null, int level = 0, QuestModifiers? modifiers = null) =>
        new CombatLaunch(scenario, Array.Empty<HeroAssignment>(), new[] { QuestContentTests.Recruit("colossus") })
        {
            Gravity = gravity, GravityLevel = level, Modifiers = modifiers,
        }.BuildSetup(Content);

    [Fact]
    public void EachLevel_BringsTheReinforcementsOfTheRule()
    {
        var rule = Content.WorldRules!.GravityFor(Content.Scenarios["quest-thugs"].Objectives ?? ObjectiveDefinition.Default);

        var calm = Quest("quest-thugs");
        var grave = Quest("quest-thugs", rule, level: 2);

        // Une vague dès le début : deux cogneurs, posés au plus près à pied du premier ennemi.
        var wave = Assert.Single(grave.Waves);
        Assert.Equal(1, wave.AtMs);
        Assert.All(wave.Units, u => Assert.Equal(("brawler", 1), (u.Definition.Id, u.Team)));
        var sim = new CombatSimulation(grave);
        sim.Tick();
        var added = sim.Units.Where(u => u.Id.Value > calm.Units.Max(c => c.Id.Value)).ToList();
        Assert.Equal(2, added.Count);
        Assert.Equal(2, added.Select(u => u.Position).Distinct().Count());
    }

    [Fact]
    public void ACivilianTheScenarioNames_IsNeverLost()
    {
        var calm = Quest("quest-market");
        var named = calm with
        {
            Units = calm.Units.Select(u => u.Definition.IsCivilian ? u with { Tag = $"civ-{u.Id.Value}" } : u).ToList(),
        };

        var grave = new GravityStep { CiviliansLost = 1 }.Apply(named, 3, Content.Units);

        Assert.Equal(named.Units.Count, grave.Units.Count);
    }

    [Fact]
    public void AProtectionToHold_IsNotShortened()
    {
        var setup = Setup(new Game.Core.Grids.Grid(4, 1), Game.Core.Tests.Combat.CombatTestKit.Unit(1, team: 0, 0, 0)) with
        {
            Objectives = new[] { new ObjectiveDefinition { Kind = ObjectiveKind.Protect, Until = ProtectUntil.Time, DurationMs = 60000 } },
        };

        Assert.Equal(60000, new GravityStep { CountdownLessMs = 10000 }.Apply(setup, 2, Content.Units).Objectives[0].DurationMs);
    }

    [Fact]
    public void LostCivilians_NeverGoBelowTheRescueMinimum()
    {
        var calm = Quest("quest-market");

        var grave = Quest("quest-market", new GravityStep { CiviliansLost = 1 }, level: 5);

        // Trois civils, minimum 2 : un seul peut être déjà perdu.
        Assert.Equal(calm.Units.Count(u => u.Definition.IsCivilian) - 1, grave.Units.Count(u => u.Definition.IsCivilian));
    }

    [Fact]
    public void ACountdown_IsShortened_ButNeverBelowAQuarter()
    {
        var setup = Setup(new Game.Core.Grids.Grid(4, 1), Game.Core.Tests.Combat.CombatTestKit.Unit(1, team: 0, 0, 0)) with
        {
            Objectives = new[]
            {
                new ObjectiveDefinition
                {
                    Kind = ObjectiveKind.Countdown, DurationMs = 60000,
                    Devices = new[] { new DeviceDefinition { X = 3, Y = 0, Interactions = 1 } },
                },
            },
        };
        var step = new GravityStep { CountdownLessMs = 10000 };

        Assert.Equal(40000, step.Apply(setup, 2, Content.Units).Objectives[0].DurationMs);
        Assert.Equal(15000, step.Apply(setup, 9, Content.Units).Objectives[0].DurationMs);
    }

    [Fact]
    public void AQuestBonus_GivesTheDeployedHeroesGauge_Armor_AndPoints()
    {
        var bonus = new QuestModifiers { StartGaugePercent = 50, StartTempHp = 10, Power = 2, Hp = -3 };

        var setup = Quest("quest-thugs", modifiers: bonus);

        var hero = setup.Units.Single(u => u.Id == Content.Scenarios["quest-thugs"].DeployedUnitId(0));
        Assert.Equal(Content.Rules.GaugeAt(50), hero.InitialGauge);
        Assert.Equal(10, hero.StartTempHp);
        Assert.Equal((2, 0), (hero.Points!.Power, hero.Points.Hp)); // un malus ne retire pas plus que ses points
        var unit = new CombatSimulation(setup).FindUnit(hero.Id)!;
        Assert.Equal(10, unit.Armor);
        Assert.All(setup.Units.Where(u => u.Team == 1), u => Assert.Equal(0, u.StartTempHp)); // les ennemis, rien
    }

    [Fact]
    public void Armor_AbsorbsAfterTemporaryHp_AndOutlastsThem()
    {
        var hero = Game.Core.Tests.Combat.CombatTestKit.Unit(1, team: 0, 0, 0) with { StartTempHp = 10 };
        var thug = Game.Core.Tests.Combat.CombatTestKit.Unit(2, team: 1, 1, 0, gauge: Game.Core.Tests.Combat.CombatTestKit.Capacity);
        var sim = new CombatSimulation(Setup(new Game.Core.Grids.Grid(4, 1), hero, thug));
        sim.Get(1).TempHp = 5;
        sim.Get(1).TempHpEndTick = 1000;

        sim.Submit(Game.Core.Tests.Combat.CombatTestKit.Use(new UnitId(2), "strike", new UnitId(1))); // 10 dégâts
        sim.Tick();

        Assert.Equal((0, 5, 100), (sim.Get(1).TempHp, sim.Get(1).Armor, sim.Get(1).Hp));
    }

    [Fact]
    public void BonusesAddUp_TheHighestStartGaugeWins()
    {
        var both = new QuestModifiers { StartGaugePercent = 30, Power = 1 }.Plus(new QuestModifiers { StartGaugePercent = 50, Power = 1 });

        Assert.Equal((50, 2), (both.StartGaugePercent, both.Power));
    }

    [Fact]
    public void TheGamesWorld_Loads_WithTheTrialQuestsOnItsSites()
    {
        Assert.NotNull(Content.World);
        Assert.All(Content.Quests.Values, q => Assert.Equal(MapNodeKind.Site, Content.World!.Node(q.Site!)!.Kind));
        Assert.Equal(Game.Core.Campaign.QuestDifficulty.VeryEasy, Content.WorldRules!.LabelOf(90));
        Assert.Equal(Game.Core.Campaign.QuestDifficulty.VeryHard, Content.WorldRules!.LabelOf(10));
    }

    private static CombatSetup Setup(Game.Core.Grids.Grid grid, params UnitSetup[] units) => Game.Core.Tests.Combat.CombatTestKit.Setup(grid, units);
}
