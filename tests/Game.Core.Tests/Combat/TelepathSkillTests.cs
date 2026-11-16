using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Pouvoirs du télépathe : frapper sans voir (Frappe télépathique, Cri psychique), siphonner, donner ses PV, apaiser
/// (Clarté, Sérénité), éveiller, et secouer un boss qu'il ne peut pas dominer.
/// </summary>
public class TelepathSkillTests
{
    private static readonly UnitId Telepath = new(1);
    private static readonly UnitId Ally = new(2);
    private static readonly UnitId Foe = new(9);

    private static readonly SkillDefinition MindStrike = Strike with
    {
        Id = "mind-strike", Range = 5, IsMental = true, IgnoresLineOfSight = true,
    };

    private static readonly SkillDefinition Siphon = Strike with { Id = "siphon", Range = 5, IsMental = true, SiphonRange = 3 };

    private static readonly SkillDefinition Transfusion = new()
    {
        Id = "transfusion", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 5, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, MinHeal = 20, MaxHeal = 20, GivesOwnHp = true,
    };

    private static readonly SkillDefinition Clarity = new()
    {
        Id = "clarity", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 5, MinDamage = 0, MaxDamage = 0,
        CooldownMs = 0, ClearsHarmfulStatuses = true,
    };

    private static readonly SkillDefinition Serenity = Clarity with
    {
        Id = "serenity", ClearsHarmfulStatuses = false,
        Effects = new[] { new StatusEffect { Status = StatusKind.Serene, DurationMs = 2000 } },
    };

    private static readonly SkillDefinition Awakening = new()
    {
        Id = "awakening", Weight = SkillWeight.Heavy, Range = 4, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Allies },
        MinHeal = 10, MaxHeal = 10, RevivesDowned = true,
    };

    private static readonly SkillDefinition Domination = new()
    {
        Id = "domination", Weight = SkillWeight.Heavy, Range = 4, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, IsMental = true,
        Effects = new[] { new StatusEffect { Status = StatusKind.Dominated, DurationMs = 5000 } },
        VsBosses = new BossShakeDefinition
        {
            MinDamage = 20, MaxDamage = 20, Effects = new[] { new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 2000 } },
        },
    };

    /// <summary>Le coup d'un ennemi : 10 dégâts, étourdi et déséquilibré.</summary>
    private static readonly SkillDefinition Daze = Strike with
    {
        Id = "daze", Range = 6,
        Effects = new[]
        {
            new StatusEffect { Status = StatusKind.Stunned, DurationMs = 2000 },
            new StatusEffect { Status = StatusKind.OffBalance, DurationMs = 2000 },
        },
    };

    private static UnitSetup TelepathWith(SkillDefinition skill, int x = 0, int y = 0, int hp = 100) =>
        Unit(1, team: 0, x, y, gauge: Capacity, hp: hp, skills: new[] { skill });

    [Fact]
    public void TelepathicStrike_HitsThroughWalls_WithoutCover()
    {
        var sim = Simulation(Grid.FromRows("..#o.."), TelepathWith(MindStrike), Unit(9, team: 1, 4, 0));

        sim.Submit(Use(Telepath, "mind-strike", Foe));
        var events = sim.Tick();

        Assert.Equal(90, sim.Get(9).Hp);
        Assert.DoesNotContain(events, e => e is HitCovered);
        Assert.Equal(RejectionReason.NoLineOfSight,
            Simulation(Grid.FromRows("..#o.."), TelepathWith(MindStrike with { IgnoresLineOfSight = false }), Unit(9, team: 1, 4, 0))
                .Check(Use(Telepath, "mind-strike", Foe)));
    }

    [Fact]
    public void PsychicScream_HitsTheEnemiesAroundACellBehindAWall()
    {
        var scream = MindStrike with
        {
            Id = "scream", Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Enemies },
        };
        var sim = Simulation(Grid.FromRows("..#...", "..#..."), TelepathWith(scream), Unit(8, team: 1, 4, 0), Unit(9, team: 1, 5, 1));

        sim.Submit(Use(Telepath, "scream", cell: new Cell(4, 0)));
        sim.Tick();

        Assert.Equal((90, 90), (sim.Get(8).Hp, sim.Get(9).Hp));
    }

    [Fact]
    public void OnlyAMentalPowerAtRange_IgnoresLineOfSight()
    {
        MindStrike.Validate();
        Assert.Throws<ArgumentException>(() => (MindStrike with { IsMental = false }).Validate());
        Assert.Throws<ArgumentException>(() => (MindStrike with { Range = 1 }).Validate());
    }

    [Fact]
    public void Siphon_HealsTheMostWoundedAllyInRange_ByWhatTheEnemyLost()
    {
        var sim = Simulation(new Grid(8, 2), TelepathWith(Siphon, hp: 100),
            Unit(2, team: 0, 2, 0), Unit(3, team: 0, 3, 1), Unit(4, team: 0, 7, 1), Unit(9, team: 1, 5, 0));
        sim.Get(2).Hp = 80;
        sim.Get(3).Hp = 60;
        sim.Get(4).Hp = 10; // le plus blessé, mais trop loin
        sim.Get(1).Hp = 50; // le télépathe ne se soigne pas lui-même

        sim.Submit(Use(Telepath, "siphon", Foe));
        sim.Tick();

        Assert.Equal((80, 70, 10), (sim.Get(2).Hp, sim.Get(3).Hp, sim.Get(4).Hp));
    }

    [Fact]
    public void Transfusion_GivesTheTelepathsOwnHp_KeepingOne()
    {
        var sim = Simulation(new Grid(6, 1), TelepathWith(Transfusion, hp: 100), Unit(2, team: 0, 2, 0), Unit(9, team: 1, 5, 0));
        sim.Get(1).Hp = 12;
        sim.Get(2).Hp = 50;

        sim.Submit(Use(Telepath, "transfusion", Ally));
        sim.Tick();

        Assert.Equal((1, 61), (sim.Get(1).Hp, sim.Get(2).Hp));
        Assert.Equal(RejectionReason.NoEffect, sim.Check(Use(Telepath, "transfusion", Ally), ignoreTiming: true));
    }

    [Fact]
    public void Clarity_EndsTheAllysHarmfulStatuses_AndIsUselessWithoutAny()
    {
        var sim = Simulation(new Grid(8, 1), TelepathWith(Clarity), Unit(2, team: 0, 2, 0, gauge: Capacity),
            Unit(9, team: 1, 7, 0, gauge: Capacity, skills: new[] { Daze }));
        Assert.Equal(RejectionReason.NoEffect, sim.Check(Use(Telepath, "clarity", Ally)));
        sim.Submit(Use(Foe, "daze", Ally));
        sim.Tick();
        Assert.True(sim.Get(2).HasStatus(StatusKind.Stunned));

        sim.Submit(Use(Telepath, "clarity", Ally));
        var events = sim.Tick();

        Assert.False(sim.Get(2).HasStatus(StatusKind.Stunned) || sim.Get(2).HasStatus(StatusKind.OffBalance));
        Assert.Contains(new UnitReady(2, Ally), events);
    }

    [Fact]
    public void ADominatedEnemy_KeepsItsDomination_UnderClarity_AndIsNotSiphonedFor()
    {
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { Domination, Clarity, Siphon }),
            Unit(8, team: 1, 2, 0), Unit(9, team: 1, 5, 0));
        sim.Get(8).Hp = 50;
        sim.Submit(Use(Telepath, "domination", new UnitId(8)));
        sim.Tick();
        Assert.Equal(0, sim.Get(8).Team);

        Assert.Equal(RejectionReason.NoEffect, sim.Check(Use(Telepath, "clarity", new UnitId(8)), ignoreTiming: true));
        sim.TickTimes(10); // de nouveau prêt (action lourde : la jauge repart de 0)
        sim.Submit(Use(Telepath, "siphon", Foe));
        sim.Tick();

        Assert.Equal((90, 50), (sim.Get(9).Hp, sim.Get(8).Hp));
    }

    [Fact]
    public void Serenity_HarmfulStatusesNoLongerTakeOnTheAlly()
    {
        var sim = Simulation(new Grid(8, 1), TelepathWith(Serenity), Unit(2, team: 0, 2, 0),
            Unit(9, team: 1, 7, 0, gauge: Capacity, skills: new[] { Daze }));
        sim.Submit(Use(Telepath, "serenity", Ally));
        sim.Tick();

        sim.Submit(Use(Foe, "daze", Ally));
        var events = sim.Tick();

        Assert.Equal(90, sim.Get(2).Hp);
        Assert.False(sim.Get(2).HasStatus(StatusKind.Stunned) || sim.Get(2).HasStatus(StatusKind.OffBalance));
        Assert.Contains(events, e => e is UnitResisted { Unit.Value: 2 });
    }

    [Fact]
    public void Awakening_HealsTheStandingAllies_AndRaisesTheDownedOnes()
    {
        var sim = Simulation(new Grid(8, 3), TelepathWith(Awakening, 0, 1),
            Unit(2, team: 0, 3, 0), Unit(3, team: 0, 4, 1, hp: 40, onDefeat: DefeatOutcome.Downed),
            Unit(9, team: 1, 5, 1, gauge: Capacity, skills: new[] { Smash with { MinDamage = 50, MaxDamage = 50 } }));
        sim.Get(2).Hp = 50;
        sim.Submit(Use(Foe, "smash", new UnitId(3)));
        sim.Tick();
        Assert.Equal(UnitStatus.Downed, sim.Get(3).Status);

        sim.Submit(Use(Telepath, "awakening", cell: new Cell(3, 1)));
        var events = sim.Tick();

        Assert.Equal(60, sim.Get(2).Hp);
        Assert.Contains(events, e => e is UnitRevived { Unit.Value: 3, By.Value: 1, Hp: 10 });
        Assert.Equal(UnitStatus.Active, sim.Get(3).Status);
        Assert.True(sim.Get(3).Gauge <= 10); // jauge vide au relèvement, plus le remplissage de ce tick
    }

    [Fact]
    public void Domination_ShakesABoss_InsteadOfTakingIt()
    {
        var sim = Simulation(new Grid(6, 1), TelepathWith(Domination), Unit(9, team: 1, 3, 0, isBoss: true));

        sim.Submit(Use(Telepath, "domination", Foe));
        sim.Tick();

        Assert.Equal((1, 80), (sim.Get(9).Team, sim.Get(9).Hp));
        Assert.True(sim.Get(9).HasStatus(StatusKind.OffBalance));
        Assert.False(sim.Get(9).HasStatus(StatusKind.Dominated));
        // Sans cette règle, elle reste refusée sur un boss ; et elle domine toujours un ennemi ordinaire.
        Assert.Equal(RejectionReason.NoEffect,
            Simulation(new Grid(6, 1), TelepathWith(Domination with { VsBosses = null }), Unit(9, team: 1, 3, 0, isBoss: true))
                .Check(Use(Telepath, "domination", Foe)));
    }

    [Fact]
    public void ABossEffect_BelongsToADomination()
    {
        Domination.Validate();
        Assert.Throws<ArgumentException>(() => (Strike with { VsBosses = Domination.VsBosses }).Validate());
    }
}
