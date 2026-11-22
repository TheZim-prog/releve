using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>Le décompte des chutes d'un héros dans un combat, pour la règle de campagne (docs/design.md 1.4).</summary>
public class FallTests
{
    private static readonly UnitId Helper = new(1), Hero = new(2), Thug = new(3);

    private static readonly SkillDefinition FlashRescue = new()
    {
        Id = "flash-rescue", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 6, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, RescueDropRadius = 4,
    };

    /// <summary>
    /// Un équipier (1, jauge pleine, Sauvetage éclair) en (0, 1), le héros (2, 20 PV, à terre à 0) en (1, 1), et un voyou
    /// (3) au contact du héros, qui frappe à 10 ou 30.
    /// </summary>
    private static CombatSimulation Fight() => Simulation(new Grid(8, 3),
        Unit(1, team: 0, 0, 1, gauge: Capacity, skills: new[] { Strike, FlashRescue }),
        Unit(2, team: 0, 1, 1, hp: 20, onDefeat: DefeatOutcome.Downed),
        Unit(3, team: 1, 2, 1, gauge: Capacity));

    private static void Hit(CombatSimulation sim, string skill = "smash")
    {
        sim.Get(3).Gauge = Capacity;
        sim.Get(3).StartCooldown(skill, 0);
        sim.Submit(Use(Thug, skill, Hero));
        sim.Tick();
    }

    private static void Revive(CombatSimulation sim)
    {
        sim.Get(1).Gauge = Capacity;
        sim.Submit(new ActivateCommand(Helper, null, Interaction: new InteractionUse(InteractionKind.Revive, Hero)));
        sim.Tick();
    }

    [Fact]
    public void AHeroKnockedDown_CountsAFall()
    {
        var sim = Fight();

        Hit(sim);

        Assert.Equal(UnitStatus.Downed, sim.Get(2).Status);
        Assert.Equal((1, 0), (sim.Get(2).Falls, sim.Get(2).RescuedFalls));
    }

    [Fact]
    public void EachFall_Counts_AndARevivalRescuesIt()
    {
        var sim = Fight();
        Hit(sim);
        Revive(sim);
        Assert.Equal((1, 1), (sim.Get(2).Falls, sim.Get(2).RescuedFalls));

        Hit(sim, "strike"); // relevé avec 5 PV : il retombe

        Assert.Equal((2, 1), (sim.Get(2).Falls, sim.Get(2).RescuedFalls));
    }

    [Fact]
    public void AFallCarriedAway_ThenRevived_IsRescuedOnce()
    {
        var sim = Fight();
        Hit(sim);

        sim.Submit(new ActivateCommand(Helper, null, new SkillUse("flash-rescue", Hero, new Cell(0, 2))));
        sim.Tick();
        Assert.Equal(1, sim.Get(2).RescuedFalls);
        Revive(sim);

        Assert.Equal((1, 1), (sim.Get(2).Falls, sim.Get(2).RescuedFalls));
    }
}
