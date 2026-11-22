using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Objectives;
using Game.Core.Reputation;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Reputation;

/// <summary>Le bilan de réputation d'une mission : chaque fait compté une fois, pesé selon les règles.</summary>
public class ReputationReportTests
{
    private static readonly UnitId Hero = new(1), Foe = new(2), Civil = new(5);

    private static readonly ReputationRules Rules = new()
    {
        SceneryDestroyed = -2, CivilianHurtByHeroes = -3, CivilianLostToHeroes = -10, CivilianLostToEnemies = -5,
        CivilianSaved = 2, SecondaryObjectiveCompleted = 5,
        Scale = new ReputationScale { Min = -10, Max = 10, Start = 0, Tiers = new[] { new ReputationTier("low", "Bas", -10) } },
    };

    /// <summary>Zone 3x3 à distance, qui touche tout le monde.</summary>
    private static SkillDefinition Blast(int damage) => Strike with
    {
        Id = "blast", Range = 5, MinDamage = damage, MaxDamage = damage,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.All },
    };

    private static readonly UnitDefinition CivilianDefinition = new()
    {
        Id = "civilian", Name = "Civil", MaxHp = 30, Initiative = 10, Mobility = 3, OnDefeat = DefeatOutcome.Removed,
        BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Flee, Skills = Array.Empty<SkillDefinition>(),
        IsCivilian = true,
    };

    /// <summary>
    /// Un héros (1) qui frappe une zone de <paramref name="damage"/> sur l'ennemi (2, 40 PV) en (4, 1), un civil (5) à côté
    /// de lui en (4, 0), avec ces objectifs ; le combat se joue d'un coup.
    /// </summary>
    private static CombatSimulation HeroBlasts(int damage, int foeHp = 40, IReadOnlyList<ObjectiveDefinition>? objectives = null)
    {
        var setup = Setup(new Grid(8, 3),
                Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Blast(damage) }),
                Unit(2, team: 1, 4, 1, hp: foeHp),
                CivilianDefinition.ToSetup(Civil, team: 2, new Cell(4, 0)))
            with { Objectives = objectives ?? ObjectiveDefinition.Default };
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(Hero, "blast", cell: new Cell(4, 1)));
        sim.Tick();
        return sim;
    }

    [Fact]
    public void ACivilianKilledByTheHeroes_CostsTheMost()
    {
        var report = ReputationReport.Of(HeroBlasts(50), Rules);

        Assert.Equal(1, report.CiviliansLostToHeroes);
        Assert.Equal(0, report.CiviliansHurtByHeroes);
        Assert.Equal(-10, report.Total);
    }

    [Fact]
    public void ACivilianHurtByTheHeroes_IsNotSaved_EvenAtVictory()
    {
        var sim = HeroBlasts(10, foeHp: 10);

        var report = ReputationReport.Of(sim, Rules);

        Assert.True(sim.Outcome);
        Assert.Equal(1, report.CiviliansHurtByHeroes);
        Assert.Equal(0, report.CiviliansSaved);
        Assert.Equal(-3, report.Total);
    }

    [Fact]
    public void ACivilianUnharmedAtVictory_IsSaved_AndASecondaryObjectivePays()
    {
        // Le héros frappe l'ennemi seul (le civil est hors de la zone) ; le sauvetage, secondaire, est réussi.
        var setup = Setup(new Grid(8, 4),
                Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Blast(50) }),
                Unit(2, team: 1, 4, 1, hp: 40),
                CivilianDefinition.ToSetup(Civil, team: 2, new Cell(7, 3)))
            with
            {
                Objectives = new[]
                {
                    new ObjectiveDefinition { Kind = ObjectiveKind.Eliminate },
                    new ObjectiveDefinition { Kind = ObjectiveKind.Rescue, Minimum = 1, Secondary = true },
                },
            };
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(Hero, "blast", cell: new Cell(4, 1)));
        sim.Tick();

        var report = ReputationReport.Of(sim, Rules);

        Assert.True(sim.Outcome);
        Assert.Equal(1, report.CiviliansSaved);
        Assert.Equal(1, report.SecondaryObjectivesCompleted);
        Assert.Equal(2 + 5, report.Total);
    }

    [Fact]
    public void ACivilianKilledByTheEnemies_CostsLess_ThanByTheHeroes()
    {
        var setup = Setup(new Grid(8, 3),
            Unit(1, team: 1, 0, 1, gauge: Capacity, skills: new[] { Blast(50) }),
            Unit(2, team: 0, 4, 1, hp: 200),
            CivilianDefinition.ToSetup(Civil, team: 2, new Cell(4, 0)));
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(Hero, "blast", cell: new Cell(4, 1))); // unité 1, ennemie ici
        sim.Tick();

        var report = ReputationReport.Of(sim, Rules);

        Assert.Equal(1, report.CiviliansLostToEnemies);
        Assert.Equal(-5, report.Total);
    }

    [Fact]
    public void AnEvacuatedCivilian_IsSaved_EvenBeforeTheEnd()
    {
        // Au bord de la carte, jauge pleine : il sort aussitôt.
        var sim = Simulation(new Grid(8, 3), Unit(1, team: 0, 0, 1), Unit(2, team: 1, 7, 2),
            CivilianDefinition.ToSetup(Civil, team: 2, new Cell(4, 0), Capacity));
        sim.Tick();

        Assert.True(sim.FindUnit(Civil)!.Escaped);
        Assert.Equal(1, ReputationReport.Of(sim, Rules).CiviliansSaved);
    }

    [Fact]
    public void AnUnharmedCivilianLeftOnTheMap_AtADefeat_DoesNotCount()
    {
        // L'ennemi abat le seul héros : défaite, le civil (loin, jauge vide) est encore là.
        var setup = Setup(new Grid(8, 4),
                Unit(1, team: 0, 0, 1, hp: 10, onDefeat: DefeatOutcome.Downed),
                Unit(2, team: 1, 1, 1, gauge: Capacity),
                CivilianDefinition.ToSetup(Civil, team: 2, new Cell(5, 2)))
            with { Objectives = ObjectiveDefinition.Default };
        var sim = new CombatSimulation(setup);
        sim.Submit(Use(Foe, "strike", Hero));
        sim.Tick();

        var report = ReputationReport.Of(sim, Rules);

        Assert.False(sim.Outcome);
        Assert.Equal(0, report.CiviliansSaved);
        Assert.Equal(0, report.Total);
    }

    [Fact]
    public void BrokenScenery_IsCounted()
    {
        var cannon = Strike with { Id = "cannon", Range = 6, MinDamage = 80, MaxDamage = 80 };
        var sim = Simulation(Grid.FromRows("...o..."), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { cannon }),
            Unit(2, team: 1, 4, 0, hp: 200));
        sim.Submit(Use(Hero, "cannon", Foe));
        sim.Tick();

        var report = ReputationReport.Of(sim, Rules);

        Assert.Equal(1, report.SceneryDestroyed);
        Assert.Equal(-2, report.Total);
    }

    [Fact]
    public void APenalty_CannotBePositive_NorABonusNegative()
    {
        Assert.Throws<ArgumentException>(() => (Rules with { SceneryDestroyed = 1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Rules with { CivilianSaved = -1 }).Validate());
    }

    [Fact]
    public void TheGamesReputationRules_AreLoaded() => Assert.NotNull(ContentTestKit.Content.Reputation);
}
