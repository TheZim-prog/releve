using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Immobilisé (docs/design.md, section 3) : l'unité ne peut plus se déplacer d'elle-même, mais agit encore sur place,
/// et on peut toujours la déplacer de force. Ici l'état dure 1 s (20 ticks) : posé au tick 1, il prend fin au tick 21.
/// </summary>
public class ImmobilizedTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Victim = new(2);

    private static readonly StatusEffect Immobilized = new() { Status = StatusKind.Immobilized, DurationMs = 1000 };

    /// <summary>Immobilise sans faire de dégâts, jusqu'à 7 cases.</summary>
    private static readonly SkillDefinition Lock = Strike with { Id = "lock", Range = 7, MinDamage = 0, MaxDamage = 0, Effects = new[] { Immobilized } };

    private static readonly SkillDefinition Dash = Strike with { Id = "dash", Range = 3, SelfMove = SelfMoveType.Pierce };

    /// <summary>Une charge, plus forte que le coup de base : l'IA la préfère quand elle peut l'utiliser.</summary>
    private static readonly SkillDefinition Rush = Strike with { Id = "rush", Range = 5, MinDamage = 20, MaxDamage = 20, SelfMove = SelfMoveType.Rush };

    private static UnitSetup HeroAt(int x, int y, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, gauge: Capacity, initiative: Capacity, skills: skills.Length > 0 ? skills : new[] { Lock });

    /// <summary>Le héros immobilise l'unité 2 au tick 1.</summary>
    private static void ImmobilizeTheVictim(CombatSimulation sim)
    {
        sim.Submit(Use(Hero, "lock", Victim));
        Assert.Contains(new StatusApplied(1, Victim, StatusKind.Immobilized, 21), sim.Tick());
    }

    [Fact]
    public void AnImmobilizedUnit_CannotMove_ButStillActsOnTheSpot()
    {
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0),
            Unit(2, team: 1, 1, 0, gauge: Capacity, initiative: Capacity, skills: new[] { Strike, Dash }));
        ImmobilizeTheVictim(sim);

        var move = new ActivateCommand(Victim, new Cell(3, 0));
        var dash = Use(Victim, "dash", cell: new Cell(2, 0));
        sim.Submit(move);
        sim.Submit(dash);
        sim.Submit(Use(Victim, "strike", Hero));
        var events = sim.Tick();

        // Ni déplacement, ni mouvement spécial ; mais elle frappe encore ce qui est à sa portée.
        Assert.Equal(new CommandRejected(2, move, RejectionReason.Immobilized), events[0]);
        Assert.Equal(new CommandRejected(2, dash, RejectionReason.Immobilized), events[1]);
        Assert.Contains(events, e => e is UnitDamaged { Unit.Value: 1 });
        Assert.Equal(new Cell(1, 0), sim.Get(2).Position);
        Assert.False(sim.Get(2).CanMove);
        Assert.Equal(0, sim.Get(2).Reach);
    }

    [Fact]
    public void WhenTheStatusEnds_TheUnitMovesAgain()
    {
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0, gauge: Capacity));
        ImmobilizeTheVictim(sim);
        sim.TickTimes(19); // ticks 2 à 20 : encore immobilisée

        sim.Submit(new ActivateCommand(Victim, new Cell(3, 0)));

        Assert.Contains(sim.Tick(), e => e is UnitMoved { Unit.Value: 2 }); // tick 21 : l'état vient de finir
        Assert.Equal(3, sim.Get(2).Reach);
    }

    [Fact]
    public void ARescue_IsAMoveToo()
    {
        var rescue = new SkillDefinition
        {
            Id = "rescue", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 4, MinDamage = 0, MaxDamage = 0,
            CooldownMs = 0, RescueDropRadius = 2,
        };
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0, skills: new[] { Strike, rescue }));
        var victim = sim.Get(2);

        Assert.Null(victim.StatusBlocking(rescue));
        ImmobilizeTheVictim(sim);

        Assert.Equal(RejectionReason.Immobilized, victim.StatusBlocking(rescue));
        Assert.Null(victim.StatusBlocking(Strike));
    }

    [Fact]
    public void AnImmobilizedUnit_CanStillBeMovedByForce()
    {
        var shove = Strike with { Id = "shove", ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Push, Distance = 2 } };
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0, Lock, shove), Unit(2, team: 1, 1, 0, hp: 100));
        ImmobilizeTheVictim(sim);

        sim.Submit(Use(Hero, "shove", Victim));

        Assert.Contains(new UnitDisplaced(2, Victim, new Cell(1, 0), new Cell(3, 0)), sim.Tick());
        Assert.True(sim.Get(2).HasStatus(StatusKind.Immobilized));
    }

    [Fact]
    public void AnOrderToMove_WaitsForTheEndOfTheStatus()
    {
        var sim = Simulation(new Grid(6, 1), HeroAt(0, 0), Unit(2, team: 1, 1, 0, gauge: Capacity));
        ImmobilizeTheVictim(sim);

        // Comme pour un étourdissement : il suffit d'attendre, l'ordre est accepté et partira tout seul.
        sim.Submit(new OrderCommand(new ActivateCommand(Victim, new Cell(3, 0))));
        Assert.IsType<OrderGiven>(sim.Tick().Single());
        Assert.DoesNotContain(sim.TickTimes(18), e => e is UnitMoved or CommandRejected); // ticks 3 à 20

        Assert.Contains(sim.Tick(), e => e is UnitMoved { Unit.Value: 2 }); // tick 21
    }

    [Fact]
    public void ImmobilizedByAnOpportunityAttack_TheMoverStopsOnTheSpot_AndLosesItsAction()
    {
        var snare = Strike with { Id = "snare", MinDamage = 5, MaxDamage = 5, Effects = new[] { Immobilized } };
        var sim = Simulation(new Grid(6, 1),
            Unit(1, team: 0, 1, 0, gauge: Capacity),
            Unit(2, team: 1, 0, 0, skills: new[] { snare }, basicAttack: "snare"),
            Unit(3, team: 1, 4, 0));

        // Il veut s'éloigner de l'unité 2 pour aller frapper l'unité 3.
        sim.Submit(new ActivateCommand(Hero, new Cell(3, 0), new SkillUse("strike", new UnitId(3))));
        var events = sim.Tick();

        Assert.Contains(events, e => e is OpportunityAttack { Attacker.Value: 2 });
        Assert.Contains(new UnitStopped(1, Hero, new Cell(1, 0)), events);
        Assert.DoesNotContain(events, e => e is UnitMoved or SkillUsed);
        Assert.Equal(new Cell(1, 0), sim.Get(1).Position);
        Assert.Equal(Capacity / 2 + 10, sim.Get(1).Gauge); // comme après un déplacement seul, puis un tick de remplissage
    }

    [Fact]
    public void AnImmobilizedAiUnit_StaysPut_ThenComesCloser()
    {
        // Un ennemi à l'IA de base, trop loin pour frapper : immobilisé, il attend sur place.
        var sim = Simulation(new Grid(8, 1), HeroAt(0, 0), Unit(2, team: 1, 7, 0, initiative: 50, behavior: Behaviors.Basic));
        ImmobilizeTheVictim(sim);

        Assert.DoesNotContain(sim.TickTimes(19), e => e is UnitMoved); // ticks 2 à 20
        Assert.Equal(new Cell(7, 0), sim.Get(2).Position);

        Assert.Contains(sim.TickTimes(3), e => e is UnitMoved { Unit.Value: 2 });
    }

    [Fact]
    public void AnImmobilizedAiUnit_DoesNotCharge_ButStrikesWhatIsInReach()
    {
        CombatSimulation With(int enemyX) => Simulation(new Grid(8, 1), HeroAt(0, 0),
            Unit(2, team: 1, enemyX, 0, initiative: 50, behavior: Behaviors.Basic, skills: new[] { Strike, Rush }));

        // À 4 cases et aligné, libre de ses mouvements : il charge.
        Assert.Contains(With(enemyX: 4).TickTimes(5), e => e is SkillUsed { Unit.Value: 2, SkillId: "rush" });

        // Immobilisé, il ne lui reste que son coup au contact, hors de portée : il attend, puis agit une fois libéré.
        var far = With(enemyX: 4);
        ImmobilizeTheVictim(far);
        Assert.DoesNotContain(far.TickTimes(19), e => e is SkillUsed or UnitMoved or UnitDisplaced); // ticks 2 à 20
        Assert.Contains(far.TickTimes(3), e => e is SkillUsed { Unit.Value: 2 });

        // Au contact, immobilisé ou non, il frappe.
        var close = With(enemyX: 1);
        ImmobilizeTheVictim(close);
        Assert.Contains(close.TickTimes(5), e => e is SkillUsed { Unit.Value: 2, SkillId: "strike" });
    }

    [Fact]
    public void AFleeingUnit_IsHeldInPlace()
    {
        // Un coup qui le fait passer sous son seuil de fuite et l'immobilise : il ne s'enfuit qu'une fois libéré.
        var grab = Strike with { Id = "grab", MinDamage = 60, MaxDamage = 60, Effects = new[] { Immobilized } };
        var coward = Unit(2, team: 1, 4, 1, hp: 100, initiative: 50, behavior: Behaviors.Basic);
        var sim = Simulation(new Grid(9, 3), Unit(1, team: 0, 3, 1, gauge: Capacity, skills: new[] { grab }),
            coward with { Definition = coward.Definition with { FleeAtHpPercent = 50 } });

        sim.Submit(Use(Hero, "grab", Victim));
        Assert.Contains(sim.Tick(), e => e is UnitFleeing { Unit.Value: 2 });

        Assert.DoesNotContain(sim.TickTimes(19), e => e is UnitMoved or UnitEscaped); // ticks 2 à 20
        Assert.Contains(sim.TickTimes(3), e => e is UnitMoved { Unit.Value: 2 });
    }

    [Fact]
    public void AFleeingUnit_HeldOnTheEdgeOfTheMap_DoesNotEscape()
    {
        // Il est déjà au bord : sans l'immobilisation, il quitterait le combat à sa prochaine activation.
        var grab = Strike with { Id = "grab", MinDamage = 60, MaxDamage = 60, Effects = new[] { Immobilized } };
        var coward = Unit(2, team: 1, 0, 1, hp: 100, initiative: 50, behavior: Behaviors.Basic);
        var sim = Simulation(new Grid(9, 3), Unit(1, team: 0, 1, 1, gauge: Capacity, skills: new[] { grab }),
            coward with { Definition = coward.Definition with { FleeAtHpPercent = 50 } });

        sim.Submit(Use(Hero, "grab", Victim));
        Assert.Contains(sim.Tick(), e => e is UnitFleeing { Unit.Value: 2 });

        Assert.DoesNotContain(sim.TickTimes(19), e => e is UnitEscaped); // ticks 2 à 20 : retenu
        Assert.Contains(sim.TickTimes(3), e => e is UnitEscaped { Unit.Value: 2 });
    }
}
