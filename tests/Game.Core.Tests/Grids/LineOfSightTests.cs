using Game.Core.Grids;

namespace Game.Core.Tests.Grids;

public class LineOfSightTests
{
    [Fact]
    public void CrossedCells_StraightLine_ExcludesEndpoints()
    {
        Assert.Equal(
            new[] { new Cell(1, 0), new Cell(2, 0), new Cell(3, 0) },
            LineOfSight.CrossedCells(new Cell(0, 0), new Cell(4, 0)));
        Assert.Empty(LineOfSight.CrossedCells(new Cell(2, 2), new Cell(2, 2)));
        Assert.Empty(LineOfSight.CrossedCells(new Cell(2, 2), new Cell(3, 2)));
    }

    [Fact]
    public void CrossedCells_ThroughACorner_IncludesBothCornerCells()
    {
        // De (0.5, 0.5) à (3.5, 1.5) : la pente 1/3 passe exactement par le coin (2, 1).
        Assert.Equal(
            new[] { new Cell(1, 0), new Cell(2, 0), new Cell(1, 1), new Cell(2, 1) },
            LineOfSight.CrossedCells(new Cell(0, 0), new Cell(3, 1)));
    }

    [Fact]
    public void CrossedCells_PureDiagonal_IncludesEveryCornerPair()
    {
        Assert.Equal(
            new[] { new Cell(1, 0), new Cell(0, 1), new Cell(1, 1), new Cell(2, 1), new Cell(1, 2) },
            LineOfSight.CrossedCells(new Cell(0, 0), new Cell(2, 2)));
    }

    [Fact]
    public void CrossedCells_ShallowSlope_WithoutCorner()
    {
        // De (0.5, 0.5) à (4.5, 1.5) : on change de ligne au milieu, sans passer par un coin.
        Assert.Equal(
            new[] { new Cell(1, 0), new Cell(2, 0), new Cell(2, 1), new Cell(3, 1) },
            LineOfSight.CrossedCells(new Cell(0, 0), new Cell(4, 1)));
    }

    [Fact]
    public void IsClear_BlockedByWallInBetween_NotByWallsOnTheSide()
    {
        var grid = Grid.FromRows(
            "..#..",
            ".....",
            "#####");

        Assert.False(LineOfSight.IsClear(grid, new Cell(0, 0), new Cell(4, 0)));
        Assert.True(LineOfSight.IsClear(grid, new Cell(0, 1), new Cell(4, 1)));
    }

    [Fact]
    public void IsClear_NoCornerCutting_OneCornerCellIsEnough()
    {
        // Le segment (0,0) → (2,2) frôle les coins ; un seul mur sur l'une des cases de coin bloque.
        var grid = Grid.FromRows(
            "...",
            "..#",
            "...");

        Assert.False(LineOfSight.IsClear(grid, new Cell(0, 0), new Cell(2, 2)));
    }

    [Fact]
    public void IsClear_UsesTheExtraBlocker_ButNotOnEndpoints()
    {
        var grid = new Grid(5, 1);
        var from = new Cell(0, 0);
        var to = new Cell(4, 0);

        Assert.True(LineOfSight.IsClear(grid, from, to));
        Assert.False(LineOfSight.IsClear(grid, from, to, cell => cell == new Cell(2, 0)));
        Assert.True(LineOfSight.IsClear(grid, from, to, cell => cell == to || cell == from));
    }

    [Fact]
    public void IsClear_IsFalse_FromOrToABlockedOrOutsideCell()
    {
        var grid = Grid.FromRows("#...#");

        Assert.False(LineOfSight.IsClear(grid, new Cell(0, 0), new Cell(2, 0)));
        Assert.False(LineOfSight.IsClear(grid, new Cell(2, 0), new Cell(4, 0)));
        Assert.False(LineOfSight.IsClear(grid, new Cell(-1, 0), new Cell(2, 0)));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void IsClear_IsSymmetric(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        var grid = new Grid(9, 9);
        for (var y = 0; y < 9; y++)
            for (var x = 0; x < 9; x++)
                grid.SetBlocked(new Cell(x, y), random.NextInt(0, 99) < 20);

        for (var i = 0; i < 300; i++)
        {
            var a = new Cell(random.NextInt(0, 8), random.NextInt(0, 8));
            var b = new Cell(random.NextInt(0, 8), random.NextInt(0, 8));

            Assert.Equal(LineOfSight.IsClear(grid, a, b), LineOfSight.IsClear(grid, b, a));
            Assert.Equal(
                LineOfSight.CrossedCells(a, b).ToHashSet(),
                LineOfSight.CrossedCells(b, a).ToHashSet());
        }
    }
}
