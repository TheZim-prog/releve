using Game.Core.Grids;

namespace Game.Core.Tests.Grids;

public class GridTests
{
    [Fact]
    public void FromRows_ParsesSizeAndObstacles()
    {
        var grid = Grid.FromRows(
            "..#",
            "#..");

        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.True(grid.IsBlocked(new Cell(2, 0)));
        Assert.True(grid.IsBlocked(new Cell(0, 1)));
        Assert.True(grid.IsWalkable(new Cell(0, 0)));
        Assert.True(grid.IsWalkable(new Cell(2, 1)));
    }

    [Fact]
    public void FromRows_RejectsRaggedRowsAndUnknownCharacters()
    {
        Assert.Throws<ArgumentException>(() => Grid.FromRows("...", ".."));
        Assert.Throws<ArgumentException>(() => Grid.FromRows("..x"));
        Assert.Throws<ArgumentException>(() => Grid.FromRows());
        Assert.Throws<ArgumentException>(() => Grid.FromRows("..", null!));
    }

    [Fact]
    public void Constructor_RejectsSizesThatOverflow()
    {
        Assert.Throws<OverflowException>(() => new Grid(65536, 65537));
    }

    [Fact]
    public void LowObstacle_BlocksMovementAndCorners_ButNotSight()
    {
        var grid = Grid.FromRows(
            ".o.",
            "...");

        Assert.Equal(Terrain.LowObstacle, grid.TerrainAt(new Cell(1, 0)));
        Assert.True(grid.IsBlocked(new Cell(1, 0)));
        Assert.False(grid.BlocksSight(new Cell(1, 0)));
        Assert.False(grid.CanStep(new Cell(0, 0), new Cell(1, 0)));
        Assert.False(grid.CanStep(new Cell(0, 1), new Cell(1, 0)));  // sur l'obstacle
        Assert.False(grid.CanStep(new Cell(0, 0), new Cell(1, 1)));  // diagonale qui frôle l'obstacle
        Assert.True(LineOfSight.IsClear(grid, new Cell(0, 0), new Cell(2, 0)));
        Assert.True(grid.BlocksSight(new Cell(-1, 0)));             // hors de la grille : comme un mur
    }

    [Fact]
    public void OutsideCells_AreBlocked()
    {
        var grid = new Grid(3, 3);

        Assert.True(grid.IsBlocked(new Cell(-1, 0)));
        Assert.True(grid.IsBlocked(new Cell(0, 3)));
        Assert.False(grid.Contains(new Cell(3, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SetBlocked(new Cell(3, 0), true));
    }

    [Fact]
    public void CanStep_OrthogonalAndDiagonal_OnOpenGround()
    {
        var grid = new Grid(3, 3);
        var center = new Cell(1, 1);

        Assert.True(grid.CanStep(center, new Cell(1, 0)));
        Assert.True(grid.CanStep(center, new Cell(0, 1)));
        Assert.True(grid.CanStep(center, new Cell(2, 2)));
        Assert.True(grid.CanStep(center, new Cell(0, 0)));
    }

    [Fact]
    public void CanStep_RejectsNonAdjacentSelfBlockedAndOutside()
    {
        var grid = Grid.FromRows(
            "...",
            ".#.",
            "...");

        Assert.False(grid.CanStep(new Cell(0, 0), new Cell(2, 0)));
        Assert.False(grid.CanStep(new Cell(0, 0), new Cell(0, 0)));
        Assert.False(grid.CanStep(new Cell(1, 0), new Cell(1, 1)));
        Assert.False(grid.CanStep(new Cell(0, 0), new Cell(-1, 0)));
    }

    // Exemple du document de conception : obstacle sur a2 ou b1 → impossible d'aller de a1 à b2 directement.
    // Ici a1 = (0,1) et b2 = (1,0) ; la diagonale frôle (0,0) et (1,1).
    [Theory]
    [InlineData("#.", "..")] // une seule case frôlée bloquée
    [InlineData("..", ".#")] // l'autre case frôlée bloquée
    [InlineData("#.", ".#")] // mur en diagonale : on ne passe pas « entre »
    public void CanStep_Diagonal_ForbidsCornerCutting(string row0, string row1)
    {
        var grid = Grid.FromRows(row0, row1);
        var a1 = new Cell(0, 1);
        var b2 = new Cell(1, 0);

        Assert.False(grid.CanStep(a1, b2));
        Assert.False(grid.CanStep(b2, a1));
    }

    [Fact]
    public void CanStep_IntoAllyCell_IsAllowed_IntoEnemyCell_IsNot()
    {
        var map = TestMap.Parse(".AE");

        Assert.True(map.Grid.CanStep(new Cell(0, 0), new Cell(1, 0), map.OccupantAt));
        Assert.False(map.Grid.CanStep(new Cell(1, 0), new Cell(2, 0), map.OccupantAt));
    }

    [Fact]
    public void CanStep_Diagonal_EnemyBlocksCornerLikeAWall_AllyDoesNot()
    {
        // a1 = (0,1), b2 = (1,0) ; la diagonale frôle (0,0).
        var withEnemy = TestMap.Parse("E.", "..");
        var withAlly = TestMap.Parse("A.", "..");

        Assert.False(withEnemy.Grid.CanStep(new Cell(0, 1), new Cell(1, 0), withEnemy.OccupantAt));
        Assert.True(withAlly.Grid.CanStep(new Cell(0, 1), new Cell(1, 0), withAlly.OccupantAt));
    }

    [Fact]
    public void Neighbors_FollowsFixedOrder_AndSkipsIllegalSteps()
    {
        var open = new Grid(3, 3);
        Assert.Equal(
            new[]
            {
                new Cell(1, 0), new Cell(2, 1), new Cell(1, 2), new Cell(0, 1),
                new Cell(2, 0), new Cell(2, 2), new Cell(0, 2), new Cell(0, 0),
            },
            open.Neighbors(new Cell(1, 1)));

        var walled = Grid.FromRows(
            "...",
            "..#",
            "...");
        // (2,1) bloquée : on perd la case elle-même et les deux diagonales qui la frôlent.
        Assert.Equal(
            new[] { new Cell(1, 0), new Cell(1, 2), new Cell(0, 1), new Cell(0, 2), new Cell(0, 0) },
            walled.Neighbors(new Cell(1, 1)));
    }
}
