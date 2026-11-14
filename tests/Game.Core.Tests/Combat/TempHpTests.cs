using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// PV temporaires (docs/design.md 2.7, Peau d'acier) : ils encaissent les dégâts avant les vrais PV, durent un temps,
/// ne se cumulent pas. Ici ils durent 1 s (20 ticks) : reçus au tick 1, ils disparaissent au tick 21.
/// </summary>
public class TempHpTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Enemy = new(2);

    /// <summary>La moitié des PV actuels du lanceur, pour lui-même.</summary>
    private static readonly SkillDefinition Guard = new()
    {
        Id = "guard", Target = SkillTargeting.Self, Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
        TempHp = new TempHpDefinition { PercentOfCasterHp = 50, DurationMs = 1000 },
    };

    /// <summary>La même chose, pour un allié.</summary>
    private static readonly SkillDefinition Ward = Guard with { Id = "ward", Target = SkillTargeting.Ally, Range = 3 };

    /// <summary>Un coup qui ignore les protections.</summary>
    private static readonly SkillDefinition Pierce = Strike with { Id = "pierce", IgnoresProtections = true };

    private static UnitSetup HeroAt(int x, int y, int hp = 100, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, hp: hp, gauge: Capacity, initiative: Capacity, onDefeat: DefeatOutcome.Downed,
            skills: skills.Length > 0 ? skills : new[] { Guard });

    private static UnitSetup EnemyAt(int x, int y, params SkillDefinition[] skills) =>
        Unit(2, team: 1, x, y, hp: 1000, gauge: Capacity, initiative: Capacity, skills: skills.Length > 0 ? skills : new[] { Strike, Smash });

    /// <summary>Le héros (100 PV) lance sa garde au tick 1 : 50 PV temporaires jusqu'au tick 21.</summary>
    private static CombatSimulation Guarded(params SkillDefinition[] enemySkills)
    {
        var sim = Simulation(new Grid(4, 1), HeroAt(0, 0), EnemyAt(1, 0, enemySkills));
        sim.Submit(Use(Hero, "guard"));
        Assert.Contains(new TempHpGained(1, Hero, 50, 21), sim.Tick());
        return sim;
    }

    [Fact]
    public void TheAmount_IsAShareOfTheCastersCurrentHp()
    {
        var sim = Simulation(new Grid(4, 1), HeroAt(0, 0), EnemyAt(1, 0));
        sim.Get(1).Hp = 60; // blessé : la garde protège moins

        sim.Submit(Use(Hero, "guard"));

        Assert.Contains(new TempHpGained(1, Hero, 30, 21), sim.Tick());
        Assert.Equal((60, 30), (sim.Get(1).Hp, sim.Get(1).TempHp));
    }

    [Fact]
    public void TemporaryHp_TakeTheDamageFirst_ThenTheRealHp()
    {
        var sim = Guarded(Strike, Smash, Smash with { Id = "smash-again" });

        sim.Submit(Use(Enemy, "strike", Hero)); // 10 : entièrement encaissé
        var first = sim.Tick();
        Assert.Contains(new DamageAbsorbed(2, Hero, 10, 40), first);
        Assert.DoesNotContain(first, e => e is UnitDamaged);
        Assert.Equal((100, 40), (sim.Get(1).Hp, sim.Get(1).TempHp));

        sim.Submit(Use(Enemy, "smash", Hero)); // 30 : il en reste 10
        sim.Tick();
        sim.Submit(Use(Enemy, "smash-again", Hero)); // 30 : 10 encaissés, 20 passent
        var last = sim.Tick();
        Assert.Contains(new DamageAbsorbed(4, Hero, 10, 0), last);
        Assert.Contains(new UnitDamaged(4, Hero, 20, 80), last);
    }

    [Fact]
    public void TemporaryHp_DisappearAfterTheirDuration()
    {
        var sim = Guarded();

        Assert.DoesNotContain(sim.TickTimes(19), e => e is TempHpEnded); // ticks 2 à 20
        Assert.Contains(new TempHpEnded(21, Hero), sim.Tick());
        Assert.Equal(0, sim.Get(1).TempHp);

        sim.Submit(Use(Enemy, "strike", Hero));
        Assert.Contains(new UnitDamaged(22, Hero, 10, 90), sim.Tick());
    }

    [Fact]
    public void TemporaryHp_DoNotStack_AnewGrantReplacesThem()
    {
        var sim = Guarded();
        sim.Submit(Use(Enemy, "strike", Hero));
        sim.Tick(); // il en reste 40

        sim.Submit(Use(Hero, "guard"));
        var events = sim.Tick();

        Assert.Contains(new TempHpGained(3, Hero, 50, 23), events); // remis à neuf, pas 90, et la durée repart
        Assert.Equal(50, sim.Get(1).TempHp);
    }

    [Fact]
    public void AHeal_DoesNotRefillTemporaryHp()
    {
        var patch = new SkillDefinition
        {
            Id = "patch", Target = SkillTargeting.Ally, Weight = SkillWeight.Light, Range = 3,
            MinDamage = 0, MaxDamage = 0, MinHeal = 20, MaxHeal = 20, CooldownMs = 0,
        };
        var sim = Simulation(new Grid(4, 2), HeroAt(0, 0), EnemyAt(1, 0),
            Unit(3, team: 0, 0, 1, gauge: Capacity, skills: new[] { patch }));
        sim.Submit(Use(Hero, "guard"));
        sim.Submit(Use(Enemy, "strike", Hero));
        sim.Tick(); // 40 PV temporaires, PV intacts

        // Ses vrais PV sont pleins : le soin n'aurait aucun effet, il est refusé.
        var heal = Use(new UnitId(3), "patch", Hero);
        sim.Submit(heal);

        Assert.Equal(new CommandRejected(2, heal, RejectionReason.NoEffect), sim.Tick().Single());
        Assert.Equal(40, sim.Get(1).TempHp);
    }

    [Fact]
    public void AHitThatIgnoresProtections_GoesThroughTemporaryHp()
    {
        var sim = Guarded(Pierce);

        sim.Submit(Use(Enemy, "pierce", Hero));
        var events = sim.Tick();

        Assert.Contains(new UnitDamaged(2, Hero, 10, 90), events);
        Assert.DoesNotContain(events, e => e is DamageAbsorbed);
        Assert.Equal(50, sim.Get(1).TempHp);
    }

    [Fact]
    public void AHitThatIgnoresProtections_GoesThroughADome_WithoutUsingItUp()
    {
        var dome = new SkillDefinition
        {
            Id = "dome", Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false },
            CreatesDome = true,
        };
        var sim = Simulation(new Grid(4, 1), HeroAt(0, 0, 100, dome), EnemyAt(1, 0, Pierce, Strike));
        sim.Submit(Use(Hero, "dome", cell: new Cell(0, 0)));
        sim.Tick();

        sim.Submit(Use(Enemy, "pierce", Hero));
        var pierced = sim.Tick();
        Assert.Contains(new UnitDamaged(2, Hero, 10, 90), pierced);
        Assert.DoesNotContain(pierced, e => e is HitBlocked or DomeEnded);

        sim.Submit(Use(Enemy, "strike", Hero)); // un coup ordinaire, lui, est bloqué
        Assert.Contains(sim.Tick(), e => e is HitBlocked);
    }

    [Fact]
    public void AnExecution_IsNotHeldBackByTemporaryHp()
    {
        var execution = Smash with { Id = "execution", ExecuteAtOrBelowPercent = 30, CooldownMs = 0 };
        var sim = Simulation(new Grid(4, 1), HeroAt(0, 0), EnemyAt(1, 0, execution));
        sim.Get(1).Hp = 20; // sous le seuil d'exécution
        sim.Submit(Use(Hero, "guard"));
        sim.Tick();

        sim.Submit(Use(Enemy, "execution", Hero));
        var events = sim.Tick();

        Assert.Contains(events, e => e is UnitExecuted);
        Assert.Equal((UnitStatus.Downed, 0, 0), (sim.Get(1).Status, sim.Get(1).Hp, sim.Get(1).TempHp)); // à terre : plus de PV temporaires
    }

    [Fact]
    public void GivenToAnAlly_TheAmountStillComesFromTheCaster()
    {
        var sim = Simulation(new Grid(4, 2), HeroAt(0, 0, 100, Ward), EnemyAt(3, 0), Unit(3, team: 0, 0, 1, hp: 40));

        sim.Submit(Use(Hero, "ward", new UnitId(3)));

        Assert.Contains(new TempHpGained(1, new UnitId(3), 50, 21), sim.Tick()); // la moitié des 100 PV du lanceur
        Assert.Equal(0, sim.Get(1).TempHp);
    }

    [Fact]
    public void AFixedAmount_IsMultipliedByThePowerOfTheCaster()
    {
        var shield = Ward with { Id = "shield", TempHp = new TempHpDefinition { Min = 10, Max = 10, DurationMs = 1000 } };
        var sim = Simulation(new Grid(4, 2),
            Unit(1, team: 0, 0, 0, gauge: Capacity, skills: new[] { shield }, points: new StatPoints { Power = 5 }),
            Unit(3, team: 0, 0, 1));

        sim.Submit(Use(Hero, "shield", new UnitId(3)));

        Assert.Contains(new TempHpGained(1, new UnitId(3), 15, 21), sim.Tick()); // 10 × 150 %
    }

    [Fact]
    public void AShieldBeacon_PulsesWhenAnAllyAroundHasNoTemporaryHp()
    {
        var pulse = new SkillDefinition
        {
            Id = "pulse", Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            TempHp = new TempHpDefinition { Min = 10, Max = 10, DurationMs = 5000 },
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Allies },
        };
        var beacon = Unit(1, team: 0, 1, 0, initiative: 50, mobility: 0, behavior: Behaviors.Beacon, skills: new[] { pulse });
        var sim = Simulation(new Grid(4, 1), beacon, Unit(3, team: 0, 0, 0));

        var events = sim.TickTimes(10);

        // Une seule impulsion : ensuite l'allié a déjà ses PV temporaires, la balise attend.
        Assert.Equal(new[] { new UnitId(3) }, events.OfType<TempHpGained>().Select(e => e.Unit));
        Assert.Single(events.OfType<SkillUsed>());
    }

    [Fact]
    public void AWeakerGrant_DoesNotDestroyABetterProtection()
    {
        // Le héros a 50 PV temporaires ; un allié lui en offre 10 : il garde les siens, et leur durée.
        var shield = Ward with { Id = "shield", TempHp = new TempHpDefinition { Min = 10, Max = 10, DurationMs = 5000 } };
        var sim = Simulation(new Grid(4, 2), HeroAt(0, 0), EnemyAt(3, 0), Unit(3, team: 0, 0, 1, gauge: Capacity, skills: new[] { shield }));
        sim.Submit(Use(Hero, "guard"));
        sim.Tick();

        sim.Submit(Use(new UnitId(3), "shield", Hero));
        var events = sim.Tick();

        Assert.DoesNotContain(events, e => e is TempHpGained);
        Assert.Equal((50, 21), (sim.Get(1).TempHp, sim.Get(1).TempHpEndTick));
    }

    [Fact]
    public void TheAmount_IsAtLeastOne()
    {
        var sim = Simulation(new Grid(4, 1), HeroAt(0, 0), EnemyAt(1, 0));
        sim.Get(1).Hp = 1; // la moitié de 1 PV : arrondie à 0, portée à 1

        sim.Submit(Use(Hero, "guard"));

        Assert.Contains(new TempHpGained(1, Hero, 1, 21), sim.Tick());
    }

    [Fact]
    public void AHitFullyAbsorbed_DoesNotExposeItsTarget()
    {
        // L'attaquant a le passif Point faible : seul un coup qui entame vraiment les PV rend la cible vulnérable.
        var sim = Simulation(new Grid(4, 1), HeroAt(0, 0),
            Unit(2, team: 1, 1, 0, gauge: Capacity, initiative: Capacity, skills: new[] { Strike, Smash, Smash with { Id = "smash-again" } },
                passive: PassiveKind.WeakPoint));
        sim.Submit(Use(Hero, "guard"));
        sim.Tick(); // 50 PV temporaires

        sim.Submit(Use(Enemy, "smash", Hero)); // 30 : entièrement encaissés
        Assert.DoesNotContain(sim.Tick(), e => e is StatusApplied { Status: StatusKind.Exposed });

        sim.Submit(Use(Enemy, "smash-again", Hero)); // 30 : 20 encaissés, 10 passent
        Assert.Contains(sim.Tick(), e => e is StatusApplied { Unit.Value: 1, Status: StatusKind.Exposed });
    }

    [Fact]
    public void AShieldBeacon_DoesNotSpendItselfOnGadgets()
    {
        var pulse = new SkillDefinition
        {
            Id = "pulse", Weight = SkillWeight.Light, Range = 0, MinDamage = 0, MaxDamage = 0, CooldownMs = 0,
            TempHp = new TempHpDefinition { Min = 10, Max = 10, DurationMs = 5000 },
            Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false, Affects = ZoneTargets.Allies },
        };
        var beacon = new UnitDefinition
        {
            Id = "beacon", Name = "Balise", MaxHp = 20, Initiative = 50, Mobility = 0, OnDefeat = DefeatOutcome.Removed,
            BasicAttack = null, OpportunityAttacksPerCycle = 0, Behavior = Behaviors.Beacon, Skills = new[] { pulse },
        };
        SkillDefinition Place(string id, UnitDefinition unit) => new()
        {
            Id = id, Weight = SkillWeight.Light, Range = 3, MinDamage = 0, MaxDamage = 0, CooldownMs = 0, Summon = unit,
        };
        // Le tech pose deux balises côte à côte, loin de lui : chacune n'a que l'autre gadget à protéger.
        var sim = Simulation(new Grid(8, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, initiative: Capacity,
                skills: new[] { Place("place-a", beacon), Place("place-b", beacon with { Id = "beacon-b" }) }));
        sim.Submit(Use(Hero, "place-a", cell: new Cell(2, 0)));
        sim.Tick();
        sim.Submit(Use(Hero, "place-b", cell: new Cell(3, 0)));
        sim.Tick();

        Assert.DoesNotContain(sim.TickTimes(20), e => e is TempHpGained or SkillUsed { SkillId: "pulse" });
    }

    [Fact]
    public void TheData_AreChecked()
    {
        Guard.Validate();
        // Les PV temporaires sont un soutien, jamais sur une attaque.
        Assert.Throws<ArgumentException>(() => (Strike with { Id = "x", TempHp = Guard.TempHp }).Validate());
        // Une part des PV du lanceur, ou un montant fixe : pas les deux, pas aucun.
        Assert.Throws<ArgumentException>(() => (Guard with { TempHp = new TempHpDefinition { PercentOfCasterHp = 50, Min = 5, Max = 5, DurationMs = 1000 } }).Validate());
        Assert.Throws<ArgumentException>(() => (Guard with { TempHp = new TempHpDefinition { DurationMs = 1000 } }).Validate());
        Assert.Throws<ArgumentException>(() => (Guard with { TempHp = new TempHpDefinition { PercentOfCasterHp = 50, DurationMs = 0 } }).Validate());
        // Ni pour un ennemi (télékinésie), ni sur une compétence qui ne passe pas par un coup (sauvetage).
        Assert.Throws<ArgumentException>(() => (Ward with { Target = SkillTargeting.Any, ForcedMove = new ForcedMoveDefinition { Type = ForcedMoveType.Place, Distance = 2 } }).Validate());
        Assert.Throws<ArgumentException>(() => (Ward with { RescueDropRadius = 2 }).Validate());
        // Ignorer les protections : un coup qui fait des dégâts à un seul ennemi.
        Assert.Throws<ArgumentException>(() => (Guard with { IgnoresProtections = true }).Validate());
        Assert.Throws<ArgumentException>(() => (Pierce with { Range = 3, SelfMove = SelfMoveType.Pierce }).Validate());
    }
}
