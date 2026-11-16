using Game.Core.Combat;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>Les compétences du télépathe ajoutées au contenu réel. Terrain vide de 9 × 5.</summary>
public class TelepathContentTests
{
    [Fact]
    public void Domination_ShakesTheKingpin_AndSlowsHim()
    {
        // Le caïd garde son IA (il en faut une pour fuir), mais sa jauge est vide.
        var sim = FightAgainstAi("telepath", "domination", (0, 2), ("kingpin", 1, 3, 2));

        var events = Use(sim, "domination", target: 2);

        Assert.Equal(new[] { 2 }, Damaged(events));
        Assert.Equal(new[] { 2 }, Affected(events, StatusKind.OffBalance));
        Assert.Equal(1, sim.FindUnit(new UnitId(2))!.Team);
    }

    [Fact]
    public void ForceWave_PushesEveryoneAroundTheTelepath_AlliesIncluded()
    {
        var sim = Fight("telepath", "force-wave", (4, 2), ("brawler", 1, 5, 2), ("colossus", 0, 3, 2));

        Use(sim, "force-wave", cell: (4, 2));

        Assert.Equal((8, 0), (PositionOf(sim, 2).X, PositionOf(sim, 3).X));
    }

    [Fact]
    public void Siphon_IsAMentalAttack_AndTheEmpathSkillsSupportAllies()
    {
        Assert.True(Content.Skills["siphon"] is { IsMental: true, SiphonRange: 5 });
        Assert.Equal(SkillTargeting.Ally, Content.Skills["transfusion"].Target);
        Assert.True(Content.Skills["clarity"].ClearsHarmfulStatuses);
        Assert.Contains(Content.Skills["serenity"].Effects, e => e.Status == StatusKind.Serene);
        Assert.True(Content.Skills["awakening"] is { RevivesDowned: true, Zone.Affects: ZoneTargets.Allies });
    }
}
