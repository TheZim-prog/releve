using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Les dégâts collatéraux relevés pendant le combat : le décor que cassent les héros, les civils touchés.</summary>
public class CollateralTests
{
    private static readonly UnitId Shooter = new(1), Target = new(2), Civil = new(5);

    /// <summary>Tir de 80 dégâts, portée 6 : de quoi briser un obstacle de 40 PV qui couvre la cible.</summary>
    private static readonly SkillDefinition Cannon = Strike with { Id = "cannon", Range = 6, MinDamage = 80, MaxDamage = 80 };

    /// <summary>Zone 3x3 à distance, qui touche tout le monde (civils compris).</summary>
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

    private static UnitSetup Civilian(int x, int y) => CivilianDefinition.ToSetup(Civil, team: 2, new Cell(x, y));

    [Fact]
    public void ACoverBrokenByAHero_IsCollateralDamage()
    {
        var sim = Simulation(Grid.FromRows("...o..."), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Cannon }),
            Unit(2, team: 1, 4, 0, hp: 200));

        sim.Submit(Use(Shooter, "cannon", Target));
        sim.Tick();

        Assert.Equal(1, sim.SceneryDestroyedByHeroes);
    }

    [Fact]
    public void ACoverBrokenByAnEnemy_DoesNotCount()
    {
        var sim = Simulation(Grid.FromRows("...o..."), Unit(1, team: 1, 0, 0, gauge: Capacity, skills: new[] { Cannon }),
            Unit(2, team: 0, 4, 0, hp: 200));

        sim.Submit(Use(Shooter, "cannon", Target));
        sim.Tick();

        Assert.Contains(sim.EventLog, e => e is CoverDestroyed);
        Assert.Equal(0, sim.SceneryDestroyedByHeroes);
    }

    [Fact]
    public void ThrowingTheScenery_IsCollateralDamage()
    {
        var toss = Strike with
        {
            Id = "toss", Range = 5,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
            Scenery = new SceneryDefinition { Use = SceneryUse.Throw },
        };
        var sim = Simulation(Grid.FromRows("........", ".o......", "........"),
            Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { toss }), Unit(9, team: 1, 6, 1));

        sim.Submit(Use(Shooter, "toss", cell: new Cell(5, 1)));
        sim.Tick();

        Assert.Equal(1, sim.SceneryDestroyedByHeroes);
    }

    [Fact]
    public void AnIceBlockBroken_IsNotCollateralDamage()
    {
        var iceBlock = new SkillDefinition
        {
            Id = "raise", Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false },
            Raises = new RaisedObstacleDefinition { Terrain = Terrain.LowObstacle, Hp = 5, DurationMs = 5000 },
        };
        var sim = Simulation(new Grid(7, 1), Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { iceBlock, Cannon }),
            Unit(2, team: 1, 4, 0, hp: 200));
        sim.Submit(Use(Shooter, "raise", cell: new Cell(3, 0)));
        sim.Tick();
        while (!sim.Get(1).IsGaugeFull)
            sim.Tick();

        sim.Submit(Use(Shooter, "cannon", Target));
        sim.Tick();

        Assert.Contains(sim.EventLog, e => e is CoverDestroyed { Cell.X: 3 });
        Assert.Equal(0, sim.SceneryDestroyedByHeroes);
    }

    [Fact]
    public void ACivilianCaughtInAHerosZone_IsHurtByTheHeroes_AndLostIfItFalls()
    {
        var hurt = Simulation(new Grid(8, 3), Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Blast(10) }),
            Unit(2, team: 1, 4, 1), Civilian(4, 0));
        hurt.Submit(Use(Shooter, "blast", cell: new Cell(4, 1)));
        hurt.Tick();
        Assert.True(hurt.WasHurtByHeroes(Civil));
        Assert.False(hurt.WasLostToHeroes(Civil));

        var lost = Simulation(new Grid(8, 3), Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Blast(50) }),
            Unit(2, team: 1, 4, 1, hp: 200), Civilian(4, 0));
        lost.Submit(Use(Shooter, "blast", cell: new Cell(4, 1)));
        lost.Tick();
        Assert.True(lost.WasLostToHeroes(Civil));
    }

    [Fact]
    public void AnEnemyDominatedByTheHeroes_HurtsCiviliansOnTheirBehalf()
    {
        // L'unité 1 est un ennemi passé dans le camp des joueurs (dominé) au moment du coup.
        var sim = Simulation(new Grid(8, 3), Unit(1, team: 1, 0, 1, gauge: Capacity, skills: new[] { Blast(10) }),
            Unit(2, team: 1, 4, 1), Civilian(4, 0));
        sim.Get(1).Team = CombatSimulation.PlayerTeam;

        sim.Submit(Use(Shooter, "blast", cell: new Cell(4, 1)));
        sim.Tick();

        Assert.True(sim.WasHurtByHeroes(Civil));
    }

    [Fact]
    public void ACivilianKilledByAnEnemy_IsNotTheHeroesDoing()
    {
        var sim = Simulation(new Grid(8, 3), Unit(1, team: 1, 0, 1, gauge: Capacity, skills: new[] { Blast(50) }),
            Unit(2, team: 0, 4, 1, hp: 200), Civilian(4, 0));

        sim.Submit(Use(Shooter, "blast", cell: new Cell(4, 1)));
        sim.Tick();

        Assert.Equal(UnitStatus.Removed, sim.FindUnit(Civil)!.Status);
        Assert.False(sim.WasHurtByHeroes(Civil));
        Assert.False(sim.WasLostToHeroes(Civil));
    }
}
