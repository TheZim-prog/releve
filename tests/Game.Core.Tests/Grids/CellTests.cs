using Game.Core.Grids;

namespace Game.Core.Tests.Grids;

public class CellTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0, 0, 1, 0, 1)]
    [InlineData(0, 0, 1, 1, 1)] // la diagonale coûte 1
    [InlineData(0, 0, 3, 2, 3)]
    [InlineData(5, 5, 2, 1, 4)]
    public void DistanceTo_CountsDiagonalAsOne(int x1, int y1, int x2, int y2, int expected)
    {
        var a = new Cell(x1, y1);
        var b = new Cell(x2, y2);

        Assert.Equal(expected, a.DistanceTo(b));
        Assert.Equal(expected, b.DistanceTo(a));
    }

    [Fact]
    public void IsAdjacentTo_IncludesDiagonals_ExcludesSelfAndFarCells()
    {
        var c = new Cell(2, 2);

        Assert.True(c.IsAdjacentTo(new Cell(2, 1)));
        Assert.True(c.IsAdjacentTo(new Cell(3, 3)));
        Assert.False(c.IsAdjacentTo(c));
        Assert.False(c.IsAdjacentTo(new Cell(4, 2)));
    }

    [Fact]
    public void IsDiagonalTo_OnlyForDiagonalNeighbors()
    {
        var c = new Cell(2, 2);

        Assert.True(c.IsDiagonalTo(new Cell(1, 1)));
        Assert.False(c.IsDiagonalTo(new Cell(2, 1)));
        Assert.False(c.IsDiagonalTo(new Cell(4, 4)));
        Assert.False(c.IsDiagonalTo(c));
    }
}
