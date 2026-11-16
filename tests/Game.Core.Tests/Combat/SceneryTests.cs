using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Le décor pris par une compétence : lancé (Lancer de décor) ou posé ailleurs (Barricade).</summary>
public class SceneryTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId Foe = new(9);

    /// <summary>Un obstacle à son contact, lancé sur un carré 3x3 à 5 cases au plus : 10 dégâts.</summary>
    private static readonly SkillDefinition Toss = Strike with
    {
        Id = "toss", Range = 5,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        Scenery = new SceneryDefinition { Use = SceneryUse.Throw },
    };

    /// <summary>Un obstacle à son contact, posé sur une case libre à 3 cases au plus.</summary>
    private static readonly SkillDefinition Barricade = new()
    {
        Id = "barricade", Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false },
        Scenery = new SceneryDefinition { Use = SceneryUse.Place },
    };

    private static UnitSetup CasterAt(int x, int y, SkillDefinition skill) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: new[] { skill });

    [Fact]
    public void Toss_ThrowsTheObstacleAtItsSide_WhichIsDestroyed_AndHitsTheZone()
    {
        var sim = Simulation(Grid.FromRows("........", ".o......", "........"), CasterAt(0, 1, Toss), Unit(9, team: 1, 6, 1));

        sim.Submit(Use(Caster, "toss", cell: new Cell(5, 1)));
        var events = sim.Tick();

        Assert.Contains(new SceneryTaken(1, Caster, new Cell(1, 1), null), events);
        Assert.Equal(Terrain.Floor, sim.Grid.TerrainAt(new Cell(1, 1)));
        Assert.Equal(90, sim.Get(9).Hp);
    }

    [Fact]
    public void Toss_WithoutAnObstacleAtItsSide_IsRefused()
    {
        // Un obstacle à deux cases, et un autre en diagonale derrière un coin de mur : aucun n'est sous la main.
        var sim = Simulation(Grid.FromRows("o#......", "...o....", "........"), CasterAt(1, 1, Toss), Unit(9, team: 1, 6, 1));

        Assert.Equal(RejectionReason.NoScenery, sim.Check(Use(Caster, "toss", cell: new Cell(6, 1))));
    }

    [Fact]
    public void Toss_TakesTheObstacleClosestToTheAimedCell()
    {
        var sim = Simulation(Grid.FromRows("........", "o.o.....", "........"), CasterAt(1, 1, Toss), Unit(9, team: 1, 6, 1));

        Assert.Equal(new Cell(2, 1), sim.PreviewAction(Caster, new Cell(1, 1), new SkillUse("toss", null, new Cell(6, 1))).Destination);
        sim.Submit(Use(Caster, "toss", cell: new Cell(6, 1)));

        Assert.Contains(new SceneryTaken(1, Caster, new Cell(2, 1), null), sim.Tick());
        Assert.Equal(Terrain.LowObstacle, sim.Grid.TerrainAt(new Cell(0, 1)));
    }

    [Fact]
    public void FromAfar_TheObstacleMustBeInReachAndInSight()
    {
        var far = Toss with { Scenery = new SceneryDefinition { Use = SceneryUse.Throw, Reach = 4 } };
        var inSight = Simulation(Grid.FromRows("...o....", "........", "........"), CasterAt(0, 0, far), Unit(9, team: 1, 6, 2));
        var hidden = Simulation(Grid.FromRows(".#.o....", ".#......", ".#......"), CasterAt(0, 0, far), Unit(9, team: 1, 6, 2));

        Assert.Null(inSight.Check(Use(Caster, "toss", cell: new Cell(5, 2))));
        Assert.Equal(RejectionReason.NoScenery, hidden.Check(Use(Caster, "toss", cell: new Cell(0, 2))));
    }

    [Fact]
    public void Barricade_MovesTheObstacle_OntoAFreeCell()
    {
        var sim = Simulation(Grid.FromRows("........", ".o......", "........"), CasterAt(0, 1, Barricade), Unit(9, team: 1, 3, 0));

        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Use(Caster, "barricade", cell: new Cell(3, 0))));
        sim.Submit(Use(Caster, "barricade", cell: new Cell(3, 1)));
        var events = sim.Tick();

        Assert.Contains(new SceneryTaken(1, Caster, new Cell(1, 1), new Cell(3, 1)), events);
        Assert.Equal(Terrain.Floor, sim.Grid.TerrainAt(new Cell(1, 1)));
        Assert.Equal(Terrain.LowObstacle, sim.Grid.TerrainAt(new Cell(3, 1)));
    }

    [Fact]
    public void Barricade_TheMovedObstacleKeepsItsDamage()
    {
        // Un tireur entame d'abord l'obstacle qui couvre le colosse (40 PV, moitié des dégâts absorbée) ; posé devant
        // l'allié, il le couvre avec ce qu'il lui reste.
        var shot = Strike with { Id = "shot", Range = 6, MinDamage = 20, MaxDamage = 20 };
        var sim = Simulation(Grid.FromRows("........", ".o......", "........"),
            CasterAt(0, 1, Barricade), Unit(2, team: 0, 0, 2),
            Unit(9, team: 1, 6, 1, gauge: Capacity, initiative: Capacity, skills: new[] { shot }));
        sim.Submit(Use(Foe, "shot", Caster));
        Assert.Contains(new HitCovered(1, Caster, new Cell(1, 1), 10, 30), sim.Tick());

        sim.Submit(Use(Caster, "barricade", cell: new Cell(1, 2)));
        sim.Tick();
        sim.Submit(Use(Foe, "shot", new UnitId(2)));

        Assert.Contains(new HitCovered(3, new UnitId(2), new Cell(1, 2), 10, 20), sim.Tick());
    }

    [Fact]
    public void ASceneryDefinition_IsAThrowOnAZone_OrAPlacementOnOneCell()
    {
        Toss.Validate();
        Barricade.Validate();
        Assert.Throws<ArgumentException>(() => (Toss with { MaxDamage = 0, MinDamage = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Barricade with { Zone = Toss.Zone }).Validate());
        Assert.Throws<ArgumentException>(() => (Toss with { Zone = null }).Validate());
        Assert.Throws<ArgumentException>(() => (Toss with { Scenery = new SceneryDefinition { Use = SceneryUse.Throw, Reach = 0 } }).Validate());
    }
}
