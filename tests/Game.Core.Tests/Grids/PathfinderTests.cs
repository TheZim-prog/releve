using Game.Core.Grids;

namespace Game.Core.Tests.Grids;

public class PathfinderTests
{
    [Fact]
    public void FindPath_SameCell_IsEmpty()
    {
        var grid = new Grid(3, 3);

        Assert.Empty(Pathfinder.FindPath(grid, new Cell(1, 1), new Cell(1, 1))!);
    }

    [Fact]
    public void FindPath_OpenGround_UsesDiagonals()
    {
        var grid = new Grid(5, 5);

        var path = Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(4, 2));

        Assert.NotNull(path);
        Assert.Equal(4, path.Count);
        Assert.Equal(new Cell(4, 2), path[^1]);
    }

    [Fact]
    public void FindPath_GoesAroundWall()
    {
        var grid = Grid.FromRows(
            ".....",
            "####.",
            ".....");

        var path = Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(0, 2));

        Assert.NotNull(path);
        // Les diagonales (3,0)→(4,1) et (4,1)→(3,2) frôlent le mur (3,1) : interdites.
        // Le seul chemin passe donc par (4,0) et (4,2).
        Assert.Equal(
            new[]
            {
                new Cell(1, 0), new Cell(2, 0), new Cell(3, 0), new Cell(4, 0),
                new Cell(4, 1), new Cell(4, 2),
                new Cell(3, 2), new Cell(2, 2), new Cell(1, 2), new Cell(0, 2),
            },
            path);
    }

    [Fact]
    public void FindPath_CornerCuttingForcesDetour()
    {
        // Sans la règle, (0,1) → (1,0) serait 1 pas ; avec elle, il faut contourner par (1,1).
        var grid = Grid.FromRows(
            "#..",
            "...");

        var path = Pathfinder.FindPath(grid, new Cell(0, 1), new Cell(1, 0));

        Assert.Equal(new[] { new Cell(1, 1), new Cell(1, 0) }, path);
    }

    [Fact]
    public void FindPath_DiagonalWall_IsNotCrossable()
    {
        var grid = Grid.FromRows(
            "..#",
            ".#.",
            "#..");

        Assert.Null(Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(2, 2)));
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenUnreachableOrBlocked()
    {
        var grid = Grid.FromRows(
            "..#..",
            "..#..",
            "..#..");

        Assert.Null(Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(4, 0)));
        Assert.Null(Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(2, 0)));
        Assert.Null(Pathfinder.FindPath(grid, new Cell(2, 0), new Cell(0, 0)));
        Assert.Null(Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(-1, 0)));
    }

    [Fact]
    public void FindPath_RespectsMaxSteps()
    {
        var grid = new Grid(10, 1);

        Assert.NotNull(Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(3, 0), maxSteps: 3));
        Assert.Null(Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(4, 0), maxSteps: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pathfinder.FindPath(grid, new Cell(0, 0), new Cell(1, 0), maxSteps: -1));
    }

    [Fact]
    public void FindPath_GoesThroughAlly()
    {
        var map = TestMap.Parse(
            "###",
            ".A.",
            "###");

        var path = Pathfinder.FindPath(map.Grid, new Cell(0, 1), new Cell(2, 1), occupantAt: map.OccupantAt);

        Assert.Equal(new[] { new Cell(1, 1), new Cell(2, 1) }, path);
    }

    [Fact]
    public void FindPath_CannotStopOnAllyOrEnemy()
    {
        var map = TestMap.Parse(".AE");

        Assert.Null(Pathfinder.FindPath(map.Grid, new Cell(0, 0), new Cell(1, 0), occupantAt: map.OccupantAt));
        Assert.Null(Pathfinder.FindPath(map.Grid, new Cell(0, 0), new Cell(2, 0), occupantAt: map.OccupantAt));
    }

    [Fact]
    public void FindPath_EnemyBlocksCorridor()
    {
        var map = TestMap.Parse(
            "###",
            ".E.",
            "###");

        Assert.Null(Pathfinder.FindPath(map.Grid, new Cell(0, 1), new Cell(2, 1), occupantAt: map.OccupantAt));
    }

    [Fact]
    public void FindPath_TwoEnemiesOnADiagonal_FormABarrier_TwoAlliesDoNot()
    {
        var enemies = TestMap.Parse(
            ".E",
            "E.");
        var allies = TestMap.Parse(
            ".A",
            "A.");

        Assert.Null(Pathfinder.FindPath(enemies.Grid, new Cell(0, 0), new Cell(1, 1), occupantAt: enemies.OccupantAt));
        Assert.Equal(
            new[] { new Cell(1, 1) },
            Pathfinder.FindPath(allies.Grid, new Cell(0, 0), new Cell(1, 1), occupantAt: allies.OccupantAt));
    }

    [Fact]
    public void FindPath_DetoursAroundEnemy()
    {
        var map = TestMap.Parse(
            "...",
            ".E.",
            "...");

        var path = Pathfinder.FindPath(map.Grid, new Cell(0, 1), new Cell(2, 1), occupantAt: map.OccupantAt);

        // Les diagonales (0,1)→(1,0) et (1,0)→(2,1) frôlent l'ennemi : il faut passer par un coin.
        Assert.NotNull(path);
        Assert.Equal(4, path.Count);
        Assert.Equal(new Cell(2, 1), path[^1]);
        Assert.DoesNotContain(new Cell(1, 1), path);
    }

    [Fact]
    public void ReachableCells_TraversesAllies_ButExcludesTheirCells()
    {
        var map = TestMap.Parse("AA..E.");

        // Départ en (0,0) : l'unité elle-même, que l'appelant peut voir comme un allié.
        var reachable = Pathfinder.ReachableCells(map.Grid, new Cell(0, 0), maxSteps: 10, occupantAt: map.OccupantAt);

        Assert.Equal(
            new Dictionary<Cell, int>
            {
                [new Cell(0, 0)] = 0,
                [new Cell(2, 0)] = 2,
                [new Cell(3, 0)] = 3,
            },
            reachable);
    }

    [Fact]
    public void ReachableCells_OpenGround_IsASquare()
    {
        var grid = new Grid(9, 9);
        var center = new Cell(4, 4);

        var reachable = Pathfinder.ReachableCells(grid, center, maxSteps: 2);

        Assert.Equal(25, reachable.Count);
        Assert.Equal(0, reachable[center]);
        Assert.Equal(2, reachable[new Cell(6, 6)]);
        Assert.Equal(1, reachable[new Cell(3, 5)]);
        Assert.False(reachable.ContainsKey(new Cell(7, 4)));
    }

    [Fact]
    public void ReachableCells_RespectsWallsAndCornerCutting()
    {
        var grid = Grid.FromRows(
            ".....",
            ".##..",
            ".....");

        var reachable = Pathfinder.ReachableCells(grid, new Cell(0, 2), maxSteps: 2);

        // La diagonale (0,1)→(1,0) frôle le mur (1,1) : interdite. (1,0) est donc à 3 pas, hors de portée.
        Assert.Equal(
            new Dictionary<Cell, int>
            {
                [new Cell(0, 2)] = 0,
                [new Cell(0, 1)] = 1,
                [new Cell(1, 2)] = 1,
                [new Cell(0, 0)] = 2,
                [new Cell(2, 2)] = 2,
            },
            reachable);
    }

    [Fact]
    public void ReachableCells_EnumeratesByIncreasingDistance_InNeighborOrder()
    {
        var grid = new Grid(5, 5);

        var reachable = Pathfinder.ReachableCells(grid, new Cell(2, 2), maxSteps: 2);

        // Distance 1 dans l'ordre de Grid.Neighbors : haut, droite, bas, gauche, puis diagonales.
        Assert.Equal(
            new[]
            {
                new Cell(2, 2),
                new Cell(2, 1), new Cell(3, 2), new Cell(2, 3), new Cell(1, 2),
                new Cell(3, 1), new Cell(3, 3), new Cell(1, 3), new Cell(1, 1),
            },
            reachable.Keys.Take(9));
        Assert.Equal(reachable.Values.Order(), reachable.Values);
    }

    [Fact]
    public void ReachableCells_FromBlockedCell_IsEmpty()
    {
        var grid = Grid.FromRows("#.");

        Assert.Empty(Pathfinder.ReachableCells(grid, new Cell(0, 0), maxSteps: 3));
    }

    [Fact]
    public void FindPath_IsDeterministic()
    {
        var grid = RandomGrid(seed: 42, width: 12, height: 12);
        var from = new Cell(0, 0);
        var to = new Cell(11, 11);
        grid.SetBlocked(from, false);
        grid.SetBlocked(to, false);

        var first = Pathfinder.FindPath(grid, from, to);
        for (var i = 0; i < 5; i++)
            Assert.Equal(first, Pathfinder.FindPath(grid, from, to));
    }

    // Vérifie sur des grilles aléatoires, avec alliés et ennemis, que les chemins sont valides
    // (aucun pas illégal, arrivée sur une case libre) et optimaux, en les comparant à un calcul
    // de distance indépendant (relaxation naïve).
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void FindPath_OnRandomGrids_IsValidAndShortest(int seed)
    {
        var random = new Random(seed);
        var grid = RandomGrid(random, width: 8, height: 8);
        var occupants = new Dictionary<Cell, Occupant>();
        foreach (var cell in AllCells(grid).Where(grid.IsWalkable))
        {
            var roll = random.Next(100);
            if (roll < 10)
                occupants[cell] = Occupant.Ally;
            else if (roll < 20)
                occupants[cell] = Occupant.Enemy;
        }
        Occupant OccupantAt(Cell cell) => occupants.GetValueOrDefault(cell);

        var cells = AllCells(grid).Where(grid.IsWalkable).ToList();
        var from = cells.First(c => OccupantAt(c) == Occupant.None);
        var reference = ReferenceDistances(grid, from, OccupantAt);
        var reachable = Pathfinder.ReachableCells(grid, from, maxSteps: int.MaxValue, OccupantAt);

        foreach (var to in cells)
        {
            var path = Pathfinder.FindPath(grid, from, to, occupantAt: OccupantAt);
            var canStop = to == from || OccupantAt(to) == Occupant.None;

            if (!canStop || !reference.TryGetValue(to, out var expected))
            {
                Assert.Null(path);
                Assert.False(reachable.ContainsKey(to));
                continue;
            }

            Assert.NotNull(path);
            Assert.Equal(expected, path.Count);
            Assert.Equal(expected, reachable[to]);

            var previous = from;
            foreach (var step in path)
            {
                Assert.True(grid.CanStep(previous, step, OccupantAt), $"Illegal step {previous} → {step}");
                previous = step;
            }
            Assert.Equal(to, previous);
        }
    }

    private static Grid RandomGrid(int seed, int width, int height) =>
        RandomGrid(new Random(seed), width, height);

    private static Grid RandomGrid(Random random, int width, int height)
    {
        var grid = new Grid(width, height);
        foreach (var cell in AllCells(grid))
            grid.SetBlocked(cell, random.Next(100) < 30);
        return grid;
    }

    private static IEnumerable<Cell> AllCells(Grid grid)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                yield return new Cell(x, y);
    }

    private static Dictionary<Cell, int> ReferenceDistances(Grid grid, Cell from, Func<Cell, Occupant> occupantAt)
    {
        var distances = new Dictionary<Cell, int> { [from] = 0 };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var a in AllCells(grid))
            {
                if (!distances.TryGetValue(a, out var da))
                    continue;
                foreach (var b in AllCells(grid))
                {
                    if (!grid.CanStep(a, b, occupantAt))
                        continue;
                    if (!distances.TryGetValue(b, out var db) || da + 1 < db)
                    {
                        distances[b] = da + 1;
                        changed = true;
                    }
                }
            }
        }
        return distances;
    }
}
