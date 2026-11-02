using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class CoverTests
{
    private static readonly UnitId Shooter = new(1);
    private static readonly UnitId Target = new(2);

    /// <summary>Tir de 20 dégâts fixes, portée 6.</summary>
    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 6, MinDamage = 20, MaxDamage = 20 };

    private static UnitSetup ShooterAt(int x, int y, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: skills.Length == 0 ? new[] { Shot } : skills);

    private static ActivateCommand Fire(string skill = "shot") => new(Shooter, null, new SkillUse(skill, Target));

    [Fact]
    public void ALowObstacleNextToTheTarget_OnTheLine_HalvesTheDamage_AndTakesThem()
    {
        var sim = Simulation(Grid.FromRows("...o..."), ShooterAt(0, 0), Unit(2, team: 1, 4, 0));

        sim.Submit(Fire());
        var events = sim.Tick();

        Assert.Contains(new HitCovered(1, Target, new Cell(3, 0), 10, 30), events);
        Assert.Contains(new UnitDamaged(1, Target, 10, 90), events);
    }

    [Fact]
    public void AnObstacleAwayFromTheTarget_GivesNoCover()
    {
        var sim = Simulation(Grid.FromRows("..o...."), ShooterAt(0, 0), Unit(2, team: 1, 4, 0));

        sim.Submit(Fire());
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is HitCovered);
        Assert.Contains(new UnitDamaged(1, Target, 20, 80), events);
    }

    [Fact]
    public void FlankingTheObstacle_GivesNoCover()
    {
        // L'obstacle (3,1) est collé à la cible (4,1), mais le tir vient d'en haut : la ligne ne le traverse pas.
        var grid = Grid.FromRows(
            "......",
            "...o..",
            "......",
            "......");
        var sim = Simulation(grid, ShooterAt(4, 3), Unit(2, team: 1, 4, 1));

        // Tireur sous la cible : ligne (4,3) -> (4,1), seule la case (4,2) est traversée.
        sim.Submit(Fire());

        Assert.DoesNotContain(sim.Tick(), e => e is HitCovered);
    }

    [Fact]
    public void MeleeIgnoresCover_EvenAcrossTheCornerOfAnObstacle()
    {
        var grid = Grid.FromRows(
            "....",
            ".o..");
        var sim = Simulation(grid, ShooterAt(0, 1), Unit(2, team: 1, 1, 0));

        sim.Submit(Fire());
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is HitCovered);
        Assert.Contains(new UnitDamaged(1, Target, 20, 80), events);
    }

    [Fact]
    public void ZonesAreCovered_FromTheirCenter()
    {
        var blast = Shot with
        {
            Id = "blast", Range = 6,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 3, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        };
        var behind = Simulation(Grid.FromRows("...o...."), ShooterAt(0, 0, blast), Unit(2, team: 1, 4, 0));
        behind.Submit(new ActivateCommand(Shooter, null, new SkillUse("blast", null, new Cell(1, 0))));
        Assert.Contains(new HitCovered(1, Target, new Cell(3, 0), 10, 30), behind.Tick());

        // Explosion centrée de l'autre côté de la cible : l'obstacle n'est pas entre elles.
        var front = Simulation(Grid.FromRows("...o...."), ShooterAt(0, 0, blast), Unit(2, team: 1, 4, 0));
        front.Submit(new ActivateCommand(Shooter, null, new SkillUse("blast", null, new Cell(6, 0))));
        Assert.DoesNotContain(front.Tick(), e => e is HitCovered);
    }

    [Fact]
    public void AChargedLine_IsCoveredFromItsCaster()
    {
        var burst = Shot with
        {
            Id = "burst", Range = 1, ChargeMs = 100, Weight = SkillWeight.Heavy,
            Zone = new ZoneDefinition { Shape = ZoneShape.Line, Size = 6, ThroughWalls = false },
        };
        var sim = Simulation(Grid.FromRows("...o..."), ShooterAt(0, 0, burst), Unit(2, team: 1, 4, 0));

        sim.Submit(new ActivateCommand(Shooter, null, new SkillUse("burst", null, new Cell(1, 0))));
        var events = sim.TickTimes(3);

        Assert.Contains(events, e => e is HitCovered { Cover: { X: 3, Y: 0 }, Absorbed: 10 });
    }

    [Fact]
    public void ACoverAtZeroHp_BecomesFloor_AndNoLongerProtects()
    {
        var cannon = Shot with { Id = "cannon", MinDamage = 80, MaxDamage = 80 };
        var sim = Simulation(Grid.FromRows("...o..."), ShooterAt(0, 0, cannon, Shot), Unit(2, team: 1, 4, 0, hp: 200));

        sim.Submit(Fire("cannon"));
        var events = sim.Tick();

        Assert.Contains(new HitCovered(1, Target, new Cell(3, 0), 40, 0), events);
        Assert.Contains(new CoverDestroyed(1, new Cell(3, 0)), events);
        Assert.Contains(new UnitDamaged(1, Target, 40, 160), events);
        Assert.Equal(Terrain.Floor, sim.Grid.TerrainAt(new Cell(3, 0)));

        while (!sim.Get(1).IsGaugeFull)
            sim.Tick();
        sim.Submit(Fire());
        Assert.DoesNotContain(sim.Tick(), e => e is HitCovered);
    }

    [Fact]
    public void AWornCover_AbsorbsNoMoreThanItsHp()
    {
        var cannon = Shot with { Id = "cannon", MinDamage = 80, MaxDamage = 80 };
        var sim = Simulation(Grid.FromRows("...o..."), ShooterAt(0, 0, cannon, Shot), Unit(2, team: 1, 4, 0, hp: 200));
        for (var i = 0; i < 3; i++) // 3 tirs de 20 : l'obstacle absorbe 10 chaque fois, il lui reste 10 PV
        {
            while (!sim.Get(1).IsGaugeFull)
                sim.Tick();
            sim.Submit(Fire());
            sim.Tick();
        }
        while (!sim.Get(1).IsGaugeFull)
            sim.Tick();

        sim.Submit(Fire("cannon"));
        var events = sim.Tick();

        Assert.Contains(events, e => e is HitCovered { Absorbed: 10, CoverHp: 0 });
        Assert.Contains(events, e => e is UnitDamaged { Unit.Value: 2, Amount: 70 });
    }

    [Fact]
    public void ZeroReduction_DisablesCover()
    {
        var setup = Setup(Grid.FromRows("...o..."), ShooterAt(0, 0), Unit(2, team: 1, 4, 0));
        var sim = new CombatSimulation(setup with { Rules = Rules with { CoverDamageReductionPercent = 0 } });

        sim.Submit(Fire());
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is HitCovered);
        Assert.Contains(new UnitDamaged(1, Target, 20, 80), events);
    }

    [Fact]
    public void ALeapsWave_IsCoveredFromItsLanding()
    {
        var leap = Shot with
        {
            Id = "leap", Range = 4, SelfMove = SelfMoveType.Leap,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 3, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        };
        var sim = Simulation(Grid.FromRows("....o..."), ShooterAt(0, 0, leap), Unit(2, team: 1, 5, 0));

        sim.Submit(new ActivateCommand(Shooter, null, new SkillUse("leap", null, new Cell(2, 0))));
        var events = sim.Tick();

        Assert.Contains(new HitCovered(1, Target, new Cell(4, 0), 10, 30), events);
    }

    [Fact]
    public void AnExecution_IgnoresCover()
    {
        var execution = Shot with { Id = "execution", ExecuteAtOrBelowPercent = 30 };
        var sim = Simulation(Grid.FromRows("...o..."), ShooterAt(0, 0, execution), Unit(2, team: 1, 4, 0));
        sim.Get(2).Hp = 25;

        sim.Submit(Fire("execution"));
        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitExecuted);
        Assert.DoesNotContain(events, e => e is HitCovered);
    }

    [Fact]
    public void CoverWearsDown_HitAfterHit()
    {
        var sim = Simulation(Grid.FromRows("...o..."), ShooterAt(0, 0), Unit(2, team: 1, 4, 0));

        sim.Submit(Fire());
        sim.Tick();
        while (!sim.Get(1).IsGaugeFull)
            sim.Tick();
        sim.Submit(Fire());
        var events = sim.Tick();

        Assert.Contains(events, e => e is HitCovered { Absorbed: 10, CoverHp: 20 });
    }
}
