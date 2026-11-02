using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

public class StatusTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Foe = new(2);

    private static SkillDefinition Control(string id, StatusKind status, int durationMs, int damage = 0) => Strike with
    {
        Id = id,
        MinDamage = damage,
        MaxDamage = damage,
        Effects = new[] { new StatusEffect { Status = status, DurationMs = durationMs } },
    };

    /// <summary>Étourdit 10 ticks (500 ms), sans dégâts.</summary>
    private static readonly SkillDefinition StunBlow = Control("stun-blow", StatusKind.Stunned, 500);

    /// <summary>Déséquilibre 10 ticks (500 ms), sans dégâts.</summary>
    private static readonly SkillDefinition Trip = Control("trip", StatusKind.OffBalance, 500);

    /// <summary>Le héros (initiative = capacité) peut agir à chaque tick, avec toutes les compétences données.</summary>
    private static CombatSimulation Duel(UnitSetup foe, params SkillDefinition[] heroSkills) =>
        Simulation(new Grid(3, 1), Unit(1, team: 0, 0, 0, initiative: Capacity, gauge: Capacity, skills: heroSkills), foe);

    private static void Use(CombatSimulation sim, string skill) =>
        sim.Submit(new ActivateCommand(Hero, null, new SkillUse(skill, Foe)));

    [Fact]
    public void Stun_FreezesTheGauge_ForItsDuration()
    {
        var sim = Duel(Unit(2, team: 1, 1, 0, initiative: 10, gauge: 30), StunBlow);

        Use(sim, "stun-blow");
        // Contrôle pur : pas d'événement de dégâts.
        Assert.Equal(
            new CombatEvent[] { new SkillUsed(1, Hero, "stun-blow", Foe), new StatusApplied(1, Foe, StatusKind.Stunned, 11) },
            sim.Tick().Where(e => e.Tick == 1 && e is not UnitReady));

        sim.TickTimes(9); // ticks 2 à 10 : toujours étourdi
        Assert.Equal(30, sim.Get(2).Gauge);
        Assert.True(sim.Get(2).HasStatus(StatusKind.Stunned));

        Assert.Contains(new StatusEnded(11, Foe, StatusKind.Stunned), sim.Tick());
        Assert.Equal(40, sim.Get(2).Gauge); // se remplit de nouveau dès le tick de fin
    }

    [Fact]
    public void StunnedUnit_CannotAct_EvenWithAFullGauge_AndIsReadyAgainWhenItEnds()
    {
        var sim = Duel(Unit(2, team: 1, 1, 0, gauge: Capacity), StunBlow);
        Use(sim, "stun-blow");
        sim.Tick();

        var foe = sim.Get(2);
        Assert.True(foe.IsGaugeFull);
        Assert.False(foe.IsReady);

        var move = new ActivateCommand(Foe, new Cell(2, 0));
        sim.Submit(move);
        Assert.Contains(new CommandRejected(2, move, RejectionReason.Stunned), sim.Tick());

        sim.TickTimes(8); // jusqu'au tick 10
        // Tick 11 : l'étourdissement se termine avant les commandes, l'unité peut agir dans le même tick.
        sim.Submit(move);
        var events = sim.Tick();
        Assert.Equal(new StatusEnded(11, Foe, StatusKind.Stunned), events[0]);
        Assert.Equal(new UnitReady(11, Foe), events[1]);
        Assert.IsType<UnitMoved>(events[2]);
    }

    [Fact]
    public void OffBalance_SlowsTheGauge_ThenItRecovers()
    {
        var sim = Duel(Unit(2, team: 1, 1, 0, initiative: 10), Trip);

        Use(sim, "trip");
        sim.TickTimes(10); // ticks 1 à 10 : +5 par tick
        Assert.Equal(50, sim.Get(2).Gauge);

        Assert.Contains(new StatusEnded(11, Foe, StatusKind.OffBalance), sim.Tick());
        Assert.Equal(60, sim.Get(2).Gauge); // vitesse normale dès le tick de fin
    }

    [Theory]
    [InlineData(1, 5)]    // 0,5 par tick : la jauge avance quand même, d'un point tous les deux ticks
    [InlineData(15, 75)]  // 7,5 par tick : exact en moyenne, pas 7 × 10
    public void OffBalance_IsExactOnAverage_EvenWithSmallOrOddInitiative(int initiative, int gaugeAfter10Ticks)
    {
        var sim = Duel(Unit(2, team: 1, 1, 0, initiative: initiative), Trip);

        Use(sim, "trip");
        sim.TickTimes(10);

        Assert.Equal(gaugeAfter10Ticks, sim.Get(2).Gauge);
    }

    [Fact]
    public void DefeatedUnit_LosesItsStatuses_BeforeFalling()
    {
        var longStun = Control("long-stun", StatusKind.Stunned, 2000);
        var sim = Duel(Unit(2, team: 1, 1, 0, hp: 10, gauge: Capacity), longStun, Strike);

        Use(sim, "long-stun");
        sim.Tick();
        Use(sim, "strike");
        var events = sim.Tick();

        Assert.Equal(
            new CombatEvent[]
            {
                new UnitDamaged(2, Foe, 10, 0),
                new StatusEnded(2, Foe, StatusKind.Stunned),
                new UnitRemoved(2, Foe),
            },
            events.SkipWhile(e => e is not UnitDamaged).TakeWhile(e => e is not UnitReady));
        Assert.Empty(sim.Get(2).StatusEndTicks);
        Assert.DoesNotContain(sim.TickTimes(50), e => e is StatusEnded or UnitReady { Unit.Value: 2 });
    }

    [Fact]
    public void Skill_EffectsListIsCopied()
    {
        var effects = new[] { new StatusEffect { Status = StatusKind.Stunned, DurationMs = 500 } };
        var skill = Strike with { Effects = effects };

        effects[0] = new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 99_999 };

        Assert.Equal(StatusKind.Stunned, skill.Effects.Single().Status);
        Assert.IsNotType<StatusEffect[]>(skill.Effects);
    }

    [Fact]
    public void Stun_TakesPrecedenceOverOffBalance()
    {
        var sim = Duel(Unit(2, team: 1, 1, 0, initiative: 10, gauge: 20), StunBlow, Trip);

        Use(sim, "trip");
        sim.Tick();
        Use(sim, "stun-blow");
        sim.TickTimes(3);

        Assert.Equal(25, sim.Get(2).Gauge); // +5 au tick 1, puis gelée
    }

    [Fact]
    public void Reapplying_KeepsTheLongestDuration()
    {
        var longStun = Control("long-stun", StatusKind.Stunned, 2000); // 40 ticks
        var sim = Duel(Unit(2, team: 1, 1, 0), StunBlow, longStun);

        Use(sim, "long-stun");
        Assert.Contains(new StatusApplied(1, Foe, StatusKind.Stunned, 41), sim.Tick());

        Use(sim, "stun-blow"); // finirait au tick 12 : on garde 41
        Assert.Contains(new StatusApplied(2, Foe, StatusKind.Stunned, 41), sim.Tick());

        sim.TickTimes(35); // tick 37
        Use(sim, "stun-blow"); // tick 38 + 10 = 48 : plus long, on prolonge
        Assert.Contains(new StatusApplied(38, Foe, StatusKind.Stunned, 48), sim.Tick());
        Assert.Equal(48, sim.Get(2).StatusEndTicks[StatusKind.Stunned]);
    }

    [Fact]
    public void DamageThenStatus_AndNoStatusOnADefeatedTarget()
    {
        var stunningHit = Control("stunning-hit", StatusKind.Stunned, 500, damage: 10);

        var survivor = Duel(Unit(2, team: 1, 1, 0, hp: 50), stunningHit);
        Use(survivor, "stunning-hit");
        Assert.Equal(
            new CombatEvent[]
            {
                new SkillUsed(1, Hero, "stunning-hit", Foe),
                new UnitDamaged(1, Foe, 10, 40),
                new StatusApplied(1, Foe, StatusKind.Stunned, 11),
            },
            survivor.Tick().Where(e => e is not UnitReady));

        var defeated = Duel(Unit(2, team: 1, 1, 0, hp: 10), stunningHit);
        Use(defeated, "stunning-hit");
        Assert.DoesNotContain(defeated.Tick(), e => e is StatusApplied);
    }
}
