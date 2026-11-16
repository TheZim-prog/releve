using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>Les invocations ajoutées au contenu réel (leurres, drone, Astre, tourelle de suppression). Terrain vide de 9 × 5.</summary>
public class SummonContentTests
{
    [Fact]
    public void Afterimage_LeavesALureBehind_ThatTheNearbyBrawlersTarget()
    {
        var sim = FightAgainstAi("speedster", "afterimage", (2, 2), ("brawler", 1, 4, 2), ("brawler", 1, 8, 4));

        var events = Use(sim, "afterimage", cell: (0, 0));

        var lure = events.OfType<UnitSummoned>().Single();
        Assert.Equal((new Cell(2, 2), "afterimage"), (lure.Position, lure.DefinitionId));
        Assert.Equal(new Cell(0, 0), PositionOf(sim, 1));
        Assert.Equal(new[] { 2 }, events.OfType<UnitTaunted>().Select(t => t.Unit.Value));
    }

    [Fact]
    public void TheRiggedDecoy_IsPlacedNextToTheSpeedster_AndBlowsUp()
    {
        var decoy = Content.Skills["rigged-decoy"];

        Assert.Equal(1, decoy.Range);
        Assert.False(decoy.SummonLeftBehind);
        Assert.NotNull(decoy.Summon!.DefeatBlast);
        Assert.Equal(1, decoy.Summon.MaxHp);
    }

    [Fact]
    public void TheDrone_Moves_TheStarExplodesOnEnemies_TheSuppressionTurretSlows()
    {
        Assert.True(Content.Skills["combat-drone"].Summon!.Mobility > 0);
        Assert.Equal(SkillWeight.Heavy, Content.Skills["combat-drone"].Weight);
        Assert.Equal(SkillWeight.Heavy, Content.Skills["star"].Weight);
        Assert.Equal(ZoneTargets.Enemies, Content.Skills["star"].Summon!.Skills.Single().Zone!.Affects);
        Assert.Contains(Content.Skills["suppression-turret"].Summon!.Skills.Single().Effects, e => e.Status == StatusKind.OffBalance);
    }

    [Fact]
    public void TheStar_HitsTheBrawlersAroundIt_NotTheEnergetic()
    {
        var sim = Fight("energetic", "star", (2, 2), ("brawler", 1, 5, 2), ("colossus", 0, 4, 3));
        Use(sim, "star", cell: (4, 2));

        for (var i = 0; i < Ticks(10_000); i++)
            sim.Tick();

        Assert.True(sim.FindUnit(new UnitId(2))!.Hp < sim.FindUnit(new UnitId(2))!.MaxHp);
        Assert.Equal(sim.FindUnit(new UnitId(3))!.MaxHp, sim.FindUnit(new UnitId(3))!.Hp);
    }

    [Fact]
    public void TheTechPassives_AreCoded()
    {
        Assert.True(Passives.TryParse("overclocking", out _));
        Assert.True(Passives.TryParse("doubleDeployment", out _));
        Assert.True(Rules.OverclockingFillPercent > 100);
        Assert.Equal(2, Rules.DoubleDeploymentCount);
    }
}
