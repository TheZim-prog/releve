using Game.Core.Combat;
using Game.Core.Grids;
using static Game.Core.Tests.Combat.CombatTestKit;

namespace Game.Core.Tests.Combat;

/// <summary>
/// Passifs de dégâts. Règles de test : Rage jusqu'à +100 % ; Opportuniste x1,5 ; Premier sang x2 ; Embusqué x1,5 ;
/// Seul contre tous et Écran cinétique x0,5 ; Fournaise 5 ; Réaction en chaîne 10 ; Givre et Carapace de givre 1 s.
/// Le coup de base, « strike », fait 10 dégâts.
/// </summary>
public class DamagePassiveTests
{
    private static readonly UnitId Hero = new(1);
    private static readonly UnitId Enemy = new(2);

    private static readonly SkillDefinition Shot = Strike with { Id = "shot", Range = 5 };

    private static UnitSetup HeroWith(PassiveKind? passive, int x = 0, int y = 0, int hp = 100, params SkillDefinition[] skills) =>
        Unit(1, team: 0, x, y, hp: hp, gauge: Capacity, initiative: Capacity, passive: passive, skills: skills.Length > 0 ? skills : null);

    /// <summary>Dégâts du premier coup du héros sur l'ennemi (unité 2).</summary>
    private static int Damage(CombatSimulation sim, string skill = "strike")
    {
        sim.Submit(Use(Hero, skill, Enemy));
        return sim.Tick().OfType<UnitDamaged>().First(e => e.Unit == Enemy).Amount;
    }

    [Theory]
    [InlineData(100, 10)] // PV pleins : pas de bonus
    [InlineData(50, 15)]  // la moitié de ses PV perdus : la moitié du bonus
    [InlineData(1, 19)]   // presque à terre : presque le double
    public void Rage_TheLowerHisHp_TheHarderHeHits(int hp, int damage)
    {
        var sim = Simulation(new Grid(4, 1), HeroWith(PassiveKind.Rage), Unit(2, team: 1, 1, 0, hp: 1000));
        sim.Get(1).Hp = hp;

        Assert.Equal(damage, Damage(sim));
    }

    [Theory]
    [InlineData(StatusKind.Stunned, 15)]
    [InlineData(StatusKind.OffBalance, 15)]
    [InlineData(StatusKind.Immobilized, 15)]
    [InlineData(StatusKind.Disarmed, 10)] // désarmée : pas une cible « contrôlée »
    public void Opportunist_HitsHarderATargetThatIsControlled(StatusKind status, int damage)
    {
        var control = Strike with { Id = "control", MinDamage = 0, MaxDamage = 0, Effects = new[] { new StatusEffect { Status = status, DurationMs = 5000 } } };
        var sim = Simulation(new Grid(4, 1), HeroWith(PassiveKind.Opportunist, skills: new[] { Strike, control }), Unit(2, team: 1, 1, 0, hp: 1000));
        sim.Submit(Use(Hero, "control", Enemy));
        sim.Tick();

        Assert.Equal(damage, Damage(sim));
    }

    [Fact]
    public void FirstBlood_HitsHarderAnEnemyWithFullHp_OnlyOnce()
    {
        var sim = Simulation(new Grid(4, 1), HeroWith(PassiveKind.FirstBlood), Unit(2, team: 1, 1, 0, hp: 1000));

        Assert.Equal(20, Damage(sim));
        Assert.Equal(10, Damage(sim)); // il n'est plus intact
    }

    [Fact]
    public void Ambusher_AgainstTheScenery_HisRangedHitsAreStronger()
    {
        // Sur la rangée du bas, un mur en (0,0) juste au-dessus de la case (0,1).
        Grid Map() => Grid.FromRows("#.....", "......");
        CombatSimulation From(int x, int enemyX) => Simulation(Map(),
            HeroWith(PassiveKind.Ambusher, x, 1, skills: new[] { Shot }), Unit(2, team: 1, enemyX, 1, hp: 1000));

        Assert.Equal(15, Damage(From(x: 0, enemyX: 4), "shot")); // collé au mur, de loin
        Assert.Equal(10, Damage(From(x: 3, enemyX: 5), "shot")); // à découvert
        Assert.Equal(10, Damage(From(x: 0, enemyX: 1), "shot")); // collé au mur, mais au contact
    }

    [Fact]
    public void AloneAgainstAll_TakesLess_WithAtLeastTwoEnemiesInContact()
    {
        CombatSimulation With(params UnitSetup[] others) => Simulation(new Grid(4, 2),
            new[] { Unit(1, team: 1, 0, 0, gauge: Capacity), Unit(2, team: 0, 1, 0, hp: 1000, passive: PassiveKind.AloneAgainstAll) }
                .Concat(others).ToArray());

        Assert.Equal(10, Damage(With()));                        // un seul ennemi au contact
        Assert.Equal(5, Damage(With(Unit(3, team: 1, 1, 1))));   // deux
        Assert.Equal(10, Damage(With(Unit(3, team: 1, 3, 1))));  // le second est trop loin
    }

    [Fact]
    public void KineticScreen_TakesLessFromHitsThatComeFromAfar()
    {
        CombatSimulation At(int x) => Simulation(new Grid(6, 1),
            Unit(1, team: 1, 0, 0, gauge: Capacity, skills: new[] { Shot }), Unit(2, team: 0, x, 0, hp: 1000, passive: PassiveKind.KineticScreen));

        Assert.Equal(5, Damage(At(4), "shot"));  // de loin
        Assert.Equal(10, Damage(At(1), "shot")); // au contact
    }

    [Fact]
    public void Piercing_IgnoresCover()
    {
        // L'ennemi est collé à un obstacle bas, sur la ligne du tir.
        CombatSimulation With(PassiveKind? passive) => Simulation(Grid.FromRows("...o.."),
            HeroWith(passive, skills: new[] { Shot }), Unit(2, team: 1, 4, 0, hp: 1000));

        Assert.Equal(5, Damage(With(null), "shot"));
        Assert.Equal(10, Damage(With(PassiveKind.Piercing), "shot"));
    }

    [Fact]
    public void Furnace_AfterEachAction_BurnsTheEnemiesInContact()
    {
        var sim = Simulation(new Grid(5, 2), HeroWith(PassiveKind.Furnace, skills: new[] { Shot }),
            Unit(2, team: 1, 0, 1, hp: 100),  // au contact
            Unit(3, team: 1, 1, 1, hp: 100),  // au contact, en diagonale
            Unit(4, team: 1, 4, 0, hp: 100),  // la cible du tir, loin
            Unit(5, team: 0, 1, 0, hp: 100)); // un allié au contact

        sim.Submit(Use(Hero, "shot", new UnitId(4)));
        var burnt = sim.Tick().OfType<UnitDamaged>().Where(e => e.Amount == 5).Select(e => e.Unit.Value).ToArray();

        Assert.Equal(new[] { 2, 3 }, burnt);

        sim.Submit(new ActivateCommand(Hero, new Cell(2, 0)));
        Assert.DoesNotContain(sim.Tick(), e => e is UnitDamaged); // un déplacement seul ne brûle personne
    }

    [Fact]
    public void ChainReaction_AnEnemyTakenOut_ExplodesOnItsNeighbours_AndTheyCanExplodeToo()
    {
        var sim = Simulation(new Grid(6, 2), HeroWith(PassiveKind.ChainReaction),
            Unit(2, team: 1, 1, 0, hp: 5),    // achevé par le coup
            Unit(3, team: 1, 2, 0, hp: 5),    // voisin : achevé par l'explosion, explose à son tour
            Unit(4, team: 1, 3, 0, hp: 100),  // voisin du voisin : blessé par la seconde explosion
            Unit(5, team: 0, 1, 1, hp: 100)); // un allié voisin : épargné

        sim.Submit(Use(Hero, "strike", Enemy));
        var events = sim.Tick();

        Assert.Equal(new[] { new Cell(1, 0), new Cell(2, 0) }, events.OfType<ChainExplosion>().Select(e => e.Cell));
        Assert.Contains(new UnitDamaged(1, new UnitId(4), 10, 90), events);
        Assert.DoesNotContain(events, e => e is UnitDamaged { Unit.Value: 5 or 1 });
    }

    [Fact]
    public void ChainReaction_InAZone_AnEnemyAlreadyBlownUp_IsNotHitAgain()
    {
        // Les deux ennemis sont dans la zone et voisins : le coup achève le premier, dont l'explosion achève le second.
        var blast = Strike with { Id = "blast", Range = 3, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false } };
        var sim = Simulation(new Grid(6, 2), HeroWith(PassiveKind.ChainReaction, skills: new[] { blast }),
            Unit(2, team: 1, 3, 0, hp: 5), Unit(3, team: 1, 3, 1, hp: 5));

        sim.Submit(Use(Hero, "blast", cell: new Cell(3, 0)));
        var events = sim.Tick();

        Assert.Equal(2, events.OfType<UnitRemoved>().Count());
        Assert.Equal(2, events.OfType<ChainExplosion>().Count()); // une explosion par ennemi, pas davantage
        Assert.Single(events.OfType<UnitDamaged>(), e => e.Unit == new UnitId(3));
    }

    [Fact]
    public void KineticScreen_AlsoCountsAZoneThrownFromAfar()
    {
        var blast = Strike with { Id = "blast", Range = 5, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false } };
        var sim = Simulation(new Grid(6, 1),
            Unit(1, team: 1, 0, 0, gauge: Capacity, skills: new[] { blast }), Unit(2, team: 0, 4, 0, hp: 1000, passive: PassiveKind.KineticScreen));

        sim.Submit(Use(Hero, "blast", cell: new Cell(4, 0))); // centrée sur lui, lancée de 4 cases

        Assert.Equal(5, sim.Tick().OfType<UnitDamaged>().Single().Amount);
    }

    [Fact]
    public void AnOffensivePassive_DoesNotBoostFriendlyFire()
    {
        // Premier sang : un allié intact pris dans sa zone ne prend pas plus cher.
        var blast = Strike with { Id = "blast", Range = 3, Zone = new ZoneDefinition { Shape = ZoneShape.Square, Size = 1, ThroughWalls = false } };
        var sim = Simulation(new Grid(6, 1), HeroWith(PassiveKind.FirstBlood, skills: new[] { blast }),
            Unit(2, team: 1, 3, 0, hp: 1000), Unit(3, team: 0, 2, 0, hp: 1000));

        sim.Submit(Use(Hero, "blast", cell: new Cell(3, 0)));
        var damage = sim.Tick().OfType<UnitDamaged>().ToDictionary(e => e.Unit.Value, e => e.Amount);

        Assert.Equal((20, 10), (damage[2], damage[3]));
    }

    [Fact]
    public void Frost_TheBasicAttackSlowsItsTarget_NotTheOtherSkills()
    {
        var sim = Simulation(new Grid(4, 1),
            Unit(1, team: 0, 0, 0, gauge: Capacity, initiative: Capacity, passive: PassiveKind.Frost, basicAttack: "strike"),
            Unit(2, team: 1, 1, 0, hp: 1000));

        sim.Submit(Use(Hero, "smash", Enemy));
        Assert.DoesNotContain(sim.Tick(), e => e is StatusApplied);

        sim.Submit(Use(Hero, "strike", Enemy));
        Assert.Contains(new StatusApplied(2, Enemy, StatusKind.OffBalance, 22), sim.Tick()); // 1 s = 20 ticks
    }

    [Fact]
    public void FrostShell_WhoStrikesHimInContact_IsSlowed()
    {
        CombatSimulation At(int x) => Simulation(new Grid(6, 1),
            Unit(1, team: 1, 0, 0, gauge: Capacity, skills: new[] { Shot }), Unit(2, team: 0, x, 0, hp: 1000, passive: PassiveKind.FrostShell));

        var close = At(1);
        close.Submit(Use(Hero, "shot", Enemy));
        Assert.Contains(new StatusApplied(1, Hero, StatusKind.OffBalance, 21), close.Tick());

        var far = At(4);
        far.Submit(Use(Hero, "shot", Enemy));
        Assert.DoesNotContain(far.Tick(), e => e is StatusApplied);
    }

    [Fact]
    public void TheRulesOfThePassives_AreChecked()
    {
        Rules.Validate();
        Assert.Throws<ArgumentException>(() => (Rules with { FirstBloodDamagePercent = 90 }).Validate());           // un bonus n'affaiblit pas
        Assert.Throws<ArgumentException>(() => (Rules with { KineticScreenDamageTakenPercent = 120 }).Validate());  // une réduction n'aggrave pas
        Assert.Throws<ArgumentException>(() => (Rules with { FurnaceMinDamage = 6 }).Validate());                   // min > max
        Assert.Throws<ArgumentException>(() => (Rules with { FrostDurationMs = 0 }).Validate());
    }
}
