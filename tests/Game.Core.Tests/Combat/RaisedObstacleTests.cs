using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Obstacles dressés pour un temps par une compétence (Mur de glace, Pilier de glace).</summary>
public class RaisedObstacleTests
{
    private static readonly UnitId Caster = new(1);
    private static readonly UnitId Enemy = new(2);

    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 6 };

    /// <summary>Trois blocs en travers (5 PV, 1 s), à 4 cases au plus.</summary>
    private static readonly SkillDefinition IceWall = new()
    {
        Id = "raise", Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Wall, Size = 1, ThroughWalls = false },
        Raises = new RaisedObstacleDefinition { Terrain = Terrain.LowObstacle, Hp = 5, DurationMs = 1000 },
    };

    /// <summary>Un seul bloc, haut comme un mur (1 s).</summary>
    private static readonly SkillDefinition IcePillar = IceWall with
    {
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 0, ThroughWalls = false },
        Raises = new RaisedObstacleDefinition { Terrain = Terrain.Wall, DurationMs = 1000 },
    };

    private static UnitSetup CasterAt(int x, int y, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, gauge: Capacity, skills: skills);

    private static ActivateCommand Raise(int x, int y) => Use(Caster, "raise", cell: new Cell(x, y));

    [Fact]
    public void AnIceWall_RaisesBlocksAcross_OnTheFreeCells_AndTheyMelt()
    {
        // L'ennemi occupe l'une des trois cases : pas de bloc sous lui.
        var sim = Simulation(new Grid(7, 5), CasterAt(0, 2, IceWall), Unit(2, team: 1, 3, 1));

        sim.Submit(Raise(3, 2));
        var events = sim.Tick();

        Assert.Equal(
            new[] { new ObstacleRaised(1, new Cell(3, 2), Terrain.LowObstacle, 21), new ObstacleRaised(1, new Cell(3, 3), Terrain.LowObstacle, 21) },
            events.OfType<ObstacleRaised>());
        Assert.Equal(Terrain.LowObstacle, sim.Grid.TerrainAt(new Cell(3, 2)));
        Assert.Equal(Terrain.Floor, sim.Grid.TerrainAt(new Cell(3, 1)));
        Assert.Null(sim.PathFor(Caster, new Cell(3, 2))); // on ne marche pas dessus

        var later = sim.TickTimes(20);

        Assert.Equal(new[] { new Cell(3, 2), new Cell(3, 3) }, later.OfType<ObstacleMelted>().Select(e => e.Cell));
        Assert.Equal(Terrain.Floor, sim.Grid.TerrainAt(new Cell(3, 2)));
    }

    [Fact]
    public void TheWall_StandsAcrossTheDirectionItIsAimedIn()
    {
        var sim = Simulation(new Grid(7, 5), CasterAt(0, 0, IceWall));
        IReadOnlyList<Cell> Aimed(int x, int y) =>
            sim.PreviewAction(Caster, new Cell(0, 0), new SkillUse("raise", null, new Cell(x, y))).Cells!;

        // Visé en diagonale : le mur est sur l'autre diagonale. Visé vers le bas : il est horizontal (une case hors de la grille).
        Assert.Equal(new[] { new Cell(2, 2), new Cell(1, 3), new Cell(3, 1) }, Aimed(2, 2));
        Assert.Equal(new[] { new Cell(0, 3), new Cell(1, 3) }, Aimed(0, 3));
        Assert.Equal(new[] { new Cell(3, 0), new Cell(3, 1) }, Aimed(3, 0));
    }

    [Fact]
    public void TheWall_DoesNotGoOnBehindARealWall()
    {
        var wide = IceWall with { Zone = new ZoneDefinition { Shape = ZoneShape.Wall, Size = 2, ThroughWalls = false } };
        var sim = Simulation(Grid.FromRows("...", "...", "...", ".#.", "..."), CasterAt(0, 2, wide));

        var cells = sim.PreviewAction(Caster, new Cell(0, 2), new SkillUse("raise", null, new Cell(1, 2))).Cells!;

        // Vers le haut : deux blocs ; vers le bas : le mur du décor en (1,3), rien derrière.
        Assert.Equal(new[] { new Cell(1, 2), new Cell(1, 1), new Cell(1, 0) }, cells);
    }

    [Fact]
    public void TheAimedCell_MustBeFree_InRange_AndInSight()
    {
        var sim = Simulation(Grid.FromRows(".......", "...#...", "......."), CasterAt(0, 1, IceWall), Unit(2, team: 1, 2, 1));

        Assert.Equal(RejectionReason.InvalidDestination, sim.Check(Raise(2, 1)));
        Assert.Equal(RejectionReason.OutOfRange, sim.Check(Raise(5, 1)));
        Assert.Equal(RejectionReason.NoLineOfSight, sim.Check(Raise(4, 1)));
        Assert.Null(sim.Check(Raise(2, 0)));
    }

    [Fact]
    public void ABlock_CoversWithItsOwnHp_AndBreaksBeforeMelting()
    {
        // Le tireur ennemi vise l'allié 3, abrité derrière le bloc dressé en (3,2).
        var sim = Simulation(new Grid(7, 5),
            CasterAt(0, 2, IceWall), Unit(3, team: 0, 2, 2), Unit(2, team: 1, 6, 2, gauge: Capacity, skills: new[] { Shot }));
        sim.Submit(Raise(3, 2));
        sim.Tick();

        sim.Submit(Use(Enemy, "shot", new UnitId(3)));
        var events = sim.Tick();

        // La moitié de 10, soit tout ce que le bloc (5 PV) peut encaisser : il casse.
        Assert.Contains(new HitCovered(2, new UnitId(3), new Cell(3, 2), 5, 0), events);
        Assert.Contains(new CoverDestroyed(2, new Cell(3, 2)), events);
        Assert.Equal(95, sim.Get(3).Hp);
        Assert.Equal(Terrain.Floor, sim.Grid.TerrainAt(new Cell(3, 2)));

        var later = sim.TickTimes(25);

        Assert.DoesNotContain(new Cell(3, 2), later.OfType<ObstacleMelted>().Select(e => e.Cell));
        Assert.Equal(2, later.OfType<ObstacleMelted>().Count()); // les deux autres blocs
    }

    [Fact]
    public void APillar_BlocksSightToo_UntilItMelts()
    {
        var sim = Simulation(new Grid(7, 1), CasterAt(0, 0, IcePillar, Shot), Unit(2, team: 1, 6, 0));
        sim.Submit(Raise(3, 0));
        sim.Tick();

        Assert.Equal(Terrain.Wall, sim.Grid.TerrainAt(new Cell(3, 0)));
        Assert.Equal(RejectionReason.NoLineOfSight, sim.Check(Use(Caster, "shot", Enemy), ignoreTiming: true));

        sim.TickTimes(20);

        Assert.Null(sim.Check(Use(Caster, "shot", Enemy), ignoreTiming: true));
    }

    [Fact]
    public void RaisedObstacles_ReplayIdentically()
    {
        var setup = Setup(new Grid(7, 5), CasterAt(0, 2, IceWall), Unit(2, team: 1, 6, 2));
        var sim = new CombatSimulation(setup);
        sim.Submit(Raise(3, 2));
        sim.TickTimes(30);

        var replay = CombatSimulation.Replay(setup, sim.CommandLog, 30);

        Assert.Equal(sim.Checksum(), replay.Checksum());
        Assert.Equal(sim.EventLog, replay.EventLog);
    }

    [Theory]
    // Rien d'autre qu'un obstacle ; un obstacle bas a des PV, pas un mur ; une durée ; un terrain qui en est un.
    [InlineData(""" "minDamage": 5, "maxDamage": 5, "raises": { "terrain": "lowObstacle", "hp": 5, "durationMs": 1000 } """)]
    [InlineData(""" "minDamage": 0, "maxDamage": 0, "raises": { "terrain": "lowObstacle", "durationMs": 1000 } """)]
    [InlineData(""" "minDamage": 0, "maxDamage": 0, "raises": { "terrain": "wall", "hp": 5, "durationMs": 1000 } """)]
    [InlineData(""" "minDamage": 0, "maxDamage": 0, "raises": { "terrain": "wall", "durationMs": 0 } """)]
    [InlineData(""" "minDamage": 0, "maxDamage": 0, "raises": { "terrain": "floor", "durationMs": 1000 } """)]
    [InlineData(""" "minDamage": 0, "maxDamage": 0, "chargeMs": 500, "raises": { "terrain": "wall", "durationMs": 1000 } """)]
    public void InvalidRaisingSkills_AreRejectedAtLoad(string fields)
    {
        Assert.Throws<ArgumentException>(() => SkillDefinition.ListFromJson(
            $$"""[ { "id": "x", "weight": "light", "cooldownMs": 0, "range": 3, "zone": { "shape": "square", "size": 0, "throughWalls": false }, {{fields}} } ]"""));
    }
}
