using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Coups d'achèvement : Acharnement (recharge annulée sur un coup fatal) et Coup de grâce (dégâts selon les PV manquants).</summary>
public class FinisherTests
{
    private static readonly UnitId Fighter = new(1);

    /// <summary>10 dégâts, 5 s de recharge, annulée si la cible tombe.</summary>
    private static readonly SkillDefinition Relentless = Strike with { Id = "relentless", CooldownMs = 5000, ResetsCooldownOnKill = true };

    /// <summary>10 dégâts, jusqu'au double contre une cible presque à terre.</summary>
    private static readonly SkillDefinition FinishingBlow = Strike with { Id = "finishing", MissingHpDamageBonusPercent = 100 };

    private static CombatSimulation Fight(SkillDefinition skill, int foeHp, bool isBoss = false) =>
        Simulation(new Grid(4, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { skill, Smash }),
            Unit(9, team: 1, 1, 0, hp: foeHp, isBoss: isBoss));

    [Fact]
    public void Relentless_IsReadyAgain_WhenItsTargetFalls()
    {
        var sim = Fight(Relentless, foeHp: 10);

        sim.Submit(Use(Fighter, "relentless", new UnitId(9)));
        sim.Tick();

        Assert.Equal(UnitStatus.Removed, sim.Get(9).Status);
        Assert.Equal(1, sim.Get(1).CooldownEndTick("relentless"));
    }

    [Fact]
    public void Relentless_KeepsItsCooldown_WhenItsTargetStandsUp()
    {
        var sim = Fight(Relentless, foeHp: 100);

        sim.Submit(Use(Fighter, "relentless", new UnitId(9)));
        sim.Tick();

        Assert.Equal(1 + 100, sim.Get(1).CooldownEndTick("relentless"));
    }

    [Theory]
    [InlineData(100, false, 10)] // intacte : dégâts normaux
    [InlineData(40, false, 13)] // 30 PV manquants sur 100 (Smash d'abord) : +30 %
    [InlineData(40, true, 13)] // un boss aussi
    public void FinishingBlow_HitsHarder_TheMoreHpItsTargetMisses(int hp, bool isBoss, int expected)
    {
        var sim = Fight(FinishingBlow, foeHp: 100, isBoss);
        if (hp < 100)
        {
            sim.Submit(Use(Fighter, "smash", new UnitId(9)));
            sim.TickTimes(1 + Capacity / 10); // lourd : la jauge repart de 0
        }

        sim.Submit(Use(Fighter, "finishing", new UnitId(9)));
        var damage = sim.Tick().OfType<UnitDamaged>().Single();

        Assert.Equal(expected, damage.Amount);
    }

    [Fact]
    public void BothBelongToADamagingAttackOnOneEnemy()
    {
        Relentless.Validate();
        FinishingBlow.Validate();
        Assert.Throws<ArgumentException>(() => (Relentless with { CooldownMs = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Relentless with { MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 100 } } }).Validate());
        Assert.Throws<ArgumentException>(() => (FinishingBlow with
        {
            Range = 0, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
        }).Validate());
    }
}
