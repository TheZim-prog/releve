using Game.Core.Combat;
using Game.Core.Grids;

namespace Game.Core.Tests.Combat;

public class ZoneDefinitionTests
{
    private static readonly ZoneDefinition Square1 = new() { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false };
    private static readonly ZoneDefinition Line3 = new() { Shape = ZoneShape.Line, Size = 3, ThroughWalls = false };

    [Fact]
    public void Square_ExcludesWallsAndOutsideCells()
    {
        var grid = Grid.FromRows(
            ".#.",
            "...");

        var cells = (Square1 with { ThroughWalls = true }).Cells(grid, new Cell(0, 0), new Cell(0, 0));

        Assert.Equal(new[] { new Cell(0, 0), new Cell(0, 1), new Cell(1, 1) }, cells);
    }

    [Fact]
    public void Square_WithoutThroughWalls_OnlyCoversCellsVisibleFromItsCenter()
    {
        // Centre (1,1) : le mur (2,1) cache (3,1), et aussi (2,0) et (2,2), dont la diagonale depuis le centre
        // frôle le mur (pas de coupe d'angle).
        var grid = Grid.FromRows(
            ".....",
            "..#..",
            ".....");
        var radius2 = Square1 with { Size = 2 };

        var hidden = radius2.Cells(grid, new Cell(0, 0), new Cell(1, 1))!;
        var through = (radius2 with { ThroughWalls = true }).Cells(grid, new Cell(0, 0), new Cell(1, 1))!;

        Assert.DoesNotContain(new Cell(3, 1), hidden);
        Assert.DoesNotContain(new Cell(2, 0), hidden);
        Assert.Contains(new Cell(1, 0), hidden);
        Assert.Contains(new Cell(3, 1), through);
        Assert.Contains(new Cell(2, 0), through);
        Assert.DoesNotContain(new Cell(2, 1), through); // la case de mur elle-même
    }

    [Fact]
    public void Square_CenteredOnAWall_IsInvalid()
    {
        Assert.Null(Square1.Cells(Grid.FromRows(".#."), new Cell(0, 0), new Cell(1, 0)));
    }

    [Fact]
    public void Line_ThroughWalls_SkipsWallCellsAndGoesOn()
    {
        var beam = Line3 with { Size = 4, ThroughWalls = true };

        Assert.Equal(
            new[] { new Cell(1, 0), new Cell(3, 0), new Cell(4, 0) },
            beam.Cells(Grid.FromRows("..#...."), new Cell(0, 0), new Cell(1, 0)));
    }

    [Fact]
    public void Square_OfSizeZero_IsTheTargetCell()
    {
        var zone = Square1 with { Size = 0 };

        Assert.Equal(new[] { new Cell(2, 2) }, zone.Cells(new Grid(5, 5), new Cell(0, 0), new Cell(2, 2)));
    }

    [Fact]
    public void Line_GoesInTheTargetsDirection_ForItsLength()
    {
        var grid = new Grid(6, 6);

        Assert.Equal(
            new[] { new Cell(2, 1), new Cell(3, 1), new Cell(4, 1) },
            Line3.Cells(grid, new Cell(1, 1), new Cell(2, 1)));
        Assert.Equal(
            new[] { new Cell(2, 2), new Cell(3, 3), new Cell(4, 4) },
            Line3.Cells(grid, new Cell(1, 1), new Cell(5, 5)));
    }

    [Fact]
    public void Line_StopsAtWalls_WithoutCuttingCorners()
    {
        Assert.Equal(
            new[] { new Cell(1, 0), new Cell(2, 0) },
            Line3.Cells(Grid.FromRows("...#.."), new Cell(0, 0), new Cell(1, 0)));

        // Diagonale depuis (0,0) : le mur (1,0) frôlé arrête la vague dès le premier pas.
        Assert.Empty(Line3.Cells(Grid.FromRows(".#..", "....", "....", "...."), new Cell(0, 0), new Cell(1, 1))!);
    }

    [Fact]
    public void Line_NeedsAnAlignedTargetOtherThanTheCaster()
    {
        var grid = new Grid(6, 6);

        Assert.Null(Line3.Cells(grid, new Cell(0, 0), new Cell(2, 1)));
        Assert.Null(Line3.Cells(grid, new Cell(0, 0), new Cell(0, 0)));
    }
}
