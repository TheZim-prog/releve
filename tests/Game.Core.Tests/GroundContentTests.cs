using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>
/// Les compétences du contenu réel qui laissent quelque chose sur le terrain (Fumigène, Piège, Mur de glace…) : leurs
/// données font ce que le document de conception décrit. Terrain vide de 9 × 5, le lanceur est l'unité 1.
/// </summary>
public class GroundContentTests
{
    private static RejectionReason? CanShoot(CombatSimulation sim, int shooter, int target)
    {
        var unit = sim.FindUnit(new UnitId(shooter))!;
        return sim.Check(new ActivateCommand(unit.Id, null, new SkillUse(unit.BasicAttack!.Id, new UnitId(target))), ignoreTiming: true);
    }

    [Fact]
    public void TheSmokeBomb_HidesBothSides_AndTheWhirlwindClearsIt()
    {
        var sim = Fight("fighter", "smoke-bomb", (1, 2), ("gunner", 1, 5, 2));
        Assert.Null(CanShoot(sim, 2, 1));

        Use(sim, "smoke-bomb", cell: (3, 2));

        Assert.Equal(RejectionReason.NoLineOfSight, CanShoot(sim, 2, 1));
        Assert.True(Content.Skills["whirlwind"].ClearsSmoke);
        Assert.False(Content.Skills["wind-wall"].ClearsSmoke);
    }

    [Fact]
    public void TheWindWall_StopsTheGunner_NotTheTelepath()
    {
        var sim = Fight("speedster", "wind-wall", (1, 2), ("gunner", 1, 5, 2), ("telepath", 0, 1, 3));

        var events = Use(sim, "wind-wall", cell: (3, 2));

        Assert.Empty(Affected(events, StatusKind.OffBalance)); // il ne déséquilibre pas, lui
        Assert.Equal(RejectionReason.NoLineOfSight, CanShoot(sim, 2, 3));
        var paralysis = Content.Skills["paralysis"];
        Assert.True(paralysis.IsMental); // un pouvoir mental passerait
    }

    [Theory]
    [InlineData("fighter", "tear-gas")]
    [InlineData("energetic", "black-ice")]
    public void ItSlowsEveryoneInItsZone_AlliesIncluded_WhileTheyStay(string caster, string skill)
    {
        var sim = Fight(caster, skill, (0, 2), ("brawler", 1, 4, 2), ("colossus", 0, 4, 3), ("brawler", 1, 8, 0));

        var events = Use(sim, skill, cell: (4, 2));

        Assert.Equal(new[] { 2, 3 }, Affected(events, StatusKind.OffBalance));
        for (var i = 0; i < Ticks(Content.Skills[skill].Ground!.DurationMs) - 1; i++)
            sim.Tick();
        Assert.True(sim.FindUnit(new UnitId(2))!.HasStatus(StatusKind.OffBalance));
    }

    [Fact]
    public void Frostbite_ImmobilizesInASmallerZoneThanBlackIce()
    {
        var sim = Fight("energetic", "frostbite", (0, 2), ("brawler", 1, 4, 2), ("brawler", 1, 6, 2));

        var events = Use(sim, "frostbite", cell: (4, 2));

        Assert.Equal(new[] { 2 }, Affected(events, StatusKind.Immobilized));
        Assert.True(Content.Skills["frostbite"].Zone!.Size < Content.Skills["black-ice"].Zone!.Size);
        Assert.False(Content.Skills["frostbite"].Ground!.Renews);
    }

    [Fact]
    public void TheTrap_Immobilizes_AndTheExplosiveOneBlastsInstead_BothInTwoTrees()
    {
        var trap = Content.Skills["trap"].Ground!;
        var explosive = Content.Skills["explosive-trap"].Ground!;

        Assert.True(trap is { IsTrap: true, DealsDamage: true, Blast: null });
        Assert.Contains(trap.Effects, e => e.Status == StatusKind.Immobilized);
        Assert.True(explosive is { IsTrap: true, DealsDamage: true, Blast: not null, Effects.Count: 0 });

        var sim = Fight("tech", "trap", (0, 2));
        Use(sim, "trap", cell: (3, 2));
        Assert.Null(Assert.Single(sim.GroundZones).EndTick);
    }

    [Fact]
    public void TheIceWall_RaisesThreeLowBlocks_AndThePillarOneWall_ThatLastsLonger()
    {
        var wall = Fight("energetic", "ice-wall", (0, 2));
        var pillar = Fight("energetic", "ice-pillar", (0, 2));

        var blocks = Use(wall, "ice-wall", cell: (4, 2)).OfType<ObstacleRaised>().ToList();
        var single = Use(pillar, "ice-pillar", cell: (4, 2)).OfType<ObstacleRaised>().Single();

        Assert.Equal(new[] { new Cell(4, 2), new Cell(4, 3), new Cell(4, 1) }, blocks.Select(b => b.Cell));
        Assert.All(blocks, b => Assert.Equal(Terrain.LowObstacle, b.Terrain));
        Assert.Equal(Terrain.Wall, single.Terrain);
        Assert.True(single.EndTick > blocks[0].EndTick);
        Assert.True(Content.Skills["ice-wall"].Raises!.Hp < Rules.LowObstacleHp); // fragiles
    }
}
