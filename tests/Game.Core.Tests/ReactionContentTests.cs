using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>Les réactions du contenu réel (Parade, Déviation, Maître d'armes, Tir de couverture…). Terrain vide de 9 × 5.</summary>
public class ReactionContentTests
{
    [Fact]
    public void Parry_AndDeflection_GuardAgainstMeleeAndShots_UntilTheNextAction()
    {
        Assert.Equal(GuardAgainst.Melee, Content.Skills["parry"].Guard!.Against);
        Assert.Equal(GuardAgainst.Ranged, Content.Skills["deflection"].Guard!.Against);
        Assert.Equal(0, Content.Skills["parry"].Guard!.DurationMs);
    }

    [Fact]
    public void TheWeaponMaster_IsHeavy_TauntsTheEnemiesAround_AndParriesForAWhile()
    {
        var skill = Content.Skills["weapon-master"];
        var sim = FightAgainstAi("fighter", "weapon-master", (4, 2), ("brawler", 1, 3, 2), ("brawler", 1, 5, 2), ("brawler", 1, 8, 4));

        var events = Use(sim, "weapon-master", cell: (4, 2));

        Assert.Equal(SkillWeight.Heavy, skill.Weight);
        Assert.Equal(new[] { 2, 3 }, events.OfType<UnitTaunted>().Select(t => t.Unit.Value).Order());
        Assert.True(skill.Guard is { BlocksAll: true, Ripostes: true, DurationMs: > 0 });
    }

    [Fact]
    public void CoveringFire_ShootsTheFirstEnemyEnteringItsZone()
    {
        var sim = Fight("tech", "covering-fire", (0, 2), ("brawler", 1, 8, 2));
        Use(sim, "covering-fire", cell: (5, 2));

        // Un ordre à l'avance : il partira dès que la jauge du cogneur sera pleine.
        sim.Submit(new OrderCommand(new ActivateCommand(new UnitId(2), new Cell(6, 2))));
        for (var i = 0; i < Ticks(10_000) && sim.FindUnit(new UnitId(2))!.Position != new Cell(6, 2); i++)
            sim.Tick();

        Assert.Single(sim.EventLog.OfType<WatchFired>());
        Assert.True(sim.FindUnit(new UnitId(2))!.Hp < sim.FindUnit(new UnitId(2))!.MaxHp);
    }

    [Fact]
    public void TheNewPassives_AreCoded()
    {
        foreach (var id in new[] { "bodyguard", "reflexes", "riposte", "stoppingBlow" })
            Assert.True(Passives.TryParse(id, out _), id);
    }
}
