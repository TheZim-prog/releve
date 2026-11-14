using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Désarmé (docs/design.md, section 3) : l'unité n'a plus que son attaque de base, et sa charge en cours est
/// interrompue. Ici l'état dure 1 s (20 ticks) : posé au tick 1, il prend fin au tick 21.
/// </summary>
public class DisarmedTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Victim = new(2);

    /// <summary>Désarme sans faire de dégâts, jusqu'à 7 cases.</summary>
    private static readonly SkillDefinition Disarm = Strike with
    {
        Id = "disarm", Range = 7, MinDamage = 0, MaxDamage = 0,
        Effects = new[] { new StatusEffect { Status = StatusKind.Disarmed, DurationMs = 1000 } },
    };

    private static UnitSetup HeroAt(int x, int y) =>
        Unit(1, team: 0, x, y, gauge: Capacity, initiative: Capacity, hp: 1000, skills: new[] { Disarm });

    /// <summary>Le héros désarme l'unité 2 au tick 1 ; renvoie les événements du tick.</summary>
    private static IReadOnlyList<CombatEvent> DisarmTheVictim(CombatSimulation sim)
    {
        sim.Submit(Use(Hero, "disarm", Victim));
        var events = sim.Tick();
        Assert.Contains(events, e => e is StatusApplied { Unit.Value: 2, Status: StatusKind.Disarmed });
        return events;
    }

    [Fact]
    public void ADisarmedUnit_OnlyHasItsBasicAttack_ButStillMoves()
    {
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0),
            Unit(2, team: 1, 1, 0, gauge: Capacity, initiative: Capacity, basicAttack: "strike"));
        DisarmTheVictim(sim);

        var smash = Use(Victim, "smash", Hero);
        sim.Submit(smash);
        sim.Submit(Use(Victim, "strike", Hero));
        var events = sim.Tick();

        Assert.Equal(new CommandRejected(2, smash, RejectionReason.Disarmed), events[0]);
        Assert.Contains(events, e => e is UnitDamaged { Unit.Value: 1, Amount: 10 });

        sim.Submit(new ActivateCommand(Victim, new Cell(3, 0)));
        Assert.Contains(sim.Tick(), e => e is UnitMoved { Unit.Value: 2 });
    }

    [Fact]
    public void WithoutABasicAttack_ADisarmedUnitHasNoSkillLeft()
    {
        // L'unité de test n'a pas d'attaque de base désignée : désarmée, aucune de ses compétences n'est permise.
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0, gauge: Capacity));
        DisarmTheVictim(sim);

        Assert.All(sim.Get(2).Skills, skill => Assert.Equal(RejectionReason.Disarmed, sim.Get(2).StatusBlocking(skill)));
    }

    [Fact]
    public void Disarmed_InterruptsAChargeInProgress()
    {
        var charged = Smash with { Id = "charged", ChargeMs = 1000, CooldownMs = 0 };
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0, gauge: Capacity, skills: new[] { Strike, charged }));
        sim.Submit(Use(Victim, "charged", Hero));
        Assert.Contains(sim.Tick(), e => e is ChargeStarted);

        sim.Submit(Use(Hero, "disarm", Victim));
        var events = sim.Tick();

        Assert.Contains(new ChargeInterrupted(2, Victim, "charged"), events);
        Assert.DoesNotContain(sim.TickTimes(25), e => e is UnitDamaged);
    }

    [Fact]
    public void AnOrderWithAForbiddenSkill_WaitsForTheEndOfTheStatus()
    {
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0, gauge: Capacity, basicAttack: "strike"));
        DisarmTheVictim(sim);

        sim.Submit(new OrderCommand(Use(Victim, "smash", Hero)));
        Assert.IsType<OrderGiven>(sim.Tick().Single());
        Assert.DoesNotContain(sim.TickTimes(18), e => e is SkillUsed or CommandRejected); // ticks 3 à 20

        Assert.Contains(sim.Tick(), e => e is SkillUsed { Unit.Value: 2, SkillId: "smash" }); // tick 21
    }

    [Fact]
    public void OnABoss_DisarmedLastsHalfAsLong()
    {
        var onAThug = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0));
        var onABoss = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0, isBoss: true));

        Assert.Contains(new StatusApplied(1, Victim, StatusKind.Disarmed, 21), DisarmTheVictim(onAThug));
        Assert.Contains(new StatusApplied(1, Victim, StatusKind.Disarmed, 11), DisarmTheVictim(onABoss)); // 50 % de 20 ticks
    }

    [Fact]
    public void ADisarmedAiUnit_FallsBackOnItsBasicAttack()
    {
        // Son coup le plus fort est « smash » (30) ; désarmé, il lui reste « strike » (10), son attaque de base.
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0),
            Unit(2, team: 1, 1, 0, initiative: 50, behavior: Behaviors.Basic, basicAttack: "strike"));
        DisarmTheVictim(sim);

        var whileDisarmed = sim.TickTimes(19).OfType<SkillUsed>().Where(e => e.Unit == Victim).Select(e => e.SkillId).ToList();
        Assert.NotEmpty(whileDisarmed);
        Assert.All(whileDisarmed, id => Assert.Equal("strike", id));

        Assert.Contains(sim.TickTimes(10), e => e is SkillUsed { Unit.Value: 2, SkillId: "smash" });
    }

    [Fact]
    public void ADisarmedAiUnit_WithoutBasicAttack_Waits()
    {
        // Une tourelle, par exemple : pas d'attaque de base, donc rien à faire tant qu'elle est désarmée.
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0),
            Unit(2, team: 1, 1, 0, initiative: 50, mobility: 0, behavior: Behaviors.Basic, skills: new[] { Strike }));
        DisarmTheVictim(sim);

        Assert.DoesNotContain(sim.TickTimes(19), e => e is SkillUsed { Unit.Value: 2 }); // ticks 2 à 20
        Assert.Contains(sim.TickTimes(3), e => e is SkillUsed { Unit.Value: 2 });
    }

    [Fact]
    public void DisarmedOnTheWay_TheUnitFinishesItsMove_ButLosesItsPlannedSkill()
    {
        // L'attaque de base de l'unité 2 désarme : son attaque d'opportunité touche le héros qui s'éloigne.
        var twist = Strike with
        {
            Id = "twist", MinDamage = 5, MaxDamage = 5,
            Effects = new[] { new StatusEffect { Status = StatusKind.Disarmed, DurationMs = 1000 } },
        };
        CombatSimulation Combat() => Simulation(new Grid(6, 1),
            Unit(1, team: 0, 1, 0, gauge: Capacity, hp: 100, basicAttack: "strike"),
            Unit(2, team: 1, 0, 0, skills: new[] { twist }, basicAttack: "twist"),
            Unit(3, team: 1, 4, 0, hp: 100));

        // Il comptait finir par « smash » : il arrive, mais la compétence est annulée (jauge comme après un déplacement seul).
        var heavy = Combat();
        heavy.Submit(new ActivateCommand(Hero, new Cell(3, 0), new SkillUse("smash", new UnitId(3))));
        var events = heavy.Tick();
        Assert.Contains(events, e => e is StatusApplied { Unit.Value: 1, Status: StatusKind.Disarmed });
        Assert.Contains(events, e => e is UnitMoved { Unit.Value: 1 });
        Assert.DoesNotContain(events, e => e is SkillUsed);
        Assert.Equal((new Cell(3, 0), Capacity / 2 + 10), (heavy.Get(1).Position, heavy.Get(1).Gauge));

        // Avec son attaque de base, rien ne l'empêche de frapper.
        var basic = Combat();
        basic.Submit(new ActivateCommand(Hero, new Cell(3, 0), new SkillUse("strike", new UnitId(3))));
        Assert.Contains(basic.Tick(), e => e is SkillUsed { Unit.Value: 1, SkillId: "strike" });
    }

    [Fact]
    public void AnIntentionMadeImpossible_IsDroppedAtOnce()
    {
        // À mi-jauge, l'ennemi a fixé son intention : « smash ». Désarmé, il l'abandonne et en choisit une permise.
        var sim = Simulation(new Grid(6, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, hp: 1000, skills: new[] { Disarm }),
            Unit(2, team: 1, 1, 0, initiative: 10, gauge: 60, behavior: Behaviors.Basic, basicAttack: "strike"));
        sim.Tick();
        Assert.Equal("smash", sim.Get(2).Intention!.SkillId);

        sim.Submit(Use(Hero, "disarm", Victim));
        var events = sim.Tick();

        Assert.Contains(new IntentionChosen(2, Victim, null), events);
        Assert.Equal("strike", sim.Get(2).Intention!.SkillId);
    }
}
