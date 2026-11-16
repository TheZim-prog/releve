using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>Les dernières compétences du colosse et du combattant, dans le contenu réel. Terrain vide de 9 × 5.</summary>
public class ColossusFighterContentTests
{
    [Fact]
    public void Fortress_IsHeldForItsWholeCharge_ThenFalls()
    {
        var sim = Fight("colossus", "fortress", (4, 2), ("fighter", 0, 3, 2), ("brawler", 1, 2, 2));

        var events = Use(sim, "fortress", cell: (4, 2));

        Assert.Contains(events, e => e is DomeEnded { Owner.Value: 1 });
        Assert.Equal(Ticks(Content.Skills["fortress"].ChargeMs) + 1, events.OfType<DomeEnded>().Single().Tick);
    }

    [Fact]
    public void SpinThrow_HitsTheGrabbedBrawler_AndThrowsIt()
    {
        var sim = Fight("colossus", "spin-throw", (4, 2), ("brawler", 1, 5, 2));

        var events = Use(sim, "spin-throw", target: 2, cell: (8, 2));

        Assert.Equal(new[] { 2 }, Damaged(events));
        Assert.Equal(new Cell(7, 2), PositionOf(sim, 2));
    }

    [Fact]
    public void DeadlyThrow_IsChargedFromAfar_AndHitsHard()
    {
        var sim = Fight("fighter", "deadly-throw", (0, 2), ("brawler", 1, 7, 2));

        var events = Use(sim, "deadly-throw", target: 2);

        Assert.True(Content.Skills["deadly-throw"].IsCharged);
        Assert.Contains(events, e => e is ChargeStarted);
        Assert.True(events.OfType<UnitDamaged>().Single().Amount >= Content.Skills["deadly-throw"].MinDamage);
    }

    [Fact]
    public void TheNewSkillsAndPassive_AreCoded()
    {
        Assert.True(Passives.TryParse("unshakable", out _));
        Assert.True(Content.Skills["personal-shield"].ShieldsAlly);
        Assert.True(Content.Skills["relentless"].ResetsCooldownOnKill);
        Assert.True(Content.Skills["finishing-blow"].MissingHpDamageBonusPercent > 0);
        Assert.Equal(SceneryUse.Place, Content.Skills["barricade"].Scenery!.Use);
        Assert.Equal(1, Content.Skills["throw-scenery"].Scenery!.Reach);
        Assert.True(Content.Skills["telekinetic-throw"].Scenery!.Reach > 1);
    }
}
